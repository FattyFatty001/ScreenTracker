using LucasScreentime.Logging;
using LucasScreentime.Settings;
using Velopack;
using Velopack.Sources;
using Timer = System.Threading.Timer;

namespace LucasScreentime.Update;

public sealed class AutoUpdater : IDisposable
{
    private readonly AppSettings _settings;
    private Timer? _timer;
    private TimeSpan _interval;
    private DateTime? _nextCheckAt;

    public event Action<Exception>? OnError;

    public int? MinutesUntilNextCheck => _nextCheckAt is null ? null
        : (int)Math.Max(0, Math.Ceiling((_nextCheckAt.Value - DateTime.Now).TotalMinutes));

    public DateTime? LastCheckAt { get; private set; }
    public string? LastCheckError { get; private set; }
    public string? CurrentVersion { get; private set; }

    public AutoUpdater(AppSettings settings) => _settings = settings;

    public void Start()
    {
        _interval = TimeSpan.FromMinutes(Math.Max(1, _settings.UpdateCheckIntervalMinutes));
        _nextCheckAt = DateTime.Now.AddSeconds(30);
        _timer = new Timer(CheckForUpdates, null, TimeSpan.FromSeconds(30), _interval);

        try
        {
            var src = new GithubSource("https://github.com/placeholder/placeholder", null, false);
            var mgr = new UpdateManager(src);
            if (mgr.IsInstalled)
                CurrentVersion = mgr.CurrentVersion?.ToString();
        }
        catch { /* not installed / dev run */ }
    }

    public async Task CheckNowAsync()
    {
        _nextCheckAt = DateTime.Now + _interval;

        if (string.IsNullOrWhiteSpace(_settings.GitHubRepo))
            throw new InvalidOperationException("GitHub repo is not configured.");

        var src = new GithubSource(
            $"https://github.com/{_settings.GitHubRepo}", null, false);
        var mgr = new UpdateManager(src);

        var update = await mgr.CheckForUpdatesAsync();
        LastCheckAt = DateTime.Now;
        LastCheckError = null;

        if (update != null)
        {
            AppLogger.Log($"Update found: {update.TargetFullRelease.Version} — downloading");
            await mgr.DownloadUpdatesAsync(update);
            AppLogger.Log("Update downloaded, restarting");
            mgr.ApplyUpdatesAndRestart(update);
        }
    }

    private async void CheckForUpdates(object? state)
    {
        try
        {
            await CheckNowAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastCheckAt = DateTime.Now;
            LastCheckError = ex.Message;
            AppLogger.Log($"Update check failed: {ex.Message}");
            OnError?.Invoke(ex);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
