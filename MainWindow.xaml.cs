#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;
using Path = System.IO.Path;

namespace SchedulerDemo;

/// <summary>
/// Entry point from application definition using
/// the <see cref="Application.StartupUri"/> property.
/// </summary>
public partial class MainWindow : Window
{
    #region [Local Properties]
    static bool _dragging = false;
    bool _closing = false;
    int _refreshMS = 1000, _maxDelayMS = 14400, _itemId = 0, _cntrCancels = 0, _cntrErrors = 0, _cntrTicks = 0, _cntrFreq = 0;
    static string _delim = "──────────────────────────────────────────────────────────────────";
    static string[] _extensions = new string[] { "*.jpg", "*.png", "*.svg", "*.bmp", "*.gif", "*.ico", "*.webp", "*.dll", "*.xml", "*.txt", "*.json", "*.log", "*.docx", "*.pdf", "*.zip", "*.js", "*.ps", "*.cmd", "*.msi", "*.ini", "*.info", "*.md", "*.crt", "*.pfx" };
    static readonly object _lockObj = new();
    static ScheduleList _sl = new(false, false);
    List<double> _resultData = new();
    DispatcherTimer _tmrStats = new();
    SynchronizationContext _syncCntx = SynchronizationContext.Current!;
    Thread? _pthd = null;
    PerformanceMonitor? _pmon = null;
    ObservableCollection<string> Messages { get; set; } = new();
    int NumProcessors { get; } = Environment.GetEnvironmentVariable("NUMBER_OF_PROCESSORS") switch
    {
        "24" => 24,
        "20" => 20,
        "18" => 18,
        "16" => 16,
        "14" => 14,
        "12" => 12,
        "10" => 10,
        "8" => 8,
        "6" => 6,
        "4" => 4,
        "2" => 2,
        "1" => 1,
        _ => 0
    };
    private Stopwatch _stopwatch = new Stopwatch();
    private readonly IFileSystemWatcher _watcher;
    #endregion

    /// <summary>
    /// Default constructor.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Hook the collection changed event on the ListView control so that we can auto-scroll.
        ((INotifyCollectionChanged)lstMessages.Items).CollectionChanged += ListViewMessages_CollectionChanged!;
        ((INotifyCollectionChanged)lstHistogram.Items).CollectionChanged += ListViewHistogram_CollectionChanged!;

        // Since we're not using a view model, we'll specify the DataContext here.
        this.DataContext = this;

        // Attach our observable collection to the ListView…
        var msgBinding = new Binding { Source = Messages };
        BindingOperations.SetBinding(lstMessages, ListView.ItemsSourceProperty, msgBinding);
        BindingOperations.EnableCollectionSynchronization(Messages, _lockObj);

        // Setup an event for the window mouse-down event.
        this.MouseDown += MainWindow_MouseDown;

        // delay loading settings on change by some time to avoid file in use exception
        _watcher = Extensions.GetFileWatcher("Settings", "settings.json", () =>
        {
            AddMessage($"Settings file change detected.");
        });

