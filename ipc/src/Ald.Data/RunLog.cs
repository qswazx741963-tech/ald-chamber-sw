using Ald.Core;
using Microsoft.Data.Sqlite;

namespace Ald.Data;

/// <summary>
/// 跑片履歷（SQLite runs 表）：每次製程 / 自動跑片 / 批次的每一片一筆。
/// 開始時寫一筆（結果 Running），結束時補結束時間與結果；配方內容整份存 JSON，事後可追溯當時參數。
/// 程式中途被關掉（斷電）留下的 Running，下次啟動時標成 Interrupted。
/// </summary>
public sealed class RunLog : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public const string Running = "Running", Interrupted = "Interrupted";

    public RunLog(string path)
    {
        _db = Db.Open(path);
        Db.Exec(_db, """
            CREATE TABLE IF NOT EXISTS runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                t_start TEXT NOT NULL,
                t_end TEXT,
                kind TEXT NOT NULL,
                wafer_id TEXT NOT NULL DEFAULT '',
                recipe TEXT NOT NULL DEFAULT '',
                batch TEXT NOT NULL DEFAULT '',
                result TEXT NOT NULL,
                message TEXT NOT NULL DEFAULT '',
                user_name TEXT NOT NULL DEFAULT '',
                recipe_json TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS runs_start ON runs(t_start);
            """);
    }

    public sealed record Row(long Id, DateTime Start, DateTime? End, string Kind, string WaferId, string Recipe,
                             string Batch, string Result, string Message, string User, string RecipeJson)
    {
        public TimeSpan? Duration => End - Start;
    }

    /// <summary>上次程式結束時還沒結束的紀錄標成 Interrupted。回傳幾筆。</summary>
    public int CloseDangling(DateTime? now = null)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE runs SET t_end = $t, result = $r, message = '程式關閉或斷電，沒有正常結束' WHERE result = $run";
            cmd.Parameters.AddWithValue("$t", (now ?? DateTime.Now).ToString("O"));
            cmd.Parameters.AddWithValue("$r", Interrupted);
            cmd.Parameters.AddWithValue("$run", Running);
            return cmd.ExecuteNonQuery();
        }
    }

    /// <summary>開始一筆，回傳 id（給 End 用）。</summary>
    public long Start(string kind, string waferId, ProcessRecipe recipe, string user, string batch = "", DateTime? time = null)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO runs(t_start, kind, wafer_id, recipe, batch, result, user_name, recipe_json)
                VALUES ($t, $k, $w, $r, $b, $res, $u, $j); SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$t", (time ?? DateTime.Now).ToString("O"));
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$w", waferId.Trim());
            cmd.Parameters.AddWithValue("$r", recipe.Name);
            cmd.Parameters.AddWithValue("$b", batch);
            cmd.Parameters.AddWithValue("$res", Running);
            cmd.Parameters.AddWithValue("$u", user);
            cmd.Parameters.AddWithValue("$j", RecipeFile.ToJson(recipe));
            return (long)cmd.ExecuteScalar()!;
        }
    }

    /// <param name="result">Done / Failed / Aborted（SequencerState 名稱）</param>
    public void End(long id, string result, string message, DateTime? time = null)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE runs SET t_end = $t, result = $r, message = $m WHERE id = $id";
            cmd.Parameters.AddWithValue("$t", (time ?? DateTime.Now).ToString("O"));
            cmd.Parameters.AddWithValue("$r", result);
            cmd.Parameters.AddWithValue("$m", message);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>查詢（新的在前）。keyword 比對晶圓編號、配方、批次、訊息、使用者；result = null 全部。</summary>
    public IReadOnlyList<Row> Query(DateTime from, DateTime to, string? keyword = null, string? result = null, int limit = 5000)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            var where = new List<string> { "t_start >= $f", "t_start <= $t" };
            cmd.Parameters.AddWithValue("$f", from.ToString("O"));
            cmd.Parameters.AddWithValue("$t", to.ToString("O"));
            if (!string.IsNullOrEmpty(result)) { where.Add("result = $res"); cmd.Parameters.AddWithValue("$res", result); }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                where.Add("(wafer_id LIKE $k ESCAPE '\\' OR recipe LIKE $k ESCAPE '\\' OR batch LIKE $k ESCAPE '\\' OR message LIKE $k ESCAPE '\\' OR user_name LIKE $k ESCAPE '\\')");
                cmd.Parameters.AddWithValue("$k", $"%{Like(keyword.Trim())}%");
            }
            cmd.CommandText = $"SELECT id, t_start, t_end, kind, wafer_id, recipe, batch, result, message, user_name, recipe_json FROM runs WHERE {string.Join(" AND ", where)} ORDER BY id DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", limit);
            using var r = cmd.ExecuteReader();
            var list = new List<Row>();
            while (r.Read())
                list.Add(new(r.GetInt64(0), DateTime.Parse(r.GetString(1)), r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2)),
                    r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10)));
            return list;
        }
    }

    internal static string Like(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>匯出 CSV（UTF-8 BOM），時間由舊到新；不含配方 JSON（太長），配方內容看畫面或資料庫。</summary>
    public int ExportCsv(string path, DateTime from, DateTime to, string? keyword = null, string? result = null)
    {
        var rows = Query(from, to, keyword, result, int.MaxValue).Reverse().ToList();
        Csv.Write(path, new[] { "id", "start", "end", "duration_s", "kind", "wafer_id", "recipe", "batch", "result", "message", "user" },
            rows.Select(x => new[]
            {
                x.Id.ToString(), x.Start.ToString("yyyy-MM-dd HH:mm:ss"), x.End?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                x.Duration is { } d ? d.TotalSeconds.ToString("0") : "", x.Kind, x.WaferId, x.Recipe, x.Batch, x.Result, x.Message, x.User,
            }));
        return rows.Count;
    }

    public void Dispose() => _db.Dispose();
}
