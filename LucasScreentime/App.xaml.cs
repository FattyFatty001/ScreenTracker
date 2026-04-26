using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using LucasScreentime.Logging;
using LucasScreentime.Notifications;
using LucasScreentime.Settings;
using LucasScreentime.Storage;
using LucasScreentime.Tracking;
using LucasScreentime.UI;
using LucasScreentime.Update;

namespace LucasScreentime;

public partial class App : Application
{
    private Mutex? _mutex;
    private TrayIcon? _trayIcon;
    private ScreentimeTracker? _tracker;
    private DailyReportJob? _reportJob;
    private AutoUpdater? _updater;
    private GitHubLogUploader? _logUploader;

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

        Program.RegisterStartup();

        var settings = AppSettings.Load();
        AppLogger.Log("App started");

        var repo = new ScreentimeRepository();

        _tracker = new ScreentimeTracker(repo);
        _tracker.Initialize();

        var emailService = new EmailService(settings);
        _reportJob = new DailyReportJob(settings, repo, emailService, _tracker);
        _updater = new AutoUpdater(settings);
        _logUploader = new GitHubLogUploader(settings);

        System.Windows.Forms.Application.EnableVisualStyles();
        _trayIcon = new TrayIcon(_tracker);

        _trayIcon.SettingsRequested += () =>
        {
            var win = new SettingsWindow(settings, emailService, _updater);
            win.Show();
            win.Activate();
        };

        _trayIcon.ExitRequested += () =>
        {
            _trayIcon.Dispose();
            _tracker.Dispose();
            _reportJob.Dispose();
            _updater.Dispose();
            _logUploader.Dispose();
            _mutex?.ReleaseMutex();
            Shutdown();
        };

        _reportJob.OnError += _ => _trayIcon.SetError(true);
        _updater.OnError += _ => _trayIcon.SetError(true);
        _logUploader.OnError += ex => { AppLogger.Log($"Log upload failed: {ex.Message}"); _trayIcon.SetError(true); };

        _reportJob.Start();
        _updater.Start();
        _logUploader.Start(settings.LogUploadIntervalMinutes);

        if (!settings.IsConfigured)
        {
            var win = new SettingsWindow(settings, emailService, _updater);
            win.Show();
        }
    }
}
