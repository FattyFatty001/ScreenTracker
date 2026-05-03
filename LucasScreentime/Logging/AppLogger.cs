using System.IO;

namespace LucasScreentime.Logging;

public static class AppLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LucasScreentime");

    private static readonly object _lock = new();

    /// <summary>Path to today's log file.</summary>
    public static string LogFilePath => GetLogPathForDate(DateTime.Now);

    /// <summary>Path to the log file for a specific local date.</summary>
    public static string GetLogPathForDate(DateTime date) =>
        Path.Combine(LogDir, $"app-{date:yyyy-MM-dd}.log");

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
                CleanOldLogs();
            }
            catch { }
        }
    }

    /// <summary>Delete log files older than 30 days.</summary>
    private static void CleanOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-30);
            foreach (var file in Directory.GetFiles(LogDir, "app-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);          // "app-2025-07-15"
                var datePart = name.Length >= 14 ? name[4..] : name[4..];   // "2025-07-15"
                if (DateTime.TryParse(datePart, out var fileDate) && fileDate < cutoff)
                    File.Delete(file);
            }
        }
        catch { }
    }
}
