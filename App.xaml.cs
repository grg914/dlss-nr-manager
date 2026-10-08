using DlssNrManager.Services;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace DlssNrManager;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\DlssNrManager.SingleInstance",
            createdNew: out _ownsSingleInstanceMutex);

        if (!_ownsSingleInstanceMutex)
        {
            MessageBox.Show(
                "DLSS NR Manager is already running. Close the existing instance before starting another one.",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown(0);
            return;
        }

        AppLogger.Initialize();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppLogger.Info("WPF startup beginning.");

        // A killed process cannot run a catch/finally block. Recover backups
        // persisted on disk before MainWindow starts detecting/installing tools.
        // The single-instance mutex prevents competing manager operations.
        var componentRecovery = ManagedComponentRedownload.RecoverKnownManagedComponentBackups();
        if (componentRecovery.Failed > 0)
        {
            AppLogger.Warn(
                $"Some component backups require attention: {componentRecovery.Failed} unresolved.");
            MessageBox.Show(
                "A previous component update was interrupted. Some backup files could not be restored automatically. " +
                "Do not reinstall those components until the diagnostic logs have been reviewed.",
                "DLSS NR Manager - component recovery",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            ExternalProcessTracker.Shutdown();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"External process shutdown cleanup failed: {ex.Message}");
        }

        AppLogger.Info($"Application exiting with code {e.ApplicationExitCode}.");

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_ownsSingleInstanceMutex && _singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error(
            "Unhandled WPF dispatcher exception.",
            e.Exception);

        try { ExternalProcessTracker.KillAll(); } catch { }

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

        if (e.IsTerminating)
        {
            try { ExternalProcessTracker.KillAll(); } catch { }
        }
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error(
            "Unobserved task exception.",
            e.Exception);

        e.SetObserved();
    }
}
