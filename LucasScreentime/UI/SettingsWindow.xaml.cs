using System.Reflection;
using System.Windows;
using Brushes = System.Windows.Media.Brushes;
using LucasScreentime.Notifications;
using LucasScreentime.Settings;

namespace LucasScreentime.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly EmailService _email;

    public SettingsWindow(AppSettings settings, EmailService email)
    {
        InitializeComponent();
        _settings = settings;
        _email = email;
        LoadSettings();

        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        TxtVersion.Text = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
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
            await _email.SendAsync(
                "Lucas Screentime — Test",
                "This is a test notification from Lucas Screentime. Everything is working!",
                """
                <!DOCTYPE html>
                <html lang="en">
                <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
                <body style="margin:0;padding:0;background:#f2f2f7;font-family:-apple-system,BlinkMacSystemFont,'Helvetica Neue',Arial,sans-serif;">
                  <table width="100%" cellpadding="0" cellspacing="0" style="background:#f2f2f7;padding:48px 20px;">
                    <tr><td align="center">
                      <table width="100%" cellpadding="0" cellspacing="0" style="max-width:440px;background:#ffffff;border-radius:18px;overflow:hidden;">
                        <tr>
                          <td align="center" style="padding:36px 32px 0;">
                            <p style="margin:0 0 6px;font-size:12px;font-weight:600;color:#8e8e93;text-transform:uppercase;letter-spacing:1.2px;">Screen Time</p>
                            <p style="margin:0 0 4px;font-size:56px;font-weight:700;color:#1c1c1e;line-height:1;">Test</p>
                            <p style="margin:0;font-size:17px;color:#8e8e93;">notifications are working</p>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:28px 32px 32px;">
                            <table width="100%" cellpadding="0" cellspacing="0" style="border-top:1px solid #e5e5ea;">
                              <tr>
                                <td align="center" style="padding-top:20px;">
                                  <p style="margin:0;font-size:15px;color:#8e8e93;">Lucas Screentime is set up correctly.</p>
                                </td>
                              </tr>
                            </table>
                          </td>
                        </tr>
                      </table>
                    </td></tr>
                  </table>
                </body>
                </html>
                """);
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
