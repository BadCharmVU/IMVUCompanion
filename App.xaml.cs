using System;
using System.Windows;
using System.Windows.Threading;

namespace IMVUCompanion;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        this.DispatcherUnhandledException += OnDispatcherUnhandledException;
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        SafeLog("DispatcherUnhandledException", e.Exception);
        try
        {
            MessageBox.Show(
                "IMVU Companion crashed.\n\n" + e.Exception.ToString(),
                "IMVUCompanion Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { }
        e.Handled = true;  // prevent immediate hard exit so log can be seen
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        SafeLog("AppDomain UnhandledException (may be before window)", ex);
        try
        {
            MessageBox.Show(
                "IMVU Companion crashed hard.\n\n" + (ex?.ToString() ?? e.ExceptionObject?.ToString()),
                "IMVUCompanion Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { }
    }

    private static void SafeLog(string message, Exception? ex = null)
    {
        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            if (ex != null)
                line += "\n" + ex.ToString();

            UserDataPaths.AppendCrash(line + "\n\n");
        }
        catch
        {
            // last resort: ignore logging failures
        }
    }
}

