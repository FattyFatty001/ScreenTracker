using System.IO;
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
    private Timer? _timer;

    public event Action<Exception>? OnError;

    public DailyReportJob(AppSettings settings, ScreentimeRepository repo, EmailService email, ScreentimeTracker tracker)
    {
        _settings = settings;
        _repo = repo;
        _email = email;
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

            var nowLocal = DateTime.Now;
            var now = nowLocal.TimeOfDay;

            // Only send during the notification window
            if (now < _settings.NotifyStart || now > _settings.NotifyEnd) return;

            // 1. Send today's notification if not yet sent
            if (!_repo.HasSentNotificationToday())
            {
                await SendReportForDate(nowLocal, nowLocal);
                _repo.MarkNotificationSent();
                AppLogger.Log($"Daily report sent: {nowLocal:yyyy-MM-dd}");
                return;
            }

            // 2. Send catch-up notifications for any missed days
            var missedDates = _repo.GetMissedNotificationDates();
            foreach (var dateStr in missedDates)
            {
                var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", null);
                await SendReportForDate(date, nowLocal);
                _repo.MarkNotificationSentForDate(dateStr);
                AppLogger.Log($"Catch-up report sent for {dateStr}");
                return; // Only send one per tick to avoid flooding
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Daily report failed: {ex.Message}");
            OnError?.Invoke(ex);
        }
    }

    private async Task SendReportForDate(DateTime reportDate, DateTime nowLocal)
    {
        var total = _repo.GetTotalForDate(reportDate);
        var hours = (int)total.TotalHours;
        var minutes = total.Minutes;

        string timeText = hours > 0
            ? $"{hours} hour{(hours != 1 ? "s" : "")} and {minutes} minute{(minutes != 1 ? "s" : "")}"
            : $"{minutes} minute{(minutes != 1 ? "s" : "")}";

        string timeBig = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";

        bool isToday = reportDate.Date == nowLocal.Date;
        string dateText = reportDate.ToString("dddd, MMMM d");
        string subject = isToday
            ? "Lucas's Screen Time Today"
            : $"Lucas's Screen Time — {dateText}";
        string ofTodayText = isToday ? "of screen time today" : $"of screen time on {dateText}";
        string plainBody = $"Lucas had {timeText} {ofTodayText}.";

        // Build log link for GitHub (if configured)
        string? logLink = null;
        if (!string.IsNullOrWhiteSpace(_settings.GitHubRepo) &&
            !string.IsNullOrWhiteSpace(_settings.GitHubPat))
        {
            logLink = $"https://github.com/{_settings.GitHubRepo}/blob/main/logs/{reportDate:yyyy-MM-dd}.log";
        }

        var hourlyMinutes = _repo.GetHourlyBreakdownForDate(reportDate);
        string htmlBody = BuildHtml(timeBig, timeText, dateText, hourlyMinutes, ofTodayText, logLink, reportDate);

        // Attach today's log file
        var attachments = new List<string>();
        var logPath = AppLogger.GetLogPathForDate(reportDate);
        if (File.Exists(logPath))
            attachments.Add(logPath);

        await _email.SendAsync(subject, plainBody, htmlBody, attachments);
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

    internal static string BuildHtml(string timeBig, string timeText, string dateText,
        int[] hourlyMinutes, string? ofTodayText = null, string? logLink = null, DateTime? reportDate = null)
    {
        string bars = BuildHtmlChart(hourlyMinutes);
        string subtitle = ofTodayText ?? "of screen time today";

        // Build a footer row with the log link if available
        string logFooter = "";
        if (!string.IsNullOrWhiteSpace(logLink))
        {
            string linkLabel = reportDate.HasValue
                ? $"View log for {reportDate.Value:yyyy-MM-dd}"
                : "View daily log";
            logFooter = $"""
                        <tr>
                          <td style="padding:0 32px 24px;">
                            <table width="100%" cellpadding="0" cellspacing="0">
                              <tr>
                                <td align="center">
                                  <a href="{logLink}" style="font-size:13px;color:#007AFF;text-decoration:none;">{linkLabel}</a>
                                </td>
                              </tr>
                            </table>
                          </td>
                        </tr>
                        """;
        }

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
                        <p style="margin:0;font-size:17px;color:#8e8e93;">{subtitle}</p>
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
                    {logFooter}
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    public void Dispose() => _timer?.Dispose();
}
