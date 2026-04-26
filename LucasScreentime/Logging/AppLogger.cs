using System.IO;

namespace LucasScreentime.Logging;

public static class AppLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LucasScreentime", "app.log");

    private static readonly object _lock = new();

    public static string LogFilePath => LogPath;

    public static void Log(string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | {message}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, line + Environment.NewLine);
                TrimIfNeeded();
            }
            catch { }
        }
    }

    private static void TrimIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || info.Length < 200 * 1024) return;
            var lines = File.ReadAllLines(LogPath);
            File.WriteAllLines(LogPath, lines[^(lines.Length / 2)..]);
        }
        catch { }
    }
}
