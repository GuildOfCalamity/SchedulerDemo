using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.IO.Abstractions; //Install-Package System.IO.Abstractions -Version 17.2.3

namespace SchedulerDemo;

public enum Level
{
    INFO = 1,
    WARNING = 2,
    ERROR = 3,
    FILE = 9
}

/// <summary>
/// Reading for future PInvoke testing: https://github.com/microsoft/CsWin32
/// https://github.com/orgs/microsoft/repositories?language=c%23&type=all
/// </summary>
public static class Extensions
{
    public static readonly IFileSystem FileSystem = new FileSystem();

    private static readonly WeakReference s_random = new WeakReference(null);
    /// <summary>
    /// A garbage-friendly globally-accessible random.
    /// </summary>
    public static Random Rnd
    {
        get
        {
            Random? r = s_random.Target as Random;
            if (r == null) { s_random.Target = r = new Random(); }
            return r;
        }
    }

    public static IFileSystemWatcher GetFileWatcher(string moduleName, string fileName, Action onChangedCallback)
    {
        var path = LocalApplicationDataFolder(moduleName);
        
        System.Diagnostics.Debug.WriteLine($"[SettingsPath] {path}");

        if (!FileSystem.Directory.Exists(path))
            FileSystem.Directory.CreateDirectory(path);

        var watcher = FileSystem.FileSystemWatcher.CreateNew();
        watcher.Path = path;
        watcher.Filter = fileName;
        watcher.NotifyFilter = NotifyFilters.LastWrite;
        watcher.EnableRaisingEvents = true;

        watcher.Changed += (o, e) => onChangedCallback();

        return watcher;
    }

