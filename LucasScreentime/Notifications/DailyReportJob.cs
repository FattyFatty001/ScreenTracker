using LucasScreentime.Logging;
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
            var hourlyMinutes = _repo.GetHourlyBreakdown();
            string htmlBody = BuildHtml(timeBig, timeText, dateText, hourlyMinutes);

            await _email.SendAsync("Lucas's Screen Time Today", plainBody, htmlBody);
            _repo.MarkNotificationSent();
            AppLogger.Log($"Daily report sent: {timeText}");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Daily report failed: {ex.Message}");
            OnError?.Invoke(ex);
        }
    }

    internal static string BuildHtmlChart(int[] hourlyMinutes)
    {
        const int chartMaxPx = 72;
        const int maxMinutes = 60;
        var sb = new System.Text.StringBuilder();
        for (int h = 0; h < 24; h++)
        {
            int mins = Math.Min(hourlyMinutes[h], maxMinutes);
            int barPx = mins > 0 ? Math.Max(2, (int)Math.Round(mins / (double)maxMinutes * chartMaxPx)) : 0;
            string bar = barPx > 0
                ? $"<div style=\"height:{barPx}px;background:#007AFF;border-radius:2px 2px 0 0;\"></div>"
                : "";
            sb.Append($"<td style=\"vertical-align:bottom;width:4.167%;padding:0 1px;\">{bar}</td>");
        }
        return sb.ToString();
    }

    internal static string BuildHtml(string timeBig, string timeText, string dateText, int[] hourlyMinutes)
    {
        string bars = BuildHtmlChart(hourlyMinutes);
        return $"""
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
                      <td style="padding:24px 32px 0;">
                        <p style="margin:0 0 10px;font-size:12px;font-weight:600;color:#8e8e93;text-transform:uppercase;letter-spacing:1px;">Usage by Hour</p>
                        <table width="100%" cellpadding="0" cellspacing="0">
                          <tr>
                            <td style="width:28px;vertical-align:top;font-size:10px;color:#8e8e93;text-align:right;padding-right:6px;line-height:1;">60m</td>
                            <td>
                              <table width="100%" cellpadding="0" cellspacing="0" style="height:72px;border-bottom:1px solid #e5e5ea;">
                                <tr>{bars}</tr>
                              </table>
                              <table width="100%" cellpadding="0" cellspacing="0" style="margin-top:5px;">
                                <tr>
                                  <td style="font-size:10px;color:#8e8e93;width:25%;">12am</td>
                                  <td style="font-size:10px;color:#8e8e93;text-align:center;width:25%;">6am</td>
                                  <td style="font-size:10px;color:#8e8e93;text-align:center;width:25%;">12pm</td>
                                  <td style="font-size:10px;color:#8e8e93;text-align:right;width:25%;">6pm</td>
                                </tr>
                              </table>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:20px 32px 32px;">
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
    }

    public void Dispose() => _timer?.Dispose();
}
