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

    /// <summary>
    /// 查詢事件記錄（新的在前）。level = null 全部；keyword 比對來源與訊息（不分大小寫）。
    /// </summary>
    public IReadOnlyList<Row> Query(DateTime from, DateTime to, string? level = null, string? keyword = null, int limit = 5000)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            var where = new List<string> { "time >= $f", "time <= $t" };
            cmd.Parameters.AddWithValue("$f", from.ToString("O"));
            cmd.Parameters.AddWithValue("$t", to.ToString("O"));
            if (!string.IsNullOrEmpty(level)) { where.Add("level = $l"); cmd.Parameters.AddWithValue("$l", level); }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                where.Add("(source LIKE $k ESCAPE '\\' OR message LIKE $k ESCAPE '\\')");
                cmd.Parameters.AddWithValue("$k", $"%{RunLog.Like(keyword.Trim())}%");
            }
            cmd.CommandText = $"SELECT time, source, level, message, code, event FROM alarm WHERE {string.Join(" AND ", where)} ORDER BY id DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", limit);
            using var r = cmd.ExecuteReader();
            var list = new List<Row>();
            while (r.Read())
                list.Add(new(DateTime.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
            return list;
        }
    }

    /// <summary>事件記錄匯出 CSV（UTF-8 BOM，Excel 直接開），時間由舊到新。</summary>
    public int ExportCsv(string path, DateTime from, DateTime to, string? level = null, string? keyword = null)
    {
        var rows = Query(from, to, level, keyword, int.MaxValue).Reverse().ToList();
        Csv.Write(path, new[] { "time", "event", "level", "source", "message", "code" },
            rows.Select(x => new[] { x.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"), x.Event, x.Level, x.Source, x.Message, x.Code }));
        return rows.Count;
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

/// <summary>CSV 輸出（UTF-8 BOM；有逗號、引號、換行的欄位加引號）。</summary>
public static class Csv
{
    public static string Field(string s)
        => s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static void Write(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        w.WriteLine(string.Join(",", header.Select(Field)));
        foreach (var r in rows) w.WriteLine(string.Join(",", r.Select(Field)));
    }
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
