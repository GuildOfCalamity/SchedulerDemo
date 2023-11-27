using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SchedulerDemo;

/// <summary>
/// This is a time based list version of my original ScheduleQueue.
/// It will only run one thread which monitors the list for something to do.
/// If an <see cref="ActionItem"/> is found that matches the criteria then
/// it will be invoked inside of a <see cref="Task"/>.
/// </summary>
public class ScheduleList
{
    #region [Properties and Events]
    bool _debug = false;
    bool _shutdown = false;
    bool _cleanup = true;
    bool _suspended = false;
    bool _busy = false;
    readonly object _locker = new object();
    Thread _agent;
    // NOTE: If you don't want to use a locking object, a BlockingCollection<ActionItem> could be used instead.
    List<ActionItem> _itemList = new List<ActionItem>();
    // This is a simple event that a WinForm/WPF/UWP application can hook to update the UI when an action runs.
    public event Action<object, string> OnInvoke = (item, msg) => { };
    public event Action<object, string> OnCancel = (item, msg) => { };
    public event Action<object?, string> OnError = (item, msg) => { };
    public event Action<string> OnShutdownComplete = (msg) => { };
    public event Action<string> OnScheduleExhausted = (msg) => { };
    #endregion

    /// <summary>
    /// Default Constructor
    /// </summary>
    public ScheduleList()
    {
        _agent = new Thread(Loop)
        {
            IsBackground = true,
            Name = $"{nameof(ActionItem)}Monitor",
            Priority = ThreadPriority.BelowNormal
        };
        _agent.Start();
    }

    /// <summary>
    /// Secondary Constructor
    /// </summary>
    public ScheduleList(bool autoRemove = true, bool debugMode = false) : this()
    {
        _debug = debugMode;
        _cleanup = autoRemove;
    }

    /// <summary>
    /// Adds an <see cref="ActionItem"/> to the <see cref="List{T}"/>.
    /// </summary>
    /// <param name="item"><see cref="ActionItem"/></param>
    public void ScheduleItem(ActionItem item)
    {
        // Add basic checks for first-time users not familiar with the order of operations.
        if (_shutdown)
            throw new Exception($"Thread has been shutdown, you must create a new {nameof(ScheduleList)}.");

        lock (_locker)
        {
            _itemList.Add(item);
        }
    }

