using LucasScreentime.Settings;
using LucasScreentime.Storage;
using LucasScreentime.Tracking;
using Timer = System.Threading.Timer;

namespace LucasScreentime.Notifications;

public sealed class DailyReportJob : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ScreentimeRepository _repo;
    private readonly EmailService _email;
    private readonly ScreentimeTracker _tracker;
    private Timer? _timer;

    public event Action<Exception>? OnError;

    public DailyReportJob(AppSettings settings, ScreentimeRepository repo, EmailService email, ScreentimeTracker tracker)
    {
        _settings = settings;
        _repo = repo;
        _email = email;
        _tracker = tracker;
    }

    public void Start()
    {
        _timer = new Timer(Check, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    private async void Check(object? state)
    {
        try
        {
            if (!_settings.IsConfigured || string.IsNullOrEmpty(_settings.SmtpPassword)) return;

            var now = DateTime.Now.TimeOfDay;
            if (now < _settings.NotifyStart || now > _settings.NotifyEnd) return;
            if (_repo.HasSentNotificationToday()) return;

            var total = _tracker.GetTodayTotal();
            var hours = (int)total.TotalHours;
            var minutes = total.Minutes;

            string timeText = hours > 0
                ? $"{hours} hour{(hours != 1 ? "s" : "")} and {minutes} minute{(minutes != 1 ? "s" : "")}"
                : $"{minutes} minute{(minutes != 1 ? "s" : "")}";

            string timeBig = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
            string dateText = DateTime.Now.ToString("dddd, MMMM d");
            string plainBody = $"Lucas had {timeText} of screen time today ({dateText}).";
            string htmlBody = BuildHtml(timeBig, timeText, dateText);

            await _email.SendAsync("Lucas's Screen Time Today", plainBody, htmlBody);

            _repo.MarkNotificationSent();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    private static string BuildHtml(string timeBig, string timeText, string dateText) => $"""
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
                    <p style="margin:0 0 4px;font-size:56px;font-weight:700;color:#1c1c1e;line-height:1;">{timeBig}</p>
                    <p style="margin:0;font-size:17px;color:#8e8e93;">of screen time today</p>
                  </td>
                </tr>
                <tr>
                  <td style="padding:28px 32px 32px;">
                    <table width="100%" cellpadding="0" cellspacing="0" style="border-top:1px solid #e5e5ea;">
                      <tr>
                        <td align="center" style="padding-top:20px;">
                          <p style="margin:0;font-size:15px;color:#8e8e93;">{dateText}</p>
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
        """;

    public void Dispose() => _timer?.Dispose();
}
