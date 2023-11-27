using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SchedulerDemo;

/// <summary>
/// This class is desinged to directly update a <see cref="System.Windows.Controls.TextBlock"/>
/// control via the <see cref="PerformanceMonitor.OneArgDelegate"/>. A public property 
/// <see cref="PerformanceMonitor.Usage"/> is also exposed for direct access to the unformatted
/// CPU usage percentage. The delegate was created as a learning exercise only, you should replace
/// <see cref="PerformanceMonitor.OneArgDelegate"/> with an in-line action for cleanliness.
/// </summary>
public class PerformanceMonitor
{
    #region [Local Properties]
    ProgressBar pb;         
    string? threadName;       
    double cpuUsage;         
    bool suspended = false;  
    bool running = true;     
    int interval = 2000;     
    static DateTime lastTime;
    static TimeSpan lastTotalProcessorTime;
    static DateTime curTime;
    static TimeSpan curTotalProcessorTime;
    delegate void OneArgDelegate(double arg);
    
    /// <summary>
    /// The thread's update speed (in milliseconds).
    /// </summary>
    public int Internal
    {
        get => interval;
        set {
            if (value < 1)
                interval = 1;
            else
                interval = value;
        }
    }

    /// <summary>
    /// The CPU's usage (in percentage).
    /// </summary>
    public double Usage
    {
        get { return cpuUsage; }
        set { cpuUsage = value; }
    }
    #endregion

    /// <summary>
    /// Main constructor.
    /// </summary>
    public PerformanceMonitor(ProgressBar progressBar)
    {
        pb = progressBar;
    }

    #region [CPU Monitor Methods]
    /// <summary>
    /// Thread entry point.
    /// </summary>
    public void GeneratePerformance()
    {
        // Get name of executing thread
        threadName = Thread.CurrentThread.Name ?? "CPU";

        string processName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "SchedulerDemo";
        Process[] pp = Process.GetProcessesByName(processName);

        // Do some basic validation checks...
        if (pp.Length == 0)
        {
            Debug.WriteLine($">> {processName} not found.");
            return;
        }

        Process p = pp[0];
        if (p == null)
        {
            Debug.WriteLine($">> {processName} could not be accessed.");
            return;
        }

        // Thread loop.
        while (running)
        {
            try
            {
                // Add a delay between updates.
                Thread.Sleep(interval);

                lock (this) // obtain lock
                {
                    while (suspended) // loop until not suspended
                    {
                        Monitor.Wait(this, 500); // suspend thread execution
                    }
                }

                // Check for first use...
                if (lastTime == DateTime.MinValue || lastTime == DateTime.MaxValue || lastTime == new DateTime())
                {
                    lastTime = DateTime.Now;
                    lastTotalProcessorTime = p.TotalProcessorTime;
                }
                else
                {
                    curTime = DateTime.Now;
                    curTotalProcessorTime = p.TotalProcessorTime;

                    Usage = (curTotalProcessorTime.TotalMilliseconds - lastTotalProcessorTime.TotalMilliseconds) / curTime.Subtract(lastTime).TotalMilliseconds / Convert.ToDouble(Environment.ProcessorCount);
                    Usage *= 100;

                    lastTime = curTime;
                    lastTotalProcessorTime = curTotalProcessorTime;

                    try {
                        // Update control's text in UI...
                        if (pb != null)
                            pb.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new OneArgDelegate(UpdateUserInterface), Usage);
                    }
                    catch (Exception) { /* On exit there is a chance that the control has been disposed. */ }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"ERROR: {e.Message}");
            }
        }
    }

    /// <summary>
    /// This will run on the UI thread, so any access to control properties is safe.
    /// </summary>
    /// <param name="data">CPU usage percent</param>
    void UpdateUserInterface(double data)
    {
        pb.Value = Usage;
    }

    /// <summary>
    /// Change the thread's suspended/running state.
    /// </summary>
    public void Toggle()
    {
        // toggle bool controlling state
        suspended = !suspended;

        // change the control's enabled state
        pb.Dispatcher.BeginInvoke(DispatcherPriority.Normal, delegate() 
        { 
            pb.IsEnabled = suspended ? false : true; 
        });

        lock (this) // obtain lock
        {
            if (!suspended) // if thread resumed
                Monitor.Pulse(this);
        }
    }

    /// <summary>
    /// The thread's while loop exit signal.
    /// </summary>
    public void Stop()
    {
        running = false;
    }
    #endregion
}
