using Microsoft.Data.Sqlite;

namespace Ald.Data;

/// <summary>警報與事件記錄（SQLite）。製程資料記錄 (trend) 之後另開資料表。TODO 保存天數</summary>
public sealed class AlarmLog : IDisposable
{
    private readonly SqliteConnection _db;

    public AlarmLog(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS alarm (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                time TEXT NOT NULL,
                source TEXT NOT NULL,
                level TEXT NOT NULL,
                message TEXT NOT NULL)
            """;
        cmd.ExecuteNonQuery();
    }

    public void Add(string source, string level, string message)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT INTO alarm(time, source, level, message) VALUES ($t, $s, $l, $m)";
        cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$l", level);
        cmd.Parameters.AddWithValue("$m", message);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
