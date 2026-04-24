using LucasScreentime.Storage;

namespace LucasScreentime.Tracking;

public sealed class ScreentimeTracker : IDisposable
{
    private readonly ScreentimeRepository _repo;
    private readonly WindowsMessageSink _sink;

    private bool _monitorOn = false;
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

        _sink.MonitorStateChanged += OnMonitorStateChanged;
        _sink.SystemSleeping += OnSystemSleeping;
        _sink.SystemResumed += OnSystemResumed;
        _sink.SessionLocked += OnSessionLocked;
        _sink.SessionUnlocked += OnSessionUnlocked;
        _sink.Initialize();
    }

    private void OnMonitorStateChanged(int state)
    {
        lock (_lock)
        {
            _monitorOn = state != 0; // 0=off, 1=on, 2=dimmed (treat dimmed as on)
            UpdateTracking();
        }
    }

    private void OnSystemSleeping()
    {
        lock (_lock)
        {
            _sleeping = true;
            UpdateTracking();
        }
    }

    private void OnSystemResumed()
    {
        lock (_lock)
        {
            _sleeping = false;
            // Wait for monitor-on signal before resuming — don't start tracking here
        }
    }

    private void OnSessionLocked()
    {
        lock (_lock)
        {
            _locked = true;
            UpdateTracking();
        }
    }

    private void OnSessionUnlocked()
    {
        lock (_lock)
        {
            _locked = false;
            // Wait for monitor-on signal — don't start tracking here
        }
    }

    private void UpdateTracking()
    {
        bool shouldTrack = _monitorOn && !_locked && !_sleeping;
        bool hasSession = _currentSessionId.HasValue;

        if (shouldTrack && !hasSession)
            StartSession();
        else if (!shouldTrack && hasSession)
            EndSession();

        StateChanged?.Invoke();
    }

    private void StartSession()
    {
        _currentSessionId = _repo.StartSession(DateTime.UtcNow);
    }

    private void EndSession()
    {
        if (_currentSessionId.HasValue)
        {
            _repo.EndSession(_currentSessionId.Value, DateTime.UtcNow);
            _currentSessionId = null;
        }
    }

    public TimeSpan GetTodayTotal() => _repo.GetTodayTotal();

    public void Dispose()
    {
        lock (_lock) { EndSession(); }
        _sink.Dispose();
    }
}
