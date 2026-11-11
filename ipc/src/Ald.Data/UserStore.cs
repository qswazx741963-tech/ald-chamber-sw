using System.Security.Cryptography;
using Ald.Core;
using Microsoft.Data.Sqlite;

namespace Ald.Data;

/// <summary>
/// 使用者帳號（SQLite users 表）。密碼只存 PBKDF2-SHA256 雜湊 + 隨機 salt，不存明碼。
/// 第一次啟動沒有任何帳號時建立 admin，密碼 = config hmi.defaultAdminPasswordHash（出廠密碼，只存雜湊；空白 = admin），
/// 還沒改之前畫面會提醒。至少要留一個管理員。
/// </summary>
public sealed class UserStore : IDisposable
{
    public const string DefaultAdmin = "admin";
    private const int Iterations = 100_000;
    private readonly SqliteConnection _db;
    private readonly object _lock = new();
    private readonly (byte[] Salt, byte[] Hash, int Iter) _default;

    public sealed record User(string Name, UserRole Role, DateTime Created);

    /// <param name="defaultAdminHash">EncodeHash 格式；null / 空白 = 密碼 admin</param>
    public UserStore(string path, string? defaultAdminHash = null)
    {
        _default = string.IsNullOrWhiteSpace(defaultAdminHash) ? DecodeHash(EncodeHash(DefaultAdmin)) : DecodeHash(defaultAdminHash);
        _db = Db.Open(path);
        Db.Exec(_db, """
            CREATE TABLE IF NOT EXISTS users (
                name TEXT PRIMARY KEY COLLATE NOCASE,
                role INTEGER NOT NULL,
                salt BLOB NOT NULL,
                hash BLOB NOT NULL,
                iter INTEGER NOT NULL,
                created TEXT NOT NULL);
            """);
        if (List().Count == 0) Insert(DefaultAdmin, UserRole.Admin, _default.Salt, _default.Hash, _default.Iter);
    }

    /// <summary>admin 還是出廠密碼（要提醒改）：比對雜湊，不需要知道明碼。</summary>
    public bool DefaultPasswordInUse
    {
        get
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT hash FROM users WHERE name = $n";
                cmd.Parameters.AddWithValue("$n", DefaultAdmin);
                return cmd.ExecuteScalar() is byte[] h && h.AsSpan().SequenceEqual(_default.Hash);
            }
        }
    }

    /// <summary>產生 config 用的雜湊字串：pbkdf2-sha256$次數$salt$hash（base64）。</summary>
    public static string EncodeHash(string password)
    {
        CheckPassword(password);
        var (salt, hash) = Hash(password);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static (byte[] Salt, byte[] Hash, int Iter) DecodeHash(string s)
    {
        var p = s.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2-sha256" || !int.TryParse(p[1], out var iter) || iter < 1000)
            throw new FormatException("出廠密碼雜湊格式錯誤（用 --hash-password 產生）");
        return (Convert.FromBase64String(p[2]), Convert.FromBase64String(p[3]), iter);
    }

    public IReadOnlyList<User> List()
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT name, role, created FROM users ORDER BY role DESC, name";
            using var r = cmd.ExecuteReader();
            var list = new List<User>();
            while (r.Read()) list.Add(new(r.GetString(0), (UserRole)r.GetInt32(1), DateTime.Parse(r.GetString(2))));
            return list;
        }
    }

    /// <summary>帳號密碼正確回傳等級，錯誤回傳 null（不分帳號不存在或密碼錯）。</summary>
    public UserRole? Login(string name, string password)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT role, salt, hash, iter FROM users WHERE name = $n";
            cmd.Parameters.AddWithValue("$n", name.Trim());
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var role = (UserRole)r.GetInt32(0);
            var salt = (byte[])r[1];
            var hash = (byte[])r[2];
            var test = Rfc2898DeriveBytes.Pbkdf2(password, salt, r.GetInt32(3), HashAlgorithmName.SHA256, hash.Length);
            return CryptographicOperations.FixedTimeEquals(test, hash) ? role : null;
        }
    }

    public void Add(string name, UserRole role, string password)
    {
        name = CheckName(name);
        CheckPassword(password);
        if (role == UserRole.Viewer) throw new ArgumentException("帳號等級至少是操作員");
        lock (_lock)
        {
            if (Exists(name)) throw new ArgumentException($"帳號 {name} 已存在");
            var (salt, hash) = Hash(password);
            Insert(name, role, salt, hash, Iterations);
        }
    }

    private void Insert(string name, UserRole role, byte[] salt, byte[] hash, int iter)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO users(name, role, salt, hash, iter, created) VALUES ($n, $r, $s, $h, $i, $c)";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$r", (int)role);
            cmd.Parameters.AddWithValue("$s", salt);
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$i", iter);
            cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void SetPassword(string name, string password)
    {
        CheckPassword(password);
        var (salt, hash) = Hash(password);
        Update(name, "UPDATE users SET salt = $s, hash = $h, iter = $i WHERE name = $n",
            c => { c.Parameters.AddWithValue("$s", salt); c.Parameters.AddWithValue("$h", hash); c.Parameters.AddWithValue("$i", Iterations); });
    }

    public void SetRole(string name, UserRole role)
    {
        if (role == UserRole.Viewer) throw new ArgumentException("帳號等級至少是操作員");
        lock (_lock)
        {
            if (role != UserRole.Admin) KeepOneAdmin(name);
            Update(name, "UPDATE users SET role = $r WHERE name = $n", c => c.Parameters.AddWithValue("$r", (int)role));
        }
    }

    public void Delete(string name)
    {
        lock (_lock)
        {
            KeepOneAdmin(name);
            Update(name, "DELETE FROM users WHERE name = $n", _ => { });
        }
    }

    private void KeepOneAdmin(string name)
    {
        var admins = List().Where(u => u.Role == UserRole.Admin).ToList();
        if (admins.Count == 1 && string.Equals(admins[0].Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("至少要留一個管理員帳號");
    }

    private void Update(string name, string sql, Action<SqliteCommand> p)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$n", name.Trim());
            p(cmd);
            if (cmd.ExecuteNonQuery() == 0) throw new ArgumentException($"沒有帳號 {name}");
        }
    }

    private bool Exists(string name)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    private static (byte[] Salt, byte[] Hash) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return (salt, Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32));
    }

    private static string CheckName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 32) throw new ArgumentException("帳號 1–32 個字");
        return name;
    }

    private static void CheckPassword(string pw)
    {
        if (pw.Length is < 4 or > 64) throw new ArgumentException("密碼 4–64 個字");
    }

    public void Dispose() => _db.Dispose();
}
