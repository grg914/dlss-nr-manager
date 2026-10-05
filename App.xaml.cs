using DlssNrManager.Services;
using System.Windows.Threading;

namespace DlssNrManager;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppLogger.Initialize();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppLogger.Info("WPF startup beginning.");

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        AppLogger.Info($"Application exiting with code {e.ApplicationExitCode}.");

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error(
            "Unhandled WPF dispatcher exception.",
            e.Exception);

        // Keep the default crash behavior. The log is diagnostic, not a
        // mechanism for hiding potentially corrupted application state.
        e.Handled = false;
    }

    private static void OnUnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        AppLogger.Error(
            $"Unhandled AppDomain exception. IsTerminating={e.IsTerminating}.",
            e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error(
            "Unobserved task exception.",
            e.Exception);

        // Logging is sufficient here; prevent finalizer-thread escalation.
        e.SetObserved();
    }
}