    public static string LocalApplicationDataFolder(string moduleName = "Settings")
    {
        var result = FileSystem.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), $"{((App.Attribs != null) ? App.Attribs.AssemblyTitle : System.Reflection.Assembly.GetExecutingAssembly().GetName().Name)}\\{moduleName}");
        return result;
    }

    public static bool IsWindows11()
    {
        return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
    }

    public static int CompareVersions(string version1, string version2)
    {
        try
        {
            // Split up the version strings into int[]
            // Example: v10.0.2 -> {10, 0, 2};
            if (version1 == null)
            {
                throw new ArgumentNullException(nameof(version1));
            }
            else if (version2 == null)
            {
                throw new ArgumentNullException(nameof(version2));
            }

            var v1 = version1.Substring(1).Split('.').Select(int.Parse).ToArray();
            var v2 = version2.Substring(1).Split('.').Select(int.Parse).ToArray();

            if (v1.Length != 3 || v2.Length != 3)
            {
                throw new FormatException();
            }

            if (v1[0] - v2[0] != 0)
            {
                return v1[0] - v2[0];
            }

            if (v1[1] - v2[1] != 0)
            {
                return v1[1] - v2[1];
            }

            return v1[2] - v2[2];
        }
        catch (Exception)
        {
            throw new FormatException("Bad product version format");
        }
    }

    public static bool IsDisposable(this Type type)
    {
        if (!typeof(IDisposable).IsAssignableFrom(type))
            return false; //throw new ArgumentException($"Type not disposable: {type.Name}");

        return true;
    }

    public static bool IsClonable(this Type type)
    {
        if (!typeof(ICloneable).IsAssignableFrom(type))
            return false; //throw new ArgumentException($"Type not clonable: {type.Name}");

        return true;
    }

    public static bool IsComparable(this Type type)
    {
        if (!typeof(IComparable).IsAssignableFrom(type))
            return false; //throw new ArgumentException($"Type not comparable: {type.Name}");

        return true;
    }

    public static bool IsConvertible(this Type type)
    {
        if (!typeof(IConvertible).IsAssignableFrom(type))
            return false; //throw new ArgumentException($"Type not convertible: {type.Name}");

        return true;
    }

    public static bool IsFormattable(this Type type)
    {
        if (!typeof(IFormattable).IsAssignableFrom(type))
            return false; //throw new ArgumentException($"Type not formattable: {type.Name}");

        return true;
    }

    #region [Time Helpers]
    public static TimeSpan Multiply(this TimeSpan timeSpan, double scalar) => new TimeSpan((long)(timeSpan.Ticks * scalar));

    /// <summary>
    /// Converts <see cref="TimeSpan"/> objects to a simple human-readable string.
    /// </summary>
    /// <param name="span"><see cref="TimeSpan"/></param>
    /// <param name="significantDigits">number of right side digits in output (precision)</param>
    /// <returns>formatted time</returns>
    public static string ToTimeString(this TimeSpan span, int significantDigits = 3)
    {
        var format = $"G{significantDigits}";
        return span.TotalMilliseconds < 1000 ? span.TotalMilliseconds.ToString(format) + " milliseconds"
                : (span.TotalSeconds < 60 ? span.TotalSeconds.ToString(format) + " seconds"
                : (span.TotalMinutes < 60 ? span.TotalMinutes.ToString(format) + " minutes"
                : (span.TotalHours < 24 ? span.TotalHours.ToString(format) + " hours"
                : span.TotalDays.ToString(format) + " days")));
    }

    /// <summary>
    /// Based on the time, it will display a readable sentence as to when that time happened.
    /// </summary>
    public static string ToReadableTime(this DateTime value, bool useUTC = false)
    {
        TimeSpan ts;

        if (useUTC)
            ts = new TimeSpan(DateTime.UtcNow.Ticks - value.Ticks);
        else
            ts = new TimeSpan(DateTime.Now.Ticks - value.Ticks);

        double delta = ts.TotalSeconds;
        if (delta < 60)
            return ts.Seconds == 1 ? "one second ago" : ts.Seconds + " seconds ago";
        if (delta < 120)
            return "a minute ago";
        if (delta < 2700) // 45 * 60
            return ts.Minutes + " minutes ago";
        if (delta < 5400) // 90 * 60
            return "an hour ago";
        if (delta < 86400) // 24 * 60 * 60
            return ts.Hours + " hours ago";
        if (delta < 172800) // 48 * 60 * 60
            return "yesterday";
        if (delta < 2592000) // 30 * 24 * 60 * 60
            return ts.Days + " days ago";
        if (delta < 31104000) // 12 * 30 * 24 * 60 * 60
        {
            int months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
            return months <= 1 ? "one month ago" : months + " months ago";
        }
        int years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
        return years <= 1 ? "one year ago" : years + " years ago";
    }
    #endregion

    #region [Enumerable Helpers]
    /// <summary>
    /// Uses an operator for the current and previous item.
    /// Needs only a single iteration to process pairs and produce an output.
    /// </summary>
    /// <example>
    /// var avg = collection.Pairwise((a, b) => (b.DateTime - a.DateTime)).Average(ts => ts.TotalMinutes);
    /// </example>
    public static IEnumerable<TResult> Pairwise<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TSource, TResult> resultSelector)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        
        if (resultSelector == null)
            throw new ArgumentNullException(nameof(resultSelector));

        return _(); IEnumerable<TResult> _()
        {
            using var e = source.GetEnumerator();

            if (!e.MoveNext())
                yield break;

            var previous = e.Current;
            while (e.MoveNext())
            {
                yield return resultSelector(previous, e.Current);
                previous = e.Current;
            }
        }
    }

    /// <summary>
    /// Compare method.
    /// </summary>
    public static bool SequenceEquals<T>(this IEnumerable<T> first, IEnumerable<T> second)
    {
        var firstIter = first.GetEnumerator();
        var secondIter = second.GetEnumerator();

        if (firstIter is null || secondIter is null)
            return false;

        while (firstIter.MoveNext() && secondIter.MoveNext())
        {
            if (firstIter.Current!.Equals(secondIter.Current!))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Weaver method.
    /// </summary>
    public static IEnumerable<T> InterleaveSequenceWith<T>(this IEnumerable<T> first, IEnumerable<T> second)
    {
        var firstIter = first.GetEnumerator();
        var secondIter = second.GetEnumerator();

        while (firstIter.MoveNext() && secondIter.MoveNext())
        {
            yield return firstIter.Current;
            yield return secondIter.Current;
        }
    }

    /// <summary>
    /// LINQ extension.
    /// </summary>
    public static void ForEach<T>(this IEnumerable<T> ie, Action<T> action)
    {
        foreach (var i in ie)
            action(i);
    }
    #endregion

    #region [Task Helpers]
    /// <summary>
    /// Chainable task helper.
    /// var result = await SomeLongAsyncFunction().WithTimeout(TimeSpan.FromSeconds(2));
    /// </summary>
    /// <typeparam name="TResult">the type of task result</typeparam>
    /// <returns><see cref="Task"/>TResult</returns>
    public async static Task<TResult> WithTimeout<TResult>(this Task<TResult> task, TimeSpan timeout)
    {
        Task winner = await (Task.WhenAny(task, Task.Delay(timeout)));

        if (winner != task)
            throw new TimeoutException();

        return await task;   // Unwrap result/re-throw
    }

    /// <summary>
    /// Task extension to add a timeout.
    /// </summary>
    /// <returns>The task with timeout.</returns>
    /// <param name="task">Task.</param>
    /// <param name="timeoutInMilliseconds">Timeout duration in Milliseconds.</param>
    /// <typeparam name="T">The 1st type parameter.</typeparam>
    public async static Task<T> WithTimeout<T>(this Task<T> task, int timeoutInMilliseconds)
    {
        var retTask = await Task.WhenAny(task, Task.Delay(timeoutInMilliseconds))
            .ConfigureAwait(false);

        #pragma warning disable CS8603 // Possible null reference return.
        return retTask is Task<T> ? task.Result : default;
        #pragma warning restore CS8603 // Possible null reference return.
    }

    /// <summary>
    /// Chainable task helper.
    /// </summary>
    /// <example>
    /// var result = await SomeLongTaskFunction().WithCancellation(cts.Token);
    /// </example>
    /// <typeparam name="TResult">the type of task result</typeparam>
    /// <returns><see cref="Task"/>TResult</returns>
    public static Task<TResult> WithCancellation<TResult>(this Task<TResult> task, CancellationToken cancelToken)
    {
        TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
        CancellationTokenRegistration reg = cancelToken.Register(() => tcs.TrySetCanceled());
        task.ContinueWith(ant =>
        {
            reg.Dispose();
            if (ant.IsCanceled)
                tcs.TrySetCanceled();
            else if (ant.IsFaulted)
                tcs.TrySetException(ant.Exception?.InnerException ?? new Exception("empty inner exception"));
            else
                tcs.TrySetResult(ant.Result);
        });
        return tcs.Task;  // Return the TaskCompletionSource result
    }

    public static Task<T> WithAllExceptions<T>(this Task<T> task)
    {
        TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();

        task.ContinueWith(ignored =>
        {
            switch (task.Status)
            {
                case TaskStatus.Canceled:
                    System.Diagnostics.Debug.WriteLine($"[TaskStatus.Canceled]");
                    tcs.SetCanceled();
                    break;
                case TaskStatus.RanToCompletion:
                    tcs.SetResult(task.Result);
                    System.Diagnostics.Debug.WriteLine($"[TaskStatus.RanToCompletion({task.Result})]");
                    break;
                case TaskStatus.Faulted:
                    // SetException will automatically wrap the original AggregateException
                    // in another one. The new wrapper will be removed in TaskAwaiter, leaving
                    // the original intact.
                    System.Diagnostics.Debug.WriteLine($"[TaskStatus.Faulted: {task.Exception?.Message}]");
                    tcs.SetException(task.Exception ?? new Exception("empty exception"));
                    break;
                default:
                    System.Diagnostics.Debug.WriteLine($"[TaskStatus: Continuation called illegally.]");
                    tcs.SetException(new InvalidOperationException("Continuation called illegally."));
                    break;
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Task.Factory.StartNew (() => { throw null; }).IgnoreExceptions();
    /// </summary>
    public static void IgnoreExceptions(this Task task, bool logEx = false)
    {
        task.ContinueWith(t =>
        {
            AggregateException ignore = t.Exception ?? new AggregateException("empty aggregate exception");

            ignore?.Flatten().Handle(ex =>
            {
                if (logEx)
                    App.WriteToLog($"Type: {ex.GetType()}, Message: {ex.Message}");
                return true; // don't re-throw
            });

        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    #pragma warning disable RECS0165 // Asynchronous methods should return a Task instead of void
    /// <summary>
    /// Attempts to await on the task and catches exception
    /// </summary>
    /// <param name="task">Task to execute</param>
    /// <param name="onException">What to do when method has an exception</param>
    /// <param name="continueOnCapturedContext">If the context should be captured.</param>
    public static async void SafeFireAndForget(this Task task, Action<Exception>? onException = null, bool continueOnCapturedContext = false)
    #pragma warning restore RECS0165 // Asynchronous methods should return a Task instead of void
    {
        try
        {
            await task.ConfigureAwait(continueOnCapturedContext);
        }
        catch (Exception ex) when (onException != null)
        {
            onException.Invoke(ex);
        }
        catch (Exception ex) when (onException == null)
        {
            App.WriteToLog($"SafeFireAndForget: {ex.Message}");
        }
    }
    #endregion

    #region [Misc Helpers]
    public static string NameOf(this object obj)
    {
        return $"{obj.GetType().Name} => {obj.GetType().BaseType?.Name}";
    }

    public static T? ParseEnum<T>(this string value)
    {
        try
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }
        catch (Exception)
        {
            return default(T);
        }
    }
    public static T Clamp<T>(this T val, T min, T max) where T : IComparable<T>
    {
        return val.CompareTo(min) < 0 ? min : (val.CompareTo(max) > 0 ? max : val);
    }
    #endregion
}
