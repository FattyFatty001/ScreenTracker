using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LucasScreentime.Settings;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LucasScreentime", "settings.json");

    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPasswordEncrypted { get; set; } = "";
    public List<string> ToAddresses { get; set; } = new();
    public string NotifyWindowStart { get; set; } = "20:45";
    public string NotifyWindowEnd { get; set; } = "21:00";
    public int UpdateCheckIntervalMinutes { get; set; } = 15;
    public string GitHubRepo { get; set; } = "FattyFatty001/ScreenTracker";
    public bool IsConfigured { get; set; } = false;

    [JsonIgnore]
    public string SmtpPassword
    {
        get
        {
            if (string.IsNullOrEmpty(SmtpPasswordEncrypted)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(SmtpPasswordEncrypted), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                SmtpPasswordEncrypted = "";
                return;
            }
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            SmtpPasswordEncrypted = Convert.ToBase64String(bytes);
        }
    }

    [JsonIgnore]
    public TimeSpan NotifyStart => ParseTime(NotifyWindowStart, new TimeSpan(20, 45, 0));

    [JsonIgnore]
    public TimeSpan NotifyEnd => ParseTime(NotifyWindowEnd, new TimeSpan(21, 0, 0));

    private static TimeSpan ParseTime(string value, TimeSpan fallback)
    {
        if (TimeSpan.TryParse(value, out var result)) return result;
        return fallback;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
