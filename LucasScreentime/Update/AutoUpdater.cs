using LucasScreentime.Settings;
using Velopack;
using Velopack.Sources;
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
        _timer = new Timer(CheckForUpdates, null, TimeSpan.FromSeconds(30), interval);
    }

    private async void CheckForUpdates(object? state)
    {
        if (string.IsNullOrWhiteSpace(_settings.GitHubRepo)) return;

        try
        {
            var src = new GithubSource(
                $"https://github.com/{_settings.GitHubRepo}", null, false);
            var mgr = new UpdateManager(src);

            var update = await mgr.CheckForUpdatesAsync();
            if (update != null)
            {
                await mgr.DownloadUpdatesAsync(update);
                mgr.ApplyUpdatesAndRestart(update);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnError?.Invoke(ex);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
