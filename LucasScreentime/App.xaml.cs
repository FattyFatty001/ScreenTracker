using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using LucasScreentime.Notifications;
using LucasScreentime.Settings;
using LucasScreentime.Storage;
using LucasScreentime.Tracking;
using LucasScreentime.UI;
using LucasScreentime.Update;
using Microsoft.Win32;
using Squirrel;

namespace LucasScreentime;

public partial class App : Application
{
    private Mutex? _mutex;
    private TrayIcon? _trayIcon;
    private ScreentimeTracker? _tracker;
    private DailyReportJob? _reportJob;
    private AutoUpdater? _updater;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "LucasScreentime_SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Lucas Screentime is already running.",
                "Lucas Screentime", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        HandleSquirrelEvents();
        RegisterStartup();

        var settings = AppSettings.Load();
        var repo = new ScreentimeRepository();

        _tracker = new ScreentimeTracker(repo);
        _tracker.Initialize();

        var emailService = new EmailService(settings);
        _reportJob = new DailyReportJob(settings, repo, emailService, _tracker);
        _updater = new AutoUpdater(settings);

        System.Windows.Forms.Application.EnableVisualStyles();
        _trayIcon = new TrayIcon(_tracker);

        _trayIcon.SettingsRequested += () =>
        {
            var win = new SettingsWindow(settings, emailService);
            win.Show();
            win.Activate();
        };

        _trayIcon.ExitRequested += () =>
        {
            _trayIcon.Dispose();
            _tracker.Dispose();
            _reportJob.Dispose();
            _updater.Dispose();
            _mutex?.ReleaseMutex();
            Shutdown();
        };

        _reportJob.OnError += _ => _trayIcon.SetError(true);
        _updater.OnError += _ => _trayIcon.SetError(true);

        _reportJob.Start();
        _updater.Start();

        if (!settings.IsConfigured)
        {
            var win = new SettingsWindow(settings, emailService);
            win.Show();
        }
    }

    private static void HandleSquirrelEvents()
    {
        try
        {
            SquirrelAwareApp.HandleEvents(
                onInitialInstall: (_, _) => RegisterStartup(),
                onAppUninstall: (_, _) => RemoveStartup());
        }
        catch { /* not running under Squirrel */ }
    }

    private static void RegisterStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            var exe = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            key?.SetValue("LucasScreentime", $"\"{exe}\"");
        }
        catch { }
    }

    private static void RemoveStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("LucasScreentime", throwOnMissingValue: false);
        }
        catch { }
    }
}
