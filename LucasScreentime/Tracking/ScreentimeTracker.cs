using LucasScreentime.Logging;
using LucasScreentime.Storage;

namespace LucasScreentime.Tracking;

public sealed class ScreentimeTracker : IDisposable
{
    private readonly ScreentimeRepository _repo;
    private readonly WindowsMessageSink _sink;

    private bool _monitorOn = true; // assume on at startup; first off event corrects if wrong
    private bool _locked = false;
    private bool _sleeping = false;
    private long? _currentSessionId;
    private readonly object _lock = new();

    public event Action? StateChanged;

    public bool IsTracking
    {
        get { lock (_lock) { return _monitorOn && !_locked && !_sleeping; } }
    }

    public ScreentimeTracker(ScreentimeRepository repo)
    {
        _repo = repo;
        _sink = new WindowsMessageSink();
    }

    public void Initialize()
    {
        _repo.CloseOpenSessions(DateTime.UtcNow);
        AppLogger.Log("Tracker initialized");

        _sink.MonitorStateChanged += OnMonitorStateChanged;
        _sink.SystemSleeping += OnSystemSleeping;
        _sink.SystemResumed += OnSystemResumed;
        _sink.SessionLocked += OnSessionLocked;
        _sink.SessionUnlocked += OnSessionUnlocked;
        _sink.Initialize();

        // Start tracking immediately — Windows only fires monitor state on *change*, not on startup
        lock (_lock) { UpdateTracking(); }
    }

    private void OnMonitorStateChanged(int state)
    {
        lock (_lock)
        {
            _monitorOn = state != 0; // 0=off, 1=on, 2=dimmed (treat dimmed as on)
            AppLogger.Log($"Monitor: {(state == 0 ? "off" : state == 1 ? "on" : "dimmed")}");
            UpdateTracking();
        }
    }

    private void OnSystemSleeping()
    {
        lock (_lock)
        {
            AppLogger.Log("System sleeping");
            _sleeping = true;
            UpdateTracking();
        }
    }

    private void OnSystemResumed()
    {
        lock (_lock)
        {
            AppLogger.Log("System resumed");
            _sleeping = false;
            UpdateTracking();
        }
    }

    private void OnSessionLocked()
    {
        lock (_lock)
        {
            AppLogger.Log("Screen locked");
            _locked = true;
            UpdateTracking();
        }
    }

    private void OnSessionUnlocked()
    {
        lock (_lock)
        {
            AppLogger.Log("Screen unlocked");
            _locked = false;
            UpdateTracking();
        }
    }

    private void UpdateTracking()
    {
        bool shouldTrack = _monitorOn && !_locked && !_sleeping;
        bool hasSession = _currentSessionId.HasValue;

        if (shouldTrack && !hasSession)
            StartSession();
        else if (!shouldTrack && hasSession)
        {
            string reason = !_monitorOn ? "monitor_off"
                          : _locked    ? "locked"
                          :               "sleeping";
            EndSession(reason);
        }

        StateChanged?.Invoke();
    }

    private void StartSession()
    {
        var now = DateTime.UtcNow;
        _currentSessionId = _repo.StartSession(now);
        AppLogger.Log($"Timer START | Session #{_currentSessionId.Value} | UTC: {now:O}");
    }

    private void EndSession(string reason)
    {
        if (_currentSessionId.HasValue)
        {
            var now = DateTime.UtcNow;
            var sessionId = _currentSessionId.Value;
            _repo.EndSession(sessionId, now);
            _currentSessionId = null;
            AppLogger.Log($"Timer END   | Session #{sessionId} | Reason: {reason} | Today: {_repo.GetTodayTotal():h\\:mm}");
        }
    }

    public TimeSpan GetTodayTotal() => _repo.GetTodayTotal();

    public TimeSpan GetTotalForDate(DateTime localDate) => _repo.GetTotalForDate(localDate);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_currentSessionId.HasValue)
                EndSession("disposing");
        }
        AppLogger.Log("Tracker disposed");
        _sink.Dispose();
    }
}