    /// <summary>
    /// The main loop for our agent thread.
    /// </summary>
    private void Loop()
    {
        while (!_shutdown)
        {
            lock (this)
            {
                // Loop until not suspended.
                while (_suspended)
                {
                    if (_debug)
                        Debug.WriteLine($"> {_agent.Name} is paused.");

                    // Suspend thread execution.
                    Monitor.Wait(this, 500);

                    // If shutting down resume the thread.
                    if (_shutdown)
                        _suspended = false;
                }
            }

            // Is there anything to do?
            if (GetCount() > 0)
            {
                #region [Single Thread Servicing Method]
                // Keep checking the list until we have an item that's ready…
                for (int idx = 0; idx < GetCount(); idx++)
                {
                    // Make a copy to avoid any lambda trapping.
                    ActionItem item = _itemList[idx];

                    if (item != null && item.RunTime <= DateTime.Now && !item.Activated)
                    {
                        // Since we're no longer starting a thread for each ActionItem
                        // we need a way to determine if it is currently running so we
                        // don't kick off another task while it is already running.
                        item.Activated = true;

                        if (_debug)
                            Debug.WriteLine($"{nameof(ActionItem)} #{item.Id} is ready, running now…");

                        // Configure the cancellation token registration for the ActionItem…
                        CancellationTokenRegistration ctr = item.Token.Register(() =>
                        {
                            if (_debug)
                                Debug.WriteLine($"{nameof(ActionItem)} #{item.Id} was cancelled.");

                            OnCancel?.Invoke(item, $"#{item.Id} was canceled! [{DateTime.Now}]");
                        });

                        // We need to start the action on another thread in the event
                        // that said action takes a long time to run which would result
                        // in blocking the monitor thread too long to service the list.
                        Task.Run(() => {
                            try
                            {
                                _busy = true;
                                OnInvoke?.Invoke(item, $"Invoking #{item.Id} [{DateTime.Now}]");
                                item.ToRun?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[{item?.Id}]: {ex.Message}");
                                OnError?.Invoke(item, $"#{item?.Id} caused exception: {ex.Message}");
                            }
                            finally
                            {
                                if (_debug)
                                    Debug.WriteLine($"{nameof(ActionItem)} #{item.Id} is now complete and " + (_cleanup ? "will" : "will not") + " be removed from the list.");
                                // We may want the system not to remove the ActionItems from
                                // the list so that metrics can be collected for reporting.
                                if (_cleanup)
                                    _itemList.Remove(item);
                            }
                        }, item.Token).ContinueWith((t) =>
                        {
                            if (_debug)
                                Debug.WriteLine($"{nameof(ActionItem)} #{item.Id} status: {t.Status}");
                            // Be sure to dispose of the CancellationTokenRegistration so
                            // it doesn't hang around in memory after the task is gone.
                            ctr.Dispose();
                            _busy = false;
                        });

                        // Is there anything left to run?
                        if (GetNextToRun() == null)
                        {
                            // There may be an action still running. This only indicates that there are no more unactivated ActionItems.
                            OnScheduleExhausted?.Invoke($"All {nameof(ActionItem)}'s have been exhausted. [{DateTime.Now}]");
                        }
                    }

                    // If the list contained thousands of items then
                    // it might be wise to check the flag mid-forloop.
                    if (_shutdown) { break; }
                }

                if (_shutdown)
                {
                    // If auto-cleaning is enabled, the list count has different meaning…
                    if (_debug && _cleanup && (GetCount() > 0))
                        Debug.WriteLine($"[WARNING]: Abandoning {GetCount()} scheduled {nameof(ActionItem)}s.");
                    else if (_debug && !_cleanup && (GetWaiting().Count() > 0))
                        Debug.WriteLine($"[WARNING]: Abandoning {GetWaiting().Count()} {nameof(ActionItem)}s.");
                    return;
                }
                #endregion

                #region [Setup And Defer Method]
                //
                // NOTE: This solves the issue of needing an Actived flag, but it's costly in
                //       terms of resources! This would not be wise to use with large amounts
                //       of ActionItems. This can also result in resource fighting, depending
                //       on what each action code delegate is targeting.
                /*
                var rez = _itemList.Select(i => i).Where(i => i != null).OrderBy(i => i.RunTime);
                foreach(var ai in rez)
                {
                    // Extract item info...
                    var id = ai.Id;
                    var time = ai.RunTime;

                    // Spin up a task to run the action when it's ready...
                    Task.Run(() => 
                    {
                        if (time <= DateTime.Now)
                        {
                            if (_debug)
                                Debug.WriteLine($"{nameof(ActionItem)} #{id} is behind schedule, running it now...");

                            try { ai.ToRun.Invoke(); }
                            catch (Exception ex) { Debug.WriteLine($"[{id)]: {ex.Message}"); }
                        }
                        else
                        {
                            if (_debug)
                                Debug.WriteLine($"{nameof(ActionItem)} #{id} will run at {time.ToLongTimeString()}, waiting...");

                            // Wait for the time requested...
                            while (time > DateTime.Now)
                            {
                                Thread.Sleep(10);
                            }

                            if (_debug)
                                Debug.WriteLine($"Finished waiting, invoking #{id} at {DateTime.Now.ToLongTimeString()}...");

                            try { ai.ToRun.Invoke(); }
                            catch (Exception ex) { Debug.WriteLine($"[{nameof(ActionItem)}.Invoke]: {ex.Message}"); }
                        }
                    });
                }
                // All ActionItems have been pre-processed.
                _itemList.Clear();
                */
                #endregion
            }

            // Go easy on the CPU. This is also our resolution, i.e.
            // the accuracy +/- when the ActionItems are fired off.
            Thread.Sleep(100);
        }

        OnShutdownComplete?.Invoke($"{nameof(_agent)} thread finished. [{DateTime.Now}]");
    }

    /// <summary>
    /// Signal the agent thread to close shop.
    /// </summary>
    /// <param name="waitForRemaining">To block or not to block, that is the question.</param>
    public void Shutdown(bool waitForRemaining)
    {
        // Add basic checks for first-time users not familiar with the order of operations.
        if (_agent == null)
            return;

        // Signal our thread loop.
        _shutdown = true;

        // Wait for tasks to finish.
        if (waitForRemaining)
        {
            if (_debug)
                Debug.WriteLine($"> Joining {nameof(_agent)} thread… ");

            _agent.Join();
        }

    }

    /// <summary>
    /// Change the agent thread's suspended/running state.
    /// </summary>
    public void Toggle()
    {
        // Add basic checks for first-time users not familiar with the order of operations.
        if (_agent == null)
            return;

        _suspended = !_suspended;

        if (_debug)
            Debug.WriteLine($"> {_agent.Name} has been {(_suspended ? "paused" : "unpaused")}.");

        lock (this)
        {
            // If thread resumed, notify state change.
            if (!_suspended)
                Monitor.Pulse(this);
        }
    }

