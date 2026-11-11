using Ald.Core;
using Ald.Data;
using Xunit;

namespace Ald.Core.Tests;

public sealed class DataTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ald-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _db, _db + "-wal", _db + "-shm" }) File.Delete(f);
    }

    [Fact]
    public void Access_Table()
    {
        Assert.True(Access.Can(UserRole.Viewer, Permission.Stop));                // 停機永遠能按
        Assert.False(Access.Can(UserRole.Viewer, Permission.Reset));
        Assert.True(Access.Can(UserRole.Operator, Permission.RunRecipe));
        Assert.False(Access.Can(UserRole.Operator, Permission.ManualControl));
        Assert.False(Access.Can(UserRole.Operator, Permission.EditRecipe));
        Assert.True(Access.Can(UserRole.Engineer, Permission.ManualControl));
        Assert.False(Access.Can(UserRole.Engineer, Permission.ManageUsers));
        Assert.All(Enum.GetValues<Permission>(), p => Assert.True(Access.Can(UserRole.Admin, p)));
    }

    [Fact]
    public void Users_DefaultAdmin_LoginAndChange()
    {
        using var u = new UserStore(_db);
        Assert.True(u.DefaultPasswordInUse);
        Assert.Equal(UserRole.Admin, u.Login("admin", "admin"));
        Assert.Equal(UserRole.Admin, u.Login(" ADMIN ", "admin"));   // 帳號不分大小寫、去空白
        Assert.Null(u.Login("admin", "wrong"));
        Assert.Null(u.Login("nobody", "admin"));

        u.SetPassword("admin", "s3cret");
        Assert.False(u.DefaultPasswordInUse);
        Assert.Null(u.Login("admin", "admin"));
        Assert.Equal(UserRole.Admin, u.Login("admin", "s3cret"));
    }

    [Fact]
    public void Users_FactoryPassword_FromConfigHash()
    {
        var hash = UserStore.EncodeHash("Factory#123");
        Assert.StartsWith("pbkdf2-sha256$100000$", hash);
        Assert.NotEqual(hash, UserStore.EncodeHash("Factory#123"));   // 每次 salt 不同
        using var u = new UserStore(_db, hash);
        Assert.Equal(UserRole.Admin, u.Login("admin", "Factory#123"));
        Assert.Null(u.Login("admin", "admin"));
        Assert.True(u.DefaultPasswordInUse);
        u.SetPassword("admin", "changed!");
        Assert.False(u.DefaultPasswordInUse);
    }

    [Fact]
    public void RepoConfig_HasFactoryPasswordHash_NotPlainText()
    {
        var h = Repo.Config.Hmi;
        Assert.StartsWith("pbkdf2-sha256$", h.DefaultAdminPasswordHash);
        Assert.Equal(30, h.IdleLogoutMinutes);
        using var u = new UserStore(_db, h.DefaultAdminPasswordHash);
        Assert.Null(u.Login("admin", "admin"));
        Assert.True(u.DefaultPasswordInUse);
        Assert.Throws<FormatException>(() => new UserStore(_db + "2", "plain-text"));
    }

    [Fact]
    public void Users_AddRoleDelete_KeepsOneAdmin()
    {
        using (var u = new UserStore(_db))
        {
            u.Add("op1", UserRole.Operator, "1234");
            u.Add("陳工程師", UserRole.Engineer, "abcd");
            Assert.Throws<ArgumentException>(() => u.Add("OP1", UserRole.Operator, "1234"));     // 重複
            Assert.Throws<ArgumentException>(() => u.Add("x", UserRole.Operator, "12"));         // 密碼太短
            Assert.Throws<ArgumentException>(() => u.Add("v", UserRole.Viewer, "1234"));
            Assert.Equal(UserRole.Engineer, u.Login("陳工程師", "abcd"));

            Assert.Throws<InvalidOperationException>(() => u.Delete("admin"));                  // 唯一的管理員
            Assert.Throws<InvalidOperationException>(() => u.SetRole("admin", UserRole.Operator));
            u.SetRole("op1", UserRole.Admin);
            u.SetRole("admin", UserRole.Operator);                                               // 還有 op1 是管理員
            u.Delete("admin");
            Assert.Throws<ArgumentException>(() => u.Delete("admin"));
        }
        using (var u2 = new UserStore(_db))   // 重開不會再建 admin
        {
            Assert.Equal(new[] { "op1", "陳工程師" }, u2.List().Select(x => x.Name));
            Assert.Null(u2.Login("admin", "admin"));
        }
    }

    [Fact]
    public void Trend_QueryMany_Downsamples_AndExportsCsv()
    {
        using var t = new TrendLog(_db, TimeSpan.Zero);
        var t0 = new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Local);
        for (int i = 0; i < 100; i++)
        {
            var temp = Enumerable.Repeat(double.NaN, 16).ToArray();
            temp[6] = 25 + i;
            Assert.True(t.Add(new MachineSnapshot { Connected = true, Time = t0.AddSeconds(i), ChamberPressureTorr = 760.0 / (i + 1), TempC = temp }));
        }
        Assert.False(t.Add(new MachineSnapshot { Connected = false, Time = t0.AddSeconds(200) }));   // 沒連線不記

        var (time, d) = t.QueryMany(new[] { "pm_torr", "t07", "t01" }, t0, t0.AddSeconds(99));
        Assert.Equal(100, time.Length);
        Assert.Equal(760, d["pm_torr"][0]);
        Assert.Equal(124, d["t07"][99]);
        Assert.True(double.IsNaN(d["t01"][5]));

        var (ts, ds) = t.QueryMany(new[] { "t07" }, t0, t0.AddSeconds(99), maxPoints: 10);
        Assert.Equal(10, ts.Length);
        Assert.Equal(new double[] { 25, 35, 45, 55, 65, 75, 85, 95, 105, 115 }, ds["t07"]);
        Assert.Throws<ArgumentException>(() => t.QueryMany(new[] { "t07; DROP TABLE trend" }, t0, t0));

        var csv = _db + ".csv";
        try
        {
            Assert.Equal(100, t.ExportCsv(csv, t0, t0.AddSeconds(99)));
            var lines = File.ReadAllLines(csv);
            Assert.Equal(101, lines.Length);
            Assert.StartsWith("time,pm_torr,", lines[0].TrimStart('﻿'));
            Assert.StartsWith("2026-11-11 14:00:00.000,760,", lines[1]);
        }
        finally { File.Delete(csv); }
    }
}
