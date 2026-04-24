using LucasScreentime.Settings;
using Squirrel;
using Squirrel.Sources;
using Timer = System.Threading.Timer;

namespace LucasScreentime.Update;

public sealed class AutoUpdater : IDisposable
{
    private readonly AppSettings _settings;
    private Timer? _timer;

    public event Action<Exception>? OnError;

    public AutoUpdater(AppSettings settings) => _settings = settings;

    public void Start()
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.UpdateCheckIntervalMinutes));
        // Delay first check by 30s to let the app finish starting up
        _timer = new Timer(CheckForUpdates, null, TimeSpan.FromSeconds(30), interval);
    }

    private async void CheckForUpdates(object? state)
    {
        if (string.IsNullOrWhiteSpace(_settings.GitHubRepo)) return;

        try
        {
            using var mgr = new UpdateManager(
                new GithubSource($"https://github.com/{_settings.GitHubRepo}", null, false));
            var result = await mgr.UpdateApp();
            if (result != null)
                UpdateManager.RestartApp();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnError?.Invoke(ex);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
