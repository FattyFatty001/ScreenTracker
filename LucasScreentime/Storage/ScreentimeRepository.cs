using System.IO;
using Microsoft.Data.Sqlite;

namespace LucasScreentime.Storage;

public class ScreentimeRepository
{
    private readonly string _dbPath;

    public ScreentimeRepository()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LucasScreentime");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "screentime.db");
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                start_utc TEXT NOT NULL,
                end_utc TEXT
            );
            CREATE TABLE IF NOT EXISTS daily_notifications (
                date TEXT PRIMARY KEY
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    public long StartSession(DateTime startUtc)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions (start_utc) VALUES ($start); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$start", startUtc.ToString("O"));
        return (long)cmd.ExecuteScalar()!;
    }

    public void EndSession(long sessionId, DateTime endUtc)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET end_utc = $end WHERE id = $id";
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    public void CloseOpenSessions(DateTime endUtc)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET end_utc = $end WHERE end_utc IS NULL";
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public TimeSpan GetTodayTotal()
    {
        var localNow = DateTime.Now;
        var todayStartLocal = localNow.Date;
        var todayStartUtc = todayStartLocal.ToUniversalTime();
        var todayEndUtc = todayStartUtc.AddDays(1);

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT start_utc, end_utc FROM sessions
            WHERE start_utc < $dayEnd
            AND (end_utc IS NULL OR end_utc > $dayStart)
            """;
        cmd.Parameters.AddWithValue("$dayStart", todayStartUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$dayEnd", todayEndUtc.ToString("O"));

        var total = TimeSpan.Zero;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var startUtc = DateTime.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind);
            var endUtc = reader.IsDBNull(1)
                ? DateTime.UtcNow
                : DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind);

            var startLocal = startUtc.ToLocalTime();
            var endLocal = endUtc.ToLocalTime();

            // Clamp to today's local boundaries
            var clampedStart = startLocal < todayStartLocal ? todayStartLocal : startLocal;
            var clampedEnd = endLocal > localNow ? localNow : endLocal;

            if (clampedEnd > clampedStart)
                total += clampedEnd - clampedStart;
        }
        return total;
    }

    public int[] GetHourlyBreakdown()
    {
        var localNow = DateTime.Now;
        var todayStartLocal = localNow.Date;
        var todayStartUtc = todayStartLocal.ToUniversalTime();
        var todayEndUtc = todayStartUtc.AddDays(1);

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT start_utc, end_utc FROM sessions
            WHERE start_utc < $dayEnd
            AND (end_utc IS NULL OR end_utc > $dayStart)
            """;
        cmd.Parameters.AddWithValue("$dayStart", todayStartUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$dayEnd", todayEndUtc.ToString("O"));

        var buckets = new double[24];
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var startUtc = DateTime.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind);
            var endUtc = reader.IsDBNull(1)
                ? DateTime.UtcNow
                : DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind);

            var startLocal = startUtc.ToLocalTime();
            var endLocal = endUtc.ToLocalTime();

            var clampedStart = startLocal < todayStartLocal ? todayStartLocal : startLocal;
            var clampedEnd = endLocal > localNow ? localNow : endLocal;

            if (clampedEnd <= clampedStart) continue;

            var cursor = clampedStart;
            while (cursor < clampedEnd)
            {
                int hour = cursor.Hour;
                var hourEnd = cursor.Date.AddHours(hour + 1);
                var segEnd = hourEnd < clampedEnd ? hourEnd : clampedEnd;
                buckets[hour] += (segEnd - cursor).TotalMinutes;
                cursor = segEnd;
            }
        }

        return buckets.Select(m => (int)Math.Round(m)).ToArray();
    }

    public bool HasSentNotificationToday()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM daily_notifications WHERE date = $date";
        cmd.Parameters.AddWithValue("$date", today);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public void MarkNotificationSent()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO daily_notifications (date) VALUES ($date)";
        cmd.Parameters.AddWithValue("$date", today);
        cmd.ExecuteNonQuery();
    }
}
