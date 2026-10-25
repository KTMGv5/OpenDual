using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace OpenDualHub;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText("hub_crash.log", $"[{DateTime.Now}] Dispatcher Unhandled: {e.Exception}\n");
        }
        catch { }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText("hub_crash.log", $"[{DateTime.Now}] Domain Unhandled: {e.ExceptionObject}\n");
        }
        catch { }
    }
}
