using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;

namespace SchedulerDemo;

/// <summary>
/// Application definition (our startup object).
/// </summary>
public partial class App : Application
{
    public static AssemblyAttributes? Attribs;
    public static Settings Profile;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        Attribs = new AssemblyAttributes();
        base.OnStartup(e);
    }

    /// <summary>
    /// Handle application object exceptions. (main UI thread only)
    /// </summary>
    void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        WriteToLog($"Unhandled exception thrown from Dispatcher {e.Dispatcher.Thread.Name}: {e.Exception}");
        e.Handled = true;
    }

    /// <summary>
    /// Handle exceptions thrown from custom threads.
    /// </summary>
    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception? ex = e.ExceptionObject as Exception;
        WriteToLog($"Thread exception: {ex?.Message}");
    }

    public static bool WriteToLog(string message)
    {
        try
        {
            var name = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "Messages";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{name}");
            using (var fileStream = new StreamWriter(File.OpenWrite(path)))
            {
                fileStream.BaseStream.Seek(0, SeekOrigin.End);
                fileStream.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")}] {message}");
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{System.Reflection.MethodBase.GetCurrentMethod()?.Name ?? "Log"}]: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> WriteToLogAsync(string message, CancellationToken token = default)
    {
        try
        {
            string name = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "Messages";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{name}");
            await File.AppendAllTextAsync(path, $"[{DateTime.Now.ToString("hh:mm:ss.fff tt")}] {message}{Environment.NewLine}", token);
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{System.Reflection.MethodBase.GetCurrentMethod()?.Name ?? "Log"}]: {ex.Message}");
            return await Task.FromResult(false);
        }
    }
}