        // Load the configuration file from disk, or setup a new profile...
        AddMessage(Extensions.LocalApplicationDataFolder());
        App.Profile = Serializer.Load<Settings>(System.IO.Path.Combine(Extensions.LocalApplicationDataFolder(), "settings.json"));
        if (string.IsNullOrEmpty(App.Profile.User))
        {
            App.Profile = new Settings()
            {
                User = App.Attribs?.AssemblyUser,
                Location = App.Attribs?.AssemblyPath,
                LastUse = DateTime.Now
            };
            App.Profile.Save(System.IO.Path.Combine(Extensions.LocalApplicationDataFolder(), "settings.json"));
        }


    }

    //*************************************************************************************************
    //*************************************************************************************************
    #region [Example of non-MVVM UI updating]
    async void SomeControlClickEvent(object sender, RoutedEventArgs e)
    {
        try {
            btnStatus.IsEnabled = false;

            // Call work method and wait.
            _ = await Task.Run(() => PerformSomeWork());
            lblCPU.Content = "Done";

            // Or, return a value direcly to the control.
            lblCPU.Content = await Task.Run(() => PerformSomeWork());

            // We can call our home-brew UI refresh, if neccessary.
            DoEvents(true);
        }
        catch (Exception) { }
        finally {
            btnStatus.IsEnabled = true;
        }
    }

    /// <summary>
    /// Place-holder method for testing.
    /// </summary>
    /// <param name="msTimeout">time to wait (in milliseconds)</param>
    private string PerformSomeWork(int pause = 2000)
    {
        new System.Threading.ManualResetEvent(false).WaitOne(pause); // over-engineered Thread.Sleep()
        return DateTime.Now.ToLongTimeString();
    }
    #endregion

    //*************************************************************************************************
    //*************************************************************************************************
    #region [UI & Event Methods]
    /// <summary>
    /// Window event.
    /// </summary>
    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        bool runStaircase = false;

        // Setup delegate for invoke event…
        _sl.OnInvoke += (item, msg) => {
            SetLED();
            //var ai = item as ActionItem;
            AddMessage($"─ EVENT ─ {msg}");
        };

        // Setup delegate for cancel event…
        _sl.OnCancel += (item, msg) => {
            SetLED("LED_Warning");
            //var ai = item as ActionItem;
            AddMessage($"─ EVENT ─ {msg}", Level.WARNING);
            _cntrCancels++;
        };

        // Setup delegate for error event…
        _sl.OnError += (item, msg) => {
            SetLED("LED_Error");
            //var ai = item as ActionItem;
            AddMessage($"─ EVENT ─ {msg}", Level.ERROR);
            _cntrErrors++;
        };

        // Setup delegate for shutdown event…
        _sl.OnShutdownComplete += (msg) => {
            AddMessage($"─ EVENT ─ {msg}", Level.INFO);
        };

        SetLED("LED_Idle");

        #region [staircase histogram for effect]
        if (runStaircase) 
        {
            Task.Run(async () => {
                for (int i = 0; i < 2; i++) 
                {
                    for (int j = 3; j < 48; j += 3) 
                    {
                        InvokeIf(() => { UpdateHistogram(lstHistogram, j, (j % 2 == 0) ? "Assets/Histo_Bar3.png" : "Assets/Histo_Bar0.png"); });
                        await Task.Delay(5);
                    }
                    for (int j = 48; j > 3; j -= 3) 
                    {
                        InvokeIf(() => { UpdateHistogram(lstHistogram, j, (j % 2 == 0) ? "Assets/Histo_Bar3.png" : "Assets/Histo_Bar0.png"); });
                        await Task.Delay(5);
                    }
                }
                #region [Adding an event to each image in the ListView]
                //InvokeIf(() => 
                //{
                //    List<Image> bars = VisualTreeUtility.FindChildVisuals<Image>(lstHistogram).ToList();
                //    if (bars.Count > 0)
                //    {
                //        foreach (Image bar in bars) 
                //        {
                //            bar.MouseDown += (s, e) => { AddMessage($"--MouseDownEvent--"); };
                //        }
                //    }
                //});
                #endregion
            });
        }
        #endregion

        AddMessage($"{System.Reflection.Assembly.GetExecutingAssembly().GetName()}");

        // Lower refresh for weaker systems…
        if (NumProcessors <= 4)
            _refreshMS *= 2;
        AddMessage($"System has {NumProcessors} available processors.");

        // Create the timer for our status updates…
        _tmrStats.Interval = TimeSpan.FromMilliseconds(_refreshMS);
        _tmrStats.Tick += tmrStats_Tick!;
        _tmrStats.Start();

        // Setup and start our CPU usage monitor…
        _pmon = new PerformanceMonitor(pbRounded) { Internal = _refreshMS };
        _pthd = new Thread(new ThreadStart(_pmon.GeneratePerformance)) { IsBackground = true };
        _pthd.Start();

        AddMessage($"Startup complete, click (Add) to begin");
    }


    /// <summary>
    /// Window event.
    /// </summary>
    void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;

        if ((bool)cbWait.IsChecked!)
            _sl.Shutdown(true);  // Wait for agent thread to service existing jobs.
        else
            _sl.Shutdown(false); // Don't wait for agent thread to service existing jobs.

        _pmon?.Stop();
        _tmrStats?.Stop();
    }

    /// <summary>
    /// Input event.
    /// </summary>
    void MainWindow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            _dragging = true;
            Cursor = Cursors.Hand;
            System.Windows.Application.Current.MainWindow.DragMove();
            Cursor = Cursors.Arrow;
            e.Handled = true;
            _dragging = false;
        }
        else if (e.RightButton == MouseButtonState.Pressed)
        {
            if (this.WindowState == WindowState.Normal)
                this.WindowState = WindowState.Maximized;
            else
                this.WindowState = WindowState.Normal;
        }
    }

    /// <summary>
    /// Add <see cref="ActionItem"/>s to the task list.
    /// </summary>
    async void btnAdd_Click(object sender, RoutedEventArgs e)
    {
        // Store control's value in the event that it's changed during loop.
        int toAdd = udcAmount.Value;
        
        ToggleControl(btnAdd, false);

        for (int i = 0; i < toAdd; i++)
        {
            _itemId++;
            int secDelay = Extensions.Rnd.Next(1, _maxDelayMS);
            DateTime runTime = DateTime.Now.AddSeconds(secDelay);
            AddMessage($"#{_itemId} will run {runTime.ToLongTimeString()}…");
            CancellationTokenSource aiCts = new CancellationTokenSource(TimeSpan.FromMinutes(60)); // 1 hour max
            int trapped = _itemId;
            _sl.ScheduleItem(new ActionItem(
                _itemId,
                delegate()
                {
                    Stopwatch sw = new(); sw.Start();

                    AddMessage($"Item #{trapped} scheduled for {runTime.ToLongTimeString()} started");

                    if (ChanceIf(10)) // Test internal error handling.
                    {
                        PerformSomeWork();
                        throw new Exception("A fake error, ignore me.");
                    }
                    else if (ChanceIf(20)) // Simulate some I/O-bound work.
                    {
                        string target = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        string ext = _extensions[Extensions.Rnd.Next(_extensions.Length)];
                        IEnumerable<FileData> rez = ReadFromFolder(target, ext, true);
                        AddMessage($"Search found {rez.Count():N0} files matching {ext}");
                    }
                    else // Simulate some CPU-bound work.
                    {
                        var max = Extensions.Rnd.Next(7000000, 25000001);
                        var primes = GeneratePrimes(max);
                        AddMessage($"{primes.Count():N0} primes found in {max:N0}");
                    }

                    sw.Stop();
                    
                    // Add our results to the data pool…
                    _resultData.Add(sw.Elapsed.TotalMilliseconds);
                    _cntrFreq++;
                    AddMessage($"Item #{trapped} ran for {sw.Elapsed.TotalSeconds:N2} seconds");
                },
                DateTime.Now.AddSeconds(secDelay),  // Set some time in the future to run.
                aiCts.Token)
            );
            await Task.Delay(1);

            // Select a task to prematurely cancel. We may
            // have already thrown an exception in the delegate.
            if (ChanceIf(10))
            {
                AddMessage($"#{_itemId} has been selected to be canceled prematurely");
                aiCts.Cancel();
            }
        }
        
        _stopwatch.Restart();

        ToggleControl(btnAdd, true);
    }

    /// <summary>
    /// Menu button event.
    /// </summary>
    void btnToggle_Click(object sender, RoutedEventArgs e)
    {
        _sl.Toggle();
        AddMessage("Toggled agent service thread");
    }

    /// <summary>
    /// Menu button event.
    /// </summary>
    void btnClear_Click(object sender, RoutedEventArgs e)
    {
        _cntrCancels = _cntrErrors = _cntrFreq = 0;
        _sl.ClearSchedule();
        tbii.ProgressValue = 0D;
        AddMessage("Schedule list cleared");
    }

    /// <summary>
    /// Menu button event.
    /// </summary>
    void btnStatus_Click(object sender, RoutedEventArgs e)
    {
        tmrStats_Tick(this, EventArgs.Empty);
        AddMessage($"Stats have been updated");
    }

    /// <summary>
    /// Menu button event.
    /// </summary>
    async void btnForce_Click(object sender, RoutedEventArgs e)
    {
        ToggleControl(btnForce, false);

        IEnumerable<ActionItem> collection = _sl.GetWaiting();
        if (collection.Any())
        {
            foreach (var ai in collection)
            {
                AddMessage($"Advancing #{ai.Id}…");
                ai.RunTime = DateTime.Now.AddSeconds(Extensions.Rnd.Next(3,11));
                await Task.Delay(1);
            }
        }
        else
            AddMessage($"No waiting items detected");

        ToggleControl(btnForce, true);
    }

    /// <summary>
    /// Menu button event.
    /// </summary>
    async void btnWaiting_Click(object sender, RoutedEventArgs e)
    {
        ToggleControl(btnWaiting, false);

        IEnumerable<ActionItem> collection = _sl.GetWaiting();
        if (collection.Any())
        {
            AddMessage(_delim);
            foreach (var ai in collection)
            {
                AddMessage($"#{ai.Id} is scheduled to run at {ai.RunTime.ToString("hh:mm:ss tt")}");
                await Task.Delay(1);
            }

            #region [determining averages]
            // Using Zip and Tuple...
            var diff = collection
                .Zip(collection
                .Skip(1))
                .Select(tuple => (tuple.Second.RunTime.TimeOfDay - tuple.First.RunTime.TimeOfDay).TotalMinutes)
                .Average();

            AddMessage($"Average time between items is {diff:N2} mins");

            // Using Pairwise extension...
            var pwAvg = collection.Pairwise((a, b) => (b.RunTime - a.RunTime)).Average(ts => ts.TotalMinutes);
            AddMessage($"Pairwise average time is {pwAvg:N2} mins");

            // Using the duration between just two items (not as accurate)…
            var sample1 = collection.Skip(1).Select(ai => ai).ToArray();
            if (sample1.Length > 1)
            {
                var diffy = (sample1[1].RunTime - sample1[0].RunTime).Duration();
                AddMessage($"Average duration between just two is {diffy.TotalMinutes:N2} mins");
            }

            // Using an aggregate (ugly w/accuracy problem)…
            var sample2 = collection.Skip(1).Select(ai => ai).ToArray();
            if (sample2.Length > 4)
            {
                var nums = new[] { 
                        sample2[0].RunTime, 
                        sample2[1].RunTime, 
                        sample2[2].RunTime, 
                        sample2[3].RunTime, 
                        sample2[4].RunTime 
                };
                var avg = nums.Aggregate(
                    (cnt: 0.0, sum: 0.0, prev: DateTime.MinValue),
                    (tup, dt) => (cnt: tup.cnt + 1,
                                 sum: (tup.prev == DateTime.MinValue)
                                    ? 0
                                    : tup.sum + (dt - tup.prev).TotalMinutes,
                                prev: dt),
                    tup => tup.cnt == 1
                         ? tup.sum
                         : tup.sum / (tup.cnt - 1));
                AddMessage($"Aggregate average time is {avg:N2} mins");
            }
            #endregion

        }
        AddMessage(_delim);
        AddMessage($"{collection.Count()} items currently waiting");

        ToggleControl(btnWaiting, true);
    }

    /// <summary>
    /// You could allow the blocking call for Shutdown(true) in the closing
    /// event, however, this is undesireable in a UI application since the 
    /// main UI thread would freeze until the last <see cref="ActionItem"/>
    /// was run and finished. You could wrap the exit up in a task so that
    /// the UI closes fully, but this would keep the application resident
    /// in memory until the last <see cref="ActionItem"/> finishes.
    /// </summary>
    private void btnExit_Click(object sender, RoutedEventArgs e)
    {
        int remain = _sl.GetUnActivatedCount();

        if (remain > 0 && (bool)cbWait.IsChecked!)
        {
            AddMessage($"There are still {remain} tasks waiting.", Level.WARNING);
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>
    /// Timer status event for updating the TextBlock control.
    /// Called every <see cref="MainWindow._refresh"/> seconds.
    /// </summary>
    void tmrStats_Tick(object sender, EventArgs e)
    {
        // Dragging while updating the UI caused issues with the global
        // mouse state message pump. We'll just return from this update
        // until the user has finished dragging. This is usually not an
        // issue with MVVM setups that employ INotifyPropertyChanged.
        if (_dragging || _closing) 
            return;

        StringBuilder sb = new StringBuilder();
        try
        {
            // We should never take more than 1 second (_refresh) to complete this,
            // but in the event that a request stalls it's good practice to disable
            // the timer until the routine finishes and then finally re-enable,
            // otherwise an over-subscription state could occur.
            _tmrStats.Stop();

            double comper = 0.0, canper = 0.0, errper = 0.0, avg = 0.0;
            if (!_sl.IsAutoCleanEnabled() && _sl.GetActivatedCount() > 0)
            {
                comper = ((double)_sl.GetActivatedCount() / (double)_sl.GetCount()) * 100.0;
                sb.AppendLine($"{comper:N1} % of actions completed");
                canper = ((double)_cntrCancels / (double)_sl.GetCount()) * 100.0;
                errper = ((double)_cntrErrors / (double)_sl.GetCount()) * 100.0;

                InvokeIf(() => {
                    // The Windows.Shell.TaskBarItemInfo uses 0.0 to 1.0 to represent 0% to 100%.
                    tbii.ProgressValue = (comper / 100D);
                });
            }
            sb.AppendLine($"{_sl.GetCount()} total ActionItems");
            sb.AppendLine($"{_sl.GetActivatedCount()} activated ActionItems");
            sb.AppendLine($"{_sl.GetUnActivatedCount()} unactivated ActionItems");
            sb.AppendLine($"{_cntrCancels:N0} canceled ActionItems ({canper:N1} %)");
            sb.AppendLine($"{_cntrErrors:N0} faulted ActionItems ({errper:N1} %)");
            avg = _resultData.Sum() / ((_resultData.Count() > 0) ? _resultData.Count() : 1);
            sb.AppendLine($"{new TimeSpan(0, 0, 0, 0, (int)avg).ToTimeString()} average run time");

            IEnumerable<ActionItem> coll = _sl.GetWaiting();
            if (coll.Any() && coll.Count() > 1)
            {
                var pwAvg = coll.Pairwise((a, b) => (b.RunTime - a.RunTime)).Average(ts => ts.TotalMinutes);
                sb.AppendLine($"{pwAvg:N2} minutes average spacing");

                var pwPer = coll.Pairwise((a, b) => (b.RunTime - a.RunTime)).Average(ts => ts.TotalMilliseconds);
                var pmVal = (60 * 1000) / pwPer;
                sb.AppendLine($"{pmVal:N1} estimated per minute");
            }

            // Check how accurate our avg spacing metric is. These values will
            // inevitably been different due to the nature of random value dispersion.
            if (++_cntrTicks >= ((60 * 1000) / _refreshMS))
            {
                if (_cntrFreq > 0) // only update if we have value
                    _perMinute = $"{_cntrFreq} ActionItems per minute";
                _cntrFreq = _cntrTicks = 0;
            }
            sb.AppendLine(_perMinute);

            // We can also access the public property from our CPU performance thread…
            if (_pmon?.Usage > _CPU) { _CPU = _pmon.Usage; }
            sb.AppendLine($"Highest CPU usage was {_CPU:N1} %");

            // The TaskBarItemInfo object uses 0.0 to 1.0 to represent 0% to 100%.
            //InvokeIf(() => { tbii.ProgressValue = (_pmon != null ) ? (_pmon.Usage / 100D) : 0D; });

            // Update our graph image based on the level of CPU usage…
            switch (_pmon?.Usage)
            {
                case > 89.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar9.png");
                    break;
                case > 79.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar8.png");
                    break;
                case > 69.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar7.png");
                    break;
                case > 59.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar6.png");
                    break;
                case > 49.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar5.png");
                    break;
                case > 39.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar4.png");
                    break;
                case > 29.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar3.png");
                    break;
                case > 19.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar2.png");
                    break;
                case > 9.1: UpdateHistogram(lstHistogram, _pmon.Usage, "Assets/Histo_Bar1.png");
                    break;
                case double.NaN: AddMessage("Usage value was not applicable.", Level.WARNING);
                    break;
                default:
                    UpdateHistogram(lstHistogram, _pmon?.Usage switch { null => 0D, _ => 0D }, "Assets/Histo_Bar0.png");
                    break;
            }

            sb.AppendLine($"Next item runs at {_sl.GetNextToRun()?.ToString("hh:mm:ss tt") ?? "N/A"}");
            sb.AppendLine($"Last item runs at {_sl.GetLastToRun()?.ToString("hh:mm:ss tt") ?? "N/A"}");
            sb.AppendLine($"Auto-Clean {(_sl.IsAutoCleanEnabled() ? "is" : "is not")} enabled");
            sb.AppendLine($"Agent thread {(_sl.IsAgentAlive() ? "is" : "is not")} running");
            sb.AppendLine($"ActionItems {(_sl.IsActived() ? "have" : "have not")} been activated");
            if (_stopwatch.IsRunning && !_sl.IsAutoCleanEnabled() && _sl.GetActivatedCount() > 0)
            {
                var TimeRemaining = _stopwatch.Elapsed.Multiply((_sl.GetCount() - _sl.GetActivatedCount()) / _sl.GetActivatedCount());
                sb.AppendLine($"Estimated run time is {TimeRemaining.ToTimeString()}");
            }

            // TaskBarInfo updating
            if (_sl.IsAgentAlive())
            {
                InvokeIf(() =>
                {
                    if (tbii.ProgressState != System.Windows.Shell.TaskbarItemProgressState.Normal)
                        tbii.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
                });
            }
            else if (!_sl.IsAgentAlive())
            {
                InvokeIf(() =>
                {
                    if (tbii.ProgressState != System.Windows.Shell.TaskbarItemProgressState.Paused)
                        tbii.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Paused;
                });
            }

            // Timers do not run on the UI thread, so call our helper method for control updates…
            UpdateControlContent(tbStatus, sb.ToString());

            if (!_sl.IsBusy())
                SetLED("LED_Off");
        }
        catch (Exception ex)
        {
            InvokeIf(() =>  {
                if (tbii.ProgressState != System.Windows.Shell.TaskbarItemProgressState.Error)
                    tbii.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Error;
            });
            AddMessage(ex.Message, Level.FILE);
        }
        finally
        {
            _tmrStats.Start();
        }
    }
    static double _CPU = 0.0;
    static string _perMinute = "0 ActionItems per minute";

    /// <summary>
    /// The NotifyCollectionChangedAction enum allows us to inform 
    /// about any change such as: Add, Move, Replace, Remove and Reset.
    /// </summary>
    void ListViewMessages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            if (e.NewItems != null)
                lstMessages.ScrollIntoView(e.NewItems[0]); // scroll the new item into view   
        }
    }
    void ListViewHistogram_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            if (e.NewItems != null)
                lstHistogram.ScrollIntoView(e.NewItems[0]); // scroll the new item into view   
        }
    }
    #endregion

    //*************************************************************************************************
    //*************************************************************************************************
    #region [Local Helper Methods]
    /// <summary>
    /// Populates the <see cref="ListView"/> control with CPU usage images. 
    /// The <see cref="Image"/>s in the <see cref="ListView"/> are defined by height and color/asset.
    /// This will run the graph right to left just like Window's Task Manager.
    /// </summary>
    /// <param name="lv"><see cref="ListView"/> to operate on</param>
    /// <param name="value">height of the total stretch</param>
    /// <param name="asset">local uri resource to inject</param>
    /// <param name="maxItems">based on total width of main window</param>
    /// <param name="maxOpacity">base-line opacity for <see cref="Image"/>s</param>
    /// <param name="fadeFactor">left-most roll-off for opacity clamping</param>
    /// <param name="addGlow">true if adding shadow effect to each <see cref="Image"/></param>
    /// <remarks>
    /// Confirm your <see cref="ListView"/> has enough height so <see cref="Image"/>s are not clipped.
    /// Our images are rotated 90 degrees, so left is top and right is bottom.
    /// </remarks>
    void UpdateHistogram(ListView lv, double value, string asset = "Assets/Histo_Bar0.png", int maxItems = 68, double maxOpacity = 0.9, double fadeFactor = 0.022, bool addGlow = true)
    {
        var imgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, asset);

        // If window is maximized, extend our histogram a bit…
        if (this.WindowState == WindowState.Maximized) {
            fadeFactor *= 0.75;
            maxItems = (int)(maxItems * 1.65);
        }

        try {
            var bi = new BitmapImage(new Uri(imgPath, UriKind.Absolute));
            if (bi != null) {
                bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                System.Windows.Controls.ListViewItem myItem = new();
                System.Windows.Controls.Image img = new();
                img.BeginInit();
                if (bi.Height > 0) { img.Height = bi.Height; }
                else { img.Height = 17; }
                if (value < 2) { value = 2; } // a minimum to draw
                img.Width = value;
                img.Opacity = maxOpacity;
                img.Margin = new Thickness(0,-2.5,0,-2.5); // squeeze the bars closer together
                img.Stretch = System.Windows.Media.Stretch.Fill; // the stretch type must be set to fill
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.Linear);
                if (addGlow) {
                    img.Effect = new DropShadowEffect {
                        Color = new Color { A = 255, R = 255, G = 255, B = 20 },
                        Direction = 320,
                        BlurRadius = 4,
                        ShadowDepth = 1,
                        Opacity = 0.7
                    };
                }
                img.Source = bi;
                img.EndInit();
                myItem.Content = img;

                lv.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() => {
                    // Add the next bar to the list.
                    lv.Items.Add(myItem);
                    // Fade out the left-most images…
                    if (lv.Items.Count >= (maxItems * 0.99))
                    {
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            var lvi = lv.Items[i] as System.Windows.Controls.ListViewItem;
                            var im = lvi?.Content as System.Windows.Controls.Image;
                            if (im != null)
                                im.Opacity = (i * fadeFactor).Clamp(0.0, maxOpacity);
                        }
                    }
                    // Clear out older elements…
                    while (lv.Items.Count > maxItems)
                        lv.Items.RemoveAt(0);
                }));
            }
            else {
                AddMessage($"Could not load '{asset}'", Level.WARNING);
            }
        }
        catch (Exception ex) {
            AddMessage($"UpdateHistogram: {ex.Message}", Level.ERROR);
        }
    }

    /// <summary>
    /// A method to consume time & cpu cycles.
    /// </summary>
    /// <remarks>
    /// There are better methods out there for doing this but
    /// we're interested in CPU cycles, not with efficiency.
    /// </remarks>
    public static List<int> GeneratePrimes(int num)
    {
        var primes = new List<int>();
        for (var i = 2; i <= num; i++) {
            var add = true;
            foreach (var prime in primes) {
                if (prime * prime > i)
                    break;
                if (i % prime == 0) {
                    add = false;
                    break;
                }
            }
            if (add)
                primes.Add(i);
        }
        return primes;
    }

    /// <summary>
    /// A throw-back to the good ol' WinForm days.
    /// </summary>
    /// <param name="useNestedFrame">if true, employ <see cref="Dispatcher.PushFrame"/></param>
    static void DoEvents(bool useNestedFrame = false)
    {
        if (!useNestedFrame)
            System.Windows.Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new System.Threading.ThreadStart(() => System.Threading.Thread.Sleep(0)));
        else
        {
            // Create new nested message pump.
            DispatcherFrame nested = new DispatcherFrame(true);

            // Dispatch a callback to the current message queue, when getting called,
            // this callback will end the nested message loop. The priority of this
            // callback should always be lower than that of the UI event messages.
            #pragma warning disable CS8622
            var exitFrameOp = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, (SendOrPostCallback)delegate (object arg)
            {
                DispatcherFrame? f = arg as DispatcherFrame;
                if (f != null) { f.Continue = false; }
            }, nested);
            #pragma warning restore CS8622

            // Pump the nested message loop, the nested message loop will
            // immediately process the messages left inside the message queue.
            Dispatcher.PushFrame(nested);

            // If the exit frame callback doesn't get completed, abort it.
            if (exitFrameOp.Status != DispatcherOperationStatus.Completed)
                exitFrameOp.Abort();
        }
    }

    /// <summary>
    /// Helper method for our <see cref="ListView"/> control.
    /// </summary>
    /// <param name="message">text to add</param>
    /// <param name="level">importance</param>
    void AddMessage(string message, Level level = Level.INFO)
    {
        if (level == Level.FILE)
        {
            App.WriteToLog(message);
            return;
        }

        if (_closing)
            return;

        System.Windows.Application.Current.Dispatcher.Invoke(delegate
        {
            lock (_lockObj)
            {
                //lstMessages.Items.Add($"[{level}] {message}");

                if (message.Contains(_delim))
                    Messages?.Add(message);
                else
                    Messages?.Add($"[{level}] {message}");
            }
        });
    }

    /// <summary>
    /// Helper method for our <see cref="ListView"/> control.
    /// </summary>
    /// <param name="message">text to add</param>
    /// <param name="level">importance</param>
    async Task AddMessageAsync(string message, Level level = Level.INFO)
    {
        if (level == Level.FILE)
        {
            await App.WriteToLogAsync(message);
            return;
        }

        if (_closing)
            return;

        await System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)delegate ()
        {
            lock (_lockObj)
            {
                //lstMessages.Items.Add($"[{level}] {message}");

                if (message.Contains(_delim))
                    Messages?.Add(message);
                else
                    Messages?.Add($"[{level}] {message}");
            }
        });
    }

    /// <summary>
    /// Generic method for using a control's built-in dispatcher.
    /// </summary>
    /// <param name="ctrl">common <see cref="System.Windows.Controls.Control"/></param>
    /// <param name="message">the text for the control's content</param>
    void UpdateControlContent(System.Windows.Controls.Control ctrl, string message)
    {
        if (_closing)
            return;

        ctrl.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            // This could be changed to a switch/case.
            if (ctrl is ListView lv)
                lv.Items.Add(message);
            else if (ctrl is ListBox lb)
                lb.Items.Add(message);
            else if (ctrl is Label lbl)
                lbl.Content = message;
            else if (ctrl is TextBox tb)
                tb.Text = message;
            else if (ctrl is Button btn)
                btn.Content = message;
            else if (ctrl is GroupBox grb)
                grb.Content = message;
            else if (ctrl is ComboBox cmb)
                cmb.Text = message;
            else if (ctrl is ProgressBar pb)
                pb.Value = Double.Parse(message);
            else
                App.WriteToLog($"Undefined Control Type: {ctrl.GetType()}");
        }));
    }

    /// <summary>
    /// A left-over from testing the <see cref="ComboBox"/>
    /// as a selector for ActionItem quantity to inject.
    /// </summary>
    void cmbAmount_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var cb = (ComboBox)sender;
        var item = (int)cb.SelectedValue; // amount to inject
        AddMessage($"SelectionChanged to {item}");
    }

    /// <summary>
    /// Use the synchronization context to marshal the delegate to the UI thread.
    /// </summary>
    /// <param name="fe">common <see cref="FrameworkElement"/></param>
    /// <param name="message">the text for the control's content</param>
    /// <remarks>the post method is asynchronous</remarks>
    void UpdateUsingSyncContext(FrameworkElement fe, string message)
    {
        if (_closing)
            return;

        // This could be changed to a switch/case.
        if (fe is TextBlock tbl)
            _syncCntx?.Post(o => tbl.Text = message, null);
        else if (fe is TextBox tbx)
            _syncCntx?.Post(o => tbx.AppendText(message), null);
        else if (fe is Label lbl)
            _syncCntx?.Post(o => lbl.Content = message, null);
        else if (fe is Button but)
            _syncCntx?.Post(o => but.Content = message, null);
        else if (fe is ListView lsv)
            _syncCntx?.Post(o => lsv.Items.Add(message), null);
        else if (fe is ListBox lsb)
            _syncCntx?.Post(o => lsb.Items.Add(message), null);
        else if (fe is GroupBox grb)
            _syncCntx?.Post(o => grb.Content = message, null);
        else if (fe is ComboBox cmb)
            _syncCntx?.Post(o => cmb.Text = message, null);
        else if (fe is CheckBox ckb)
            _syncCntx?.Post(o => ckb.Content = message, null);
        else if (fe is RadioButton rbn)
            _syncCntx?.Post(o => rbn.Content = message, null);
        else if (fe is ProgressBar pb)
            _syncCntx?.Post(o => pb.Value = Double.Parse(message), null);
        else
            App.WriteToLog($"Undefined FrameworkElement: {fe.GetType()}");
    }

    /// <summary>
    /// Uses the <see cref="System.Windows.Controls.Control"/>'s dispatcher to toggle it's enabled state.
    /// </summary>
    /// <param name="ctrl"><see cref="System.Windows.Controls.Control"/></param>
    /// <param name="state">any boolean flag</param>
    void ToggleControl(System.Windows.Controls.Control ctrl, bool state = true)
    {
        if (_closing)
            return;

        ctrl.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
        {
            ctrl.IsEnabled = state;
        }));
    }

    /// <summary>
    /// This is a handy method to determine if a call to the Application Dispatcher is neccessary.
    /// </summary>
    /// <param name="action">The <see cref="Action"/> to perform.</param>
    void InvokeIf(Action execute)
    {
        if (System.Threading.Thread.CurrentThread == System.Windows.Application.Current.Dispatcher.Thread)
            execute();
        else
            System.Windows.Application.Current.Dispatcher.Invoke(execute);
    }

    /// <summary>
    /// Sets the <see cref="Image"/> control's source.
    /// </summary>
    /// <param name="state">LED image name</param>
    void SetLED(string state = "LED_On")
    {
        if (_closing)
            return;

        System.Windows.Application.Current.Dispatcher.Invoke(delegate()
        {
            try
            {
                imgStatus.BeginInit();
                imgStatus.Source = new BitmapImage(new Uri($@"/Assets/{state}.png", UriKind.Relative)); //imgStatus.Source = GetBitmapFrame(@"pack://application:,,,/Assets/LED_Off.png");
                imgStatus.EndInit();
            }
            catch (Exception ex)
            {
                AddMessage(ex.Message, Level.FILE);
            }
        });
    }

    /// <summary>
    /// Example image source setting method via <see cref="System.Net.Http.HttpClient"/>.
    /// </summary>
    /// <param name="imgCntrl">The <see cref="Image"/> control to update</param>
    /// <param name="ImageUrl">URL path to the external image</param>
    async Task UpdateImageControlHttpAsync(System.Windows.Controls.Image imgCntrl, string ImageUrl)
    {
        if (_closing)
            return;

        System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
        byte[] bytes = await client.GetByteArrayAsync(ImageUrl);
        BitmapImage image = new BitmapImage();
        using (var mem = new MemoryStream(bytes))
        {
            mem.Position = 0;
            image.BeginInit();
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = mem;
            image.EndInit();
        }
        image.Freeze();
        imgCntrl.Source = image;
    }

    /// <summary>
    /// Creates <see cref="FileData"/> objects from a specified folder.
    /// </summary>
    /// <returns><see cref="IEnumerable{FileData}"/></returns>
    static IEnumerable<FileData> ReadFromFolder(string location, string filter = "*.*", bool subFolders = false)
    {
        try
        {
            return Directory.GetFiles(location, filter, subFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                            .Where(filePath => System.IO.Path.GetExtension(filePath) != ".suo")
                            .Select(filePath => new FileData
                            {
                                Name = new FileInfo(filePath).Name,
                                Location = new FileInfo(filePath).DirectoryName ?? "",
                                FullName = new FileInfo(filePath).FullName,
                                Size = new FileInfo(filePath).Length,
                                LastWrite = new FileInfo(filePath).LastWriteTime,
                                Created = new FileInfo(filePath).CreationTime,
                                Attributes = new FileInfo(filePath).Attributes
                            });
        }
        catch (Exception ex) // typically a permissions issue
        {
            App.WriteToLog($"ReadFromFolder: {ex.Message}");
            return Enumerable.Empty<FileData>();
        }
    }

    /// <summary>
    /// Helper method.
    /// </summary>
    /// <param name="path">path to search</param>
    /// <param name="names">names to match (optional)</param>
    /// <returns><see cref="Dictionary{string, string}"/></returns>
    static IDictionary<string, string> GetFilePaths(string path, ICollection<string> names = null)
    {
        IDictionary<string, string> dictionary = new Dictionary<string, string>();
        FileInfo[] files = new DirectoryInfo(path).GetFiles();
        foreach (FileInfo fileInfo in files)
        {
            if (names == null || names.Contains(fileInfo.Name))
            {
                dictionary.Add(fileInfo.Name, fileInfo.FullName);
            }
        }
        return dictionary;
    }

    /// <summary>
    /// Gets the list of file keys that have the specified long file name.
    /// </summary>
    /// <param name="longFileName">File name to search for (case-insensitive)</param>
    /// <returns>Array of file keys, or a 0-length array if none are found</returns>
    public string[] FindFiles(string longFileName, string path)
    {
        longFileName = longFileName.ToLowerInvariant();
        ArrayList arrayList = new ArrayList();
        foreach (KeyValuePair<string, string> file in GetFilePaths(path))
        {
            if (file.Value.ToLowerInvariant() == longFileName)
            {
                arrayList.Add(file.Key);
            }
        }
        return (string[])arrayList.ToArray(typeof(string));
    }

    /// <summary>
    /// Gets the list of file keys whose long file names match a specified
    /// regular-expression search pattern.
    /// </summary>
    /// <param name="pattern">Regular expression search pattern</param>
    /// <returns>Array of file keys, or a 0-length array if none are found</returns>
    public string[] FindFiles(Regex pattern, string path)
    {
        ArrayList arrayList = new ArrayList();
        foreach (KeyValuePair<string, string> file in GetFilePaths(path))
        {
            if (pattern.IsMatch(file.Value))
            {
                arrayList.Add(file.Key);
            }
        }
        return (string[])arrayList.ToArray(typeof(string));
    }

    /// <summary>
    /// Simple decision maker.
    /// </summary>
    bool ChanceIf(int percent = 50)
    {
        if (Extensions.Rnd.Next(101) <= percent)
            return true;

        return false;
    }
    #endregion
}
