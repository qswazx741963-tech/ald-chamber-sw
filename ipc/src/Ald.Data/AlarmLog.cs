using Ald.Core;
using Microsoft.Data.Sqlite;

namespace Ald.Data;

/// <summary>警報與事件記錄（SQLite）：警報發生 / 解除、互鎖擋下、流程步驟、操作紀錄。</summary>
public sealed class AlarmLog : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public AlarmLog(string path)
    {
        _db = Db.Open(path);
        Db.Exec(_db, """
            CREATE TABLE IF NOT EXISTS alarm (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                time TEXT NOT NULL,
                source TEXT NOT NULL,
                level TEXT NOT NULL,
                message TEXT NOT NULL,
                code TEXT NOT NULL DEFAULT '',
                event TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS alarm_time ON alarm(time);
            """);
    }

    /// <param name="evt">RAISE / CLEAR / EVENT / STEP / OPERATOR</param>
    public void Add(string source, string level, string message, string code = "", string evt = "EVENT", DateTime? time = null)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO alarm(time, source, level, message, code, event) VALUES ($t, $s, $l, $m, $c, $e)";
            cmd.Parameters.AddWithValue("$t", (time ?? DateTime.Now).ToString("O"));
            cmd.Parameters.AddWithValue("$s", source);
            cmd.Parameters.AddWithValue("$l", level);
            cmd.Parameters.AddWithValue("$m", message);
            cmd.Parameters.AddWithValue("$c", code);
            cmd.Parameters.AddWithValue("$e", evt);
            cmd.ExecuteNonQuery();
        }
    }

    public void Add(AlarmChange c)
        => Add(c.Info.Source, c.Info.Level.ToString(), c.Info.Message, c.Info.Code, c.Raised ? "RAISE" : "CLEAR", c.Time);

    public sealed record Row(DateTime Time, string Source, string Level, string Message, string Code, string Event);

    public IReadOnlyList<Row> Recent(int count = 200)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT time, source, level, message, code, event FROM alarm ORDER BY id DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", count);
            using var r = cmd.ExecuteReader();
            var list = new List<Row>();
            while (r.Read())
                list.Add(new(DateTime.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
            return list;
        }
    }

    public void Purge(int keepDays)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM alarm WHERE time < $t";
            cmd.Parameters.AddWithValue("$t", DateTime.Now.AddDays(-keepDays).ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose() => _db.Dispose();
}

internal static class Db
{
    public static SqliteConnection Open(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var db = new SqliteConnection($"Data Source={path}");
        db.Open();
        Exec(db, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");   // 斷電時資料庫不會壞
        return db;
    }

    public static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