    #region [Helper Methods]
    /// <summary>
    /// Returns the number of <see cref="ActionItem"/>s in the <see cref="List{ActionItem}"/>.
    /// </summary>
    public int GetCount()
    {
        lock (_locker)
        {
            return _itemList.Count;
        }
    }

    /// <summary>
    /// Returns the <see cref="ActionItem"/>s with the nearest <see cref="ActionItem.RunTime"/>.
    /// </summary>
    public DateTime? GetNextToRun()
    {
        if (GetCount() > 0)
        {
            var rez = _itemList
                .Select(i => i)
                .Where(i => i != null && !i.Activated)
                .OrderBy(i => i.RunTime)
                .FirstOrDefault();

            if (rez != null)
                return rez.RunTime;
        }
        return null;
    }

    /// <summary>
    /// Returns the <see cref="ActionItem"/>s with the farthest <see cref="ActionItem.RunTime"/>.
    /// </summary>
    public DateTime? GetLastToRun()
    {
        if (GetCount() > 0)
        {
            var rez = _itemList
                .Select(i => i)
                .Where(i => i != null && !i.Activated)
                .OrderByDescending(i => i.RunTime)
                .FirstOrDefault();

            if (rez != null)
                return rez.RunTime;
        }
        return null;
    }

    /// <summary>
    /// Removes all <see cref="ActionItem"/>s from the <see cref="List{ActionItem}"/>.
    /// </summary>
    /// <returns>true if any items were cleared, false otherwise</returns>
    public bool ClearSchedule()
    {
        if (GetCount() > 0)
        {
            lock (_locker)
            {
                _itemList.Clear();
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the amount of <see cref="ActionItem"/>s which are activated.
    /// </summary>
    /// <returns>-1 if empty or result is null, otherwise the activated amount</returns>
    public int GetActivatedCount()
    {
        if (GetCount() > 0)
        {
            var rez = _itemList
                .Select(i => i)
                .Where(i => i != null && i.Activated);

            if (rez != null)
                return rez.Count();
        }
        return 0;
    }

    /// <summary>
    /// Returns the amount of <see cref="ActionItem"/>s which are not activated.
    /// </summary>
    /// <returns>0 if empty or result is null, otherwise the unactivated amount</returns>
    public int GetUnActivatedCount()
    {
        if (GetCount() > 0)
        {
            var rez = _itemList
                .Select(i => i)
                .Where(i => i != null && !i.Activated);

            if (rez != null)
                return rez.Count();
        }
        return 0;
    }

    /// <summary>
    /// Returns the <see cref="ActionItem"/>s which have yet to be activated.
    /// </summary>
    public IEnumerable<ActionItem> GetWaiting()
    {
        if (GetCount() > 0)
        {
            var rez = _itemList
                .Select(i => i)
                .Where(i => i != null && !i.Activated)
                .OrderBy(i => i.RunTime);

            if (rez != null)
                return rez;
        }
        return Enumerable.Empty<ActionItem>();
    }

    /// <summary>
    /// Have any <see cref="ActionItem"/>s been activated yet?
    /// This does not determine if an <see cref="ActionItem"/> is currently running.
    /// </summary>
    /// <returns>true if any <see cref="ActionItem"/>s have been activated, false otherwise</returns>
    public bool IsActived()
    {
        if (GetCount() > 0)
        {
            var rez = _itemList.Select(i => i).Where(i => i != null && i.Activated);
            if (rez != null)
                return rez.Any();
        }
        return false;
    }

    /// <summary>
    /// Determine if auto-removal of <see cref="ActionItem"/>s from the <see cref="List{ActionItem}"/> is enabled.
    /// </summary>
    /// <returns>true if enabled, false otherwise</returns>
    public bool IsAutoCleanEnabled() => _cleanup;

    /// <summary>
    /// Is the agent thread alive?
    /// </summary>
    /// <returns>true if agent thread is running, false otherwise</returns>
    public bool IsThreadAlive() => (_agent != null) ? _agent.IsAlive : false;

    /// <summary>
    /// Is our agent thread suspended?
    /// </summary>
    /// <returns>true if agent thread is running, false otherwise</returns>
    public bool IsAgentAlive() => !_suspended;

    /// <summary>
    /// Are any tasks currently running?
    /// </summary>
    /// <returns>true if a task is running, false otherwise</returns>
    public bool IsBusy() => _busy;

    #endregion
}
