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

    public bool HasSentNotificationForDate(string date)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM daily_notifications WHERE date = $date";
        cmd.Parameters.AddWithValue("$date", date);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public void MarkNotificationSentForDate(string date)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO daily_notifications (date) VALUES ($date)";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.ExecuteNonQuery();
    }

    public TimeSpan GetTotalForDate(DateTime localDate)
    {
        var dayStartLocal = localDate.Date;
        var dayEndLocal = dayStartLocal.AddDays(1);
        var dayStartUtc = dayStartLocal.ToUniversalTime();
        var dayEndUtc = dayEndLocal.ToUniversalTime();

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT start_utc, end_utc FROM sessions
            WHERE start_utc < $dayEnd
            AND (end_utc IS NULL OR end_utc > $dayStart)
            """;
        cmd.Parameters.AddWithValue("$dayStart", dayStartUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$dayEnd", dayEndUtc.ToString("O"));

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

            // Clamp to the given day's local boundaries
            var clampedStart = startLocal < dayStartLocal ? dayStartLocal : startLocal;
            var clampedEnd = endLocal > dayEndLocal ? dayEndLocal : endLocal;

            if (clampedEnd > clampedStart)
                total += clampedEnd - clampedStart;
        }
        return total;
    }

    public int[] GetHourlyBreakdownForDate(DateTime localDate)
    {
        var dayStartLocal = localDate.Date;
        var dayEndLocal = dayStartLocal.AddDays(1);
        var dayStartUtc = dayStartLocal.ToUniversalTime();
        var dayEndUtc = dayEndLocal.ToUniversalTime();

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT start_utc, end_utc FROM sessions
            WHERE start_utc < $dayEnd
            AND (end_utc IS NULL OR end_utc > $dayStart)
            """;
        cmd.Parameters.AddWithValue("$dayStart", dayStartUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$dayEnd", dayEndUtc.ToString("O"));

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

            var clampedStart = startLocal < dayStartLocal ? dayStartLocal : startLocal;
            var clampedEnd = endLocal > dayEndLocal ? dayEndLocal : endLocal;

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

    public List<string> GetMissedNotificationDates(int maxDaysBack = 7)
    {
        var missed = new List<string>();
        var todayLocal = DateTime.Now.Date;

        // Generate candidate dates from (today - maxDaysBack) to yesterday
        for (int daysBack = maxDaysBack; daysBack >= 1; daysBack--)
        {
            var date = todayLocal.AddDays(-daysBack);
            var dateStr = date.ToString("yyyy-MM-dd");

            // Skip if notification already sent
            if (HasSentNotificationForDate(dateStr))
                continue;

            // Check if there was any screen time on this date
            var totalForDate = GetTotalForDate(date);
            if (totalForDate > TimeSpan.Zero)
                missed.Add(dateStr);
        }

        return missed;
    }
}
