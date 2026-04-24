using System.Reflection;
using System.Windows;
using Brushes = System.Windows.Media.Brushes;
using LucasScreentime.Notifications;
using LucasScreentime.Settings;
using LucasScreentime.Update;
using System.Linq;

namespace LucasScreentime.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly EmailService _email;
    private readonly AutoUpdater? _updater;

    public SettingsWindow(AppSettings settings, EmailService email, AutoUpdater? updater = null)
    {
        InitializeComponent();
        _settings = settings;
        _email = email;
        _updater = updater;
        LoadSettings();

        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        var versionText = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";

        var mins = _updater?.MinutesUntilNextCheck;
        var updateText = mins is null ? "" : $" · next check in {mins}min";
        TxtVersion.Text = versionText + updateText;

        if (_updater?.LastCheckAt is DateTime checkedAt)
        {
            var ago = (int)Math.Round((DateTime.Now - checkedAt).TotalMinutes);
            var agoText = ago < 1 ? "just now" : $"{ago}min ago";
            if (_updater.LastCheckError is string err)
            {
                TxtUpdateStatus.Text = $"Last check {agoText} — error: {err}";
                TxtUpdateStatus.Foreground = Brushes.Red;
            }
            else
            {
                TxtUpdateStatus.Text = $"Last check {agoText} — up to date";
                TxtUpdateStatus.Foreground = Brushes.Gray;
            }
        }
    }

    private void LoadSettings()
    {
        TxtSmtpHost.Text = _settings.SmtpHost;
        TxtSmtpPort.Text = _settings.SmtpPort.ToString();
        TxtSmtpUser.Text = _settings.SmtpUsername;
        PbSmtpPassword.Password = _settings.SmtpPassword;
        TxtParent1.Text = _settings.ToAddresses.Count > 0 ? _settings.ToAddresses[0] : "";
        TxtParent2.Text = _settings.ToAddresses.Count > 1 ? _settings.ToAddresses[1] : "";
        TxtNotifyStart.Text = _settings.NotifyWindowStart;
        TxtNotifyEnd.Text = _settings.NotifyWindowEnd;
        TxtGitHubRepo.Text = _settings.GitHubRepo;
        TxtUpdateInterval.Text = _settings.UpdateCheckIntervalMinutes.ToString();
    }

    private bool SaveSettings()
    {
        if (!int.TryParse(TxtSmtpPort.Text.Trim(), out int port) || port is <= 0 or > 65535)
        {
            ShowStatus("Invalid SMTP port.", isError: true);
            return false;
        }
        if (!int.TryParse(TxtUpdateInterval.Text.Trim(), out int interval) || interval < 1)
        {
            ShowStatus("Update interval must be at least 1 minute.", isError: true);
            return false;
        }

        _settings.SmtpHost = TxtSmtpHost.Text.Trim();
        _settings.SmtpPort = port;
        _settings.SmtpUsername = TxtSmtpUser.Text.Trim();

        if (!string.IsNullOrEmpty(PbSmtpPassword.Password))
            _settings.SmtpPassword = PbSmtpPassword.Password;

        _settings.ToAddresses.Clear();
        if (!string.IsNullOrWhiteSpace(TxtParent1.Text))
            _settings.ToAddresses.Add(TxtParent1.Text.Trim());
        if (!string.IsNullOrWhiteSpace(TxtParent2.Text))
            _settings.ToAddresses.Add(TxtParent2.Text.Trim());

        _settings.NotifyWindowStart = TxtNotifyStart.Text.Trim();
        _settings.NotifyWindowEnd = TxtNotifyEnd.Text.Trim();
        _settings.GitHubRepo = TxtGitHubRepo.Text.Trim();
        _settings.UpdateCheckIntervalMinutes = interval;
        _settings.IsConfigured = _settings.ToAddresses.Count > 0
            && !string.IsNullOrEmpty(_settings.SmtpUsername)
            && !string.IsNullOrEmpty(_settings.SmtpPasswordEncrypted);

        _settings.Save();
        return true;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (SaveSettings())
        {
            ShowStatus("Settings saved.");
            Close();
        }
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveSettings()) return;
        BtnTest.IsEnabled = false;
        ShowStatus("Sending…");
        try
        {
            // Dummy data: active 3–5 pm and 7–9 pm, totalling 3h 25m
            var dummyHours = new int[24];
            dummyHours[15] = 48; // 3pm
            dummyHours[16] = 60; // 4pm
            dummyHours[17] = 32; // 5pm
            dummyHours[19] = 55; // 7pm
            dummyHours[20] = 50; // 8pm

            var totalMinutes = dummyHours.Sum();
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            string timeBig = $"{hours}h {minutes}m";
            string timeText = $"{hours} hours and {minutes} minutes";
            string dateText = DateTime.Now.ToString("dddd, MMMM d") + " (sample)";

            string htmlBody = DailyReportJob.BuildHtml(timeBig, timeText, dateText, dummyHours);
            string plainBody = $"[TEST] Lucas had {timeText} of screen time today ({dateText}).";

            await _email.SendAsync("Lucas's Screen Time Today (Test)", plainBody, htmlBody);
            ShowStatus("Test email sent successfully!", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed: {ex.Message}", isError: true);
        }
        finally
        {
            BtnTest.IsEnabled = true;
        }
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updater is null)
        {
            ShowStatus("Auto-updater is not running.", isError: true);
            return;
        }
        BtnCheckUpdate.IsEnabled = false;
        ShowStatus("Checking for updates…");
        try
        {
            await _updater.CheckNowAsync();
            ShowStatus("Up to date.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Update check failed: {ex.Message}", isError: true);
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Allow close normally
    }

    private void ShowStatus(string message, bool isError = false)
    {
        TxtStatus.Text = message;
        TxtStatus.Foreground = isError ? Brushes.Red : Brushes.Green;
    }
}
