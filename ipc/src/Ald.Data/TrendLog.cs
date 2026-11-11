using Ald.Core;
using Microsoft.Data.Sqlite;

namespace Ald.Data;

/// <summary>
/// 製程趨勢記錄（SQLite，每 dataLog.intervalMs 一筆）：壓力、溫度、MFC、閥狀態、ALD cycle。
/// 一筆一列（寬表），查詢 / 匯出 CSV 都簡單；一天 86400 筆約 30 MB，保存 keepDays 天後自動刪除。
/// </summary>
public sealed class TrendLog : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();
    private DateTime _last = DateTime.MinValue;
    private readonly TimeSpan _interval;

    public static readonly string[] Columns = BuildColumns();

    private static string[] BuildColumns()
    {
        var c = new List<string> { "pm_torr", "manometer_torr", "ion_torr", "foreline_torr", "ll_torr" };
        for (int i = 1; i <= 16; i++) c.Add($"t{i:00}");
        for (int i = 1; i <= 16; i++) c.Add($"duty{i:00}");
        for (int i = 1; i <= 8; i++) c.Add($"mfc{i}");
        c.AddRange(new[] { "vac_do", "ald_valves", "mfc_up", "ald_cycle", "sys_state", "heater_pos", "ll_sv" });
        return c.ToArray();
    }

    public TrendLog(string path, TimeSpan interval)
    {
        _interval = interval;
        _db = Db.Open(path);
        Db.Exec(_db, $"CREATE TABLE IF NOT EXISTS trend (time TEXT PRIMARY KEY, {string.Join(", ", Columns.Select(c => c + " REAL"))});");
    }

    private static double? N(double v) => double.IsNaN(v) || double.IsInfinity(v) ? null : v;

    private static IEnumerable<double?> Values(MachineSnapshot s)
    {
        yield return N(s.ChamberPressureTorr);
        yield return N(s.ManometerTorr);
        yield return N(s.IonGaugeTorr);
        yield return N(s.ForelineTorr);
        yield return N(s.LoadlockPressureUnderRange ? 0.999 : s.LoadlockPressureTorr);   // SLK 低於下限記成 < 1
        foreach (var t in s.TempC) yield return N(t);
        foreach (var d in s.HeaterDutyPct) yield return N(d);
        foreach (var m in s.MfcValue) yield return N(m);
        yield return (double)s.VacuumOutputs;
        yield return s.AldValves;
        yield return s.MfcUpstreamValves;
        yield return s.AldCycle;
        yield return (double)s.SysState;
        yield return s.HeaterPosSteps;
        yield return (double)s.SlitValve;
    }

    /// <summary>每次輪詢都可以呼叫，未滿間隔就略過。主控板沒連上不記。</summary>
    public bool Add(MachineSnapshot s)
    {
        if (!s.Connected || s.Time - _last < _interval) return false;
        _last = s.Time;
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"INSERT OR REPLACE INTO trend (time, {string.Join(", ", Columns)}) VALUES ($t, {string.Join(", ", Columns.Select((_, i) => "$v" + i))})";
            cmd.Parameters.AddWithValue("$t", s.Time.ToString("O"));
            int k = 0;
            foreach (var v in Values(s)) cmd.Parameters.AddWithValue("$v" + k++, (object?)v ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        return true;
    }

    /// <summary>讀一段時間的某個欄位（畫趨勢圖用）。</summary>
    public IReadOnlyList<(DateTime Time, double? Value)> Query(string column, DateTime from, DateTime to)
    {
        if (!Columns.Contains(column)) throw new ArgumentException($"沒有欄位 {column}");
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"SELECT time, {column} FROM trend WHERE time >= $a AND time <= $b ORDER BY time";
            cmd.Parameters.AddWithValue("$a", from.ToString("O"));
            cmd.Parameters.AddWithValue("$b", to.ToString("O"));
            using var r = cmd.ExecuteReader();
            var list = new List<(DateTime, double?)>();
            while (r.Read()) list.Add((DateTime.Parse(r.GetString(0)), r.IsDBNull(1) ? null : r.GetDouble(1)));
            return list;
        }
    }

    /// <summary>
    /// 一次讀多個欄位（趨勢圖用）。點數超過 maxPoints 時平均抽樣（每 k 筆取 1 筆），24 小時的資料也能很快畫出來。
    /// 沒有值（斷線 / 未設定）回傳 NaN。
    /// </summary>
    public (DateTime[] Time, Dictionary<string, double[]> Data) QueryMany(IReadOnlyList<string> columns, DateTime from, DateTime to, int maxPoints = 4000)
    {
        foreach (var c in columns) if (!Columns.Contains(c)) throw new ArgumentException($"沒有欄位 {c}");
        var time = new List<DateTime>();
        var vals = columns.Select(_ => new List<double>()).ToArray();
        lock (_lock)
        {
            using var cnt = _db.CreateCommand();
            cnt.CommandText = "SELECT COUNT(*) FROM trend WHERE time >= $a AND time <= $b";
            cnt.Parameters.AddWithValue("$a", from.ToString("O"));
            cnt.Parameters.AddWithValue("$b", to.ToString("O"));
            long n = (long)(cnt.ExecuteScalar() ?? 0L);
            long step = Math.Max(1, (n + maxPoints - 1) / Math.Max(1, maxPoints));

            using var cmd = _db.CreateCommand();
            var cols = columns.Count == 0 ? "" : ", " + string.Join(", ", columns);
            cmd.CommandText = $"SELECT time{cols} FROM trend WHERE time >= $a AND time <= $b ORDER BY time";
            cmd.Parameters.AddWithValue("$a", from.ToString("O"));
            cmd.Parameters.AddWithValue("$b", to.ToString("O"));
            using var r = cmd.ExecuteReader();
            long i = 0;
            while (r.Read())
            {
                if (i++ % step != 0) continue;
                time.Add(DateTime.Parse(r.GetString(0)));
                for (int c = 0; c < columns.Count; c++) vals[c].Add(r.IsDBNull(c + 1) ? double.NaN : r.GetDouble(c + 1));
            }
        }
        var data = new Dictionary<string, double[]>();
        for (int c = 0; c < columns.Count; c++) data[columns[c]] = vals[c].ToArray();
        return (time.ToArray(), data);
    }

    /// <summary>匯出 CSV（全部欄位，UTF-8 含 BOM，Excel 直接開）。回傳筆數。</summary>
    public int ExportCsv(string path, DateTime from, DateTime to)
    {
        var (t, d) = QueryMany(Columns, from, to, int.MaxValue);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        w.WriteLine("time," + string.Join(",", Columns));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < t.Length; i++)
            w.WriteLine(t[i].ToString("yyyy-MM-dd HH:mm:ss.fff", inv) + "," +
                        string.Join(",", Columns.Select(c => double.IsNaN(d[c][i]) ? "" : d[c][i].ToString("G6", inv))));
        return t.Length;
    }

    public void Purge(int keepDays)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM trend WHERE time < $t";
            cmd.Parameters.AddWithValue("$t", DateTime.Now.AddDays(-keepDays).ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose() => _db.Dispose();
}
