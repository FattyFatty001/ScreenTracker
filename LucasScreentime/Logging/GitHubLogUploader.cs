using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LucasScreentime.Settings;
using Timer = System.Threading.Timer;

namespace LucasScreentime.Logging;

public sealed class GitHubLogUploader : IDisposable
{
    private readonly AppSettings _settings;
    private Timer? _timer;
    private static readonly HttpClient _http = new();
    private const string RepoLogPath = "logs/screentime.log";

    public event Action<Exception>? OnError;

    public GitHubLogUploader(AppSettings settings) => _settings = settings;

    public void Start(int intervalMinutes)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));
        // First upload 30s after start, then on interval
        _timer = new Timer(async _ => await UploadAsync(), null, TimeSpan.FromSeconds(30), interval);
    }

    public async Task UploadAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.GitHubPat) ||
            string.IsNullOrWhiteSpace(_settings.GitHubRepo))
            return;

        try
        {
            var content = File.Exists(AppLogger.LogFilePath)
                ? File.ReadAllText(AppLogger.LogFilePath)
                : "(no log entries yet)";

            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
            var sha = await GetFileShaAsync();
            await PutFileAsync(encoded, sha);
            AppLogger.Log("Log uploaded to GitHub");
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    private async Task<string?> GetFileShaAsync()
    {
        var req = BuildRequest(HttpMethod.Get, RepoLogPath);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("sha").GetString();
    }

    private async Task PutFileAsync(string base64Content, string? sha)
    {
        var body = new Dictionary<string, object?>
        {
            ["message"] = $"log update {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
            ["content"] = base64Content,
        };
        if (sha != null) body["sha"] = sha;

        var req = BuildRequest(HttpMethod.Put, RepoLogPath);
        req.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method,
            $"https://api.github.com/repos/{_settings.GitHubRepo}/contents/{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.GitHubPat);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("LucasScreentime", "1.0"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return req;
    }

    public void Dispose() => _timer?.Dispose();
}
