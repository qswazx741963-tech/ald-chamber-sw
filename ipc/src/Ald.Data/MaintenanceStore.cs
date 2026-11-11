using Ald.Core;
using Microsoft.Data.Sqlite;

namespace Ald.Data;

/// <summary>
/// 保養計數與漏率測試記錄（SQLite）。
/// 計數：每筆狀態呼叫 Track()，增量先累加在記憶體，每 flushInterval（預設 60 s）與關閉時寫回資料庫，
/// 斷電最多少算最後一分鐘。每項有「保養後累計」（可歸零）與「總累計」（不歸零）兩個值；
/// 提醒上限 0 = 不提醒，超過上限時觸發一次 BecameDue（歸零或改上限後重新判斷）。
/// </summary>
public sealed class MaintenanceStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();
    private readonly Dictionary<string, Counter> _c = new();
    private readonly TimeSpan _flushInterval;
    private DateTime _lastFlush = DateTime.Now;
    private MachineSnapshot? _prev;

    private sealed class Counter
    {
        public double Value, Total, Limit;
        public DateTime? ResetTime;
        public string ResetBy = "";
        public bool Dirty, DueRaised;
    }

    public sealed record Row(MaintItem Item, double Value, double Total, double Limit, DateTime? ResetTime, string ResetBy)
    {
        public bool Due => Limit > 0 && Value >= Limit;
        public double Percent => Limit > 0 ? Value / Limit * 100 : double.NaN;
    }

    /// <summary>某項超過提醒上限（在呼叫 Track 的執行緒觸發）。</summary>
    public event Action<Row>? BecameDue;

    public MaintenanceStore(string path, TimeSpan? flushInterval = null)
    {
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(60);
        _db = Db.Open(path);
        Db.Exec(_db, """
            CREATE TABLE IF NOT EXISTS maint (
                id TEXT PRIMARY KEY,
                value REAL NOT NULL DEFAULT 0,
                total REAL NOT NULL DEFAULT 0,
                limit_value REAL NOT NULL DEFAULT 0,
                reset_time TEXT,
                reset_by TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS leak_test (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                time TEXT NOT NULL,
                user TEXT NOT NULL,
                gauge TEXT NOT NULL,
                start_torr REAL, end_torr REAL, measure_s REAL, samples INTEGER,
                ror_mtorr_min REAL, leak_torr_l_s REAL, r2 REAL, limit_mtorr_min REAL,
                pass INTEGER NOT NULL,
                note TEXT NOT NULL DEFAULT '');
            """);
        foreach (var it in Maintenance.Items)
            Db.Exec(_db, $"INSERT OR IGNORE INTO maint(id) VALUES ('{it.Id}')");
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, value, total, limit_value, reset_time, reset_by FROM maint";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            _c[r.GetString(0)] = new Counter
            {
                Value = r.GetDouble(1), Total = r.GetDouble(2), Limit = r.GetDouble(3),
                ResetTime = r.IsDBNull(4) ? null : DateTime.Parse(r.GetString(4)), ResetBy = r.GetString(5),
            };
        foreach (var c in _c.Values) c.DueRaised = c.Limit > 0 && c.Value >= c.Limit;   // 上次就到期的不再重複提醒
    }

    /// <summary>每筆狀態都可以呼叫（MachineService.Updated）。</summary>
    public void Track(MachineSnapshot s, AldRecipe? ald)
    {
        var due = new List<Row>();
        lock (_lock)
        {
            if (_prev is not null)
                foreach (var (id, v) in Maintenance.Delta(_prev, s, ald))
                {
                    if (!_c.TryGetValue(id, out var c)) continue;
                    c.Value += v;
                    c.Total += v;
                    c.Dirty = true;
                    if (!c.DueRaised && c.Limit > 0 && c.Value >= c.Limit) { c.DueRaised = true; due.Add(ToRow(id, c)); }
                }
            _prev = s;
            if (DateTime.Now - _lastFlush >= _flushInterval) FlushLocked();
        }
        foreach (var r in due) BecameDue?.Invoke(r);
    }

    public IReadOnlyList<Row> Rows()
    {
        lock (_lock) return Maintenance.Items.Select(it => ToRow(it.Id, _c[it.Id])).ToList();
    }

    /// <summary>保養完成：保養後累計歸零（總累計保留）。</summary>
    public void Reset(string id, string user)
    {
        lock (_lock)
        {
            var c = Get(id);
            c.Value = 0;
            c.ResetTime = DateTime.Now;
            c.ResetBy = user;
            c.DueRaised = false;
            c.Dirty = true;
            FlushLocked();
        }
    }

    /// <summary>提醒上限（同單位），0 = 不提醒。</summary>
    public void SetLimit(string id, double limit)
    {
        if (!(limit >= 0) || double.IsInfinity(limit)) throw new ArgumentException("上限要 ≥ 0");
        lock (_lock)
        {
            var c = Get(id);
            c.Limit = limit;
            c.DueRaised = limit > 0 && c.Value >= limit;
            c.Dirty = true;
            FlushLocked();
        }
    }

    public void Flush() { lock (_lock) FlushLocked(); }

    private Counter Get(string id) => _c.TryGetValue(id, out var c) ? c : throw new ArgumentException($"沒有保養項目 {id}");

    private static Row ToRow(string id, Counter c)
        => new(Maintenance.Items.First(i => i.Id == id), c.Value, c.Total, c.Limit, c.ResetTime, c.ResetBy);

    private void FlushLocked()
    {
        _lastFlush = DateTime.Now;
        var dirty = _c.Where(kv => kv.Value.Dirty).ToList();
        if (dirty.Count == 0) return;
        using var tx = _db.BeginTransaction();
        foreach (var (id, c) in dirty)
        {
            using var cmd = _db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE maint SET value=$v, total=$t, limit_value=$l, reset_time=$rt, reset_by=$rb WHERE id=$id";
            cmd.Parameters.AddWithValue("$v", c.Value);
            cmd.Parameters.AddWithValue("$t", c.Total);
            cmd.Parameters.AddWithValue("$l", c.Limit);
            cmd.Parameters.AddWithValue("$rt", (object?)c.ResetTime?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rb", c.ResetBy);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            c.Dirty = false;
        }
        tx.Commit();
    }

    // ------------------------------------------------------------ 漏率測試記錄
    public sealed record LeakRow(DateTime Time, string User, LeakTestResult Result);

    public void AddLeakTest(LeakTestResult r, string user)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO leak_test(time, user, gauge, start_torr, end_torr, measure_s, samples, ror_mtorr_min, leak_torr_l_s, r2, limit_mtorr_min, pass, note)
                VALUES ($t, $u, $g, $p0, $p1, $ms, $n, $ror, $q, $r2, $lim, $pass, $note)
                """;
            static object N(double v) => double.IsNaN(v) || double.IsInfinity(v) ? DBNull.Value : v;
            cmd.Parameters.AddWithValue("$t", r.Time.ToString("O"));
            cmd.Parameters.AddWithValue("$u", user);
            cmd.Parameters.AddWithValue("$g", r.Gauge);
            cmd.Parameters.AddWithValue("$p0", N(r.StartTorr));
            cmd.Parameters.AddWithValue("$p1", N(r.EndTorr));
            cmd.Parameters.AddWithValue("$ms", N(r.MeasureS));
            cmd.Parameters.AddWithValue("$n", r.Samples);
            cmd.Parameters.AddWithValue("$ror", N(r.RorMtorrPerMin));
            cmd.Parameters.AddWithValue("$q", N(r.LeakTorrLps));
            cmd.Parameters.AddWithValue("$r2", N(r.R2));
            cmd.Parameters.AddWithValue("$lim", N(r.LimitMtorrPerMin));
            cmd.Parameters.AddWithValue("$pass", r.Pass ? 1 : 0);
            cmd.Parameters.AddWithValue("$note", r.Note);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<LeakRow> LeakTests(int count = 100)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT time, user, gauge, start_torr, end_torr, measure_s, samples, ror_mtorr_min, leak_torr_l_s, r2, limit_mtorr_min, pass, note
                FROM leak_test ORDER BY id DESC LIMIT $n
                """;
            cmd.Parameters.AddWithValue("$n", count);
            using var r = cmd.ExecuteReader();
            double D(int i) => r.IsDBNull(i) ? double.NaN : r.GetDouble(i);
            var list = new List<LeakRow>();
            while (r.Read())
            {
                var t = DateTime.Parse(r.GetString(0));
                list.Add(new(t, r.GetString(1), new LeakTestResult(t, r.GetString(2), D(3), D(4), D(5), r.GetInt32(6),
                    D(7), D(8), D(9), D(10), r.GetInt32(11) != 0, r.GetString(12))));
            }
            return list;
        }
    }

    public void Dispose()
    {
        try { Flush(); } catch (Exception) { /* 關閉時寫不進去就算了 */ }
        _db.Dispose();
    }
}
