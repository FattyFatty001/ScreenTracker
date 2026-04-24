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

            await _email.SendAsync(
                "Lucas's Screen Time Today",
                $"Lucas had {timeText} of screen time today ({DateTime.Now:dddd, MMMM d}).");

            _repo.MarkNotificationSent();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
