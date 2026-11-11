using Ald.Core;
using Ald.Data;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Ald.Hmi;

/// <summary>
/// 登入與權限：未登入 = 檢視；權限表在 Ald.Core.Access。安全停止 / 中止流程永遠不鎖。
/// config hmi.idleLogoutMinutes 沒有操作自動登出。登入、登出、帳號異動都寫進事件記錄。
/// </summary>
public partial class MainWindow
{
    private readonly UserStore? _users;
    private string? _user;
    private UserRole _role = UserRole.Viewer;
    private DateTime _lastInput = DateTime.Now;
    private bool _defaultPw;

    private TimeSpan IdleLogout => TimeSpan.FromMinutes(_m?.Config.Hmi.IdleLogoutMinutes ?? 30);
    private bool Can(Permission p) => Access.Can(_role, p);
    /// <summary>選單的「登入 / 登出」。</summary>
    private Action LoginClicked = () => { };
    private string OpSource => $"操作:{_user ?? "未登入"}";

    private void WireAccess()
    {
        AddHandler(PointerPressedEvent, (_, _) => _lastInput = DateTime.Now, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, _) => _lastInput = DateTime.Now, RoutingStrategies.Tunnel, handledEventsToo: true);
        _defaultPw = _users?.DefaultPasswordInUse ?? false;

        var panel = C<Border>("PnlLogin");
        LoginClicked = () =>
        {
            if (_user is not null) { Logout("登出"); return; }
            C<TextBox>("TxtLoginName").Text = "";
            C<TextBox>("TxtLoginPw").Text = "";
            C<TextBlock>("TxtLoginErr").Text = _users is null ? "沒有帳號資料庫" : "";
            panel.IsVisible = true;
            C<TextBox>("TxtLoginName").Focus();
        };
        C<Button>("BtnLoginCancel").Click += (_, _) => panel.IsVisible = false;
        C<Button>("BtnLoginOk").Click += (_, _) =>
        {
            if (_users is null) return;
            string name = (C<TextBox>("TxtLoginName").Text ?? "").Trim();
            var role = _users.Login(name, C<TextBox>("TxtLoginPw").Text ?? "");
            if (role is null)
            {
                C<TextBlock>("TxtLoginErr").Text = "帳號或密碼錯誤";
                _alarmLog?.Add("登入", "Warning", $"登入失敗：{name}", evt: "OPERATOR");
                return;
            }
            _user = _users.List().FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase))?.Name ?? name;
            _role = role.Value;
            _lastInput = DateTime.Now;
            panel.IsVisible = false;
            _status = $"{_user}（{Access.Text(_role)}）已登入" + (_defaultPw ? "。admin 還是出廠預設密碼，請到「使用者」頁變更" : "");
            _alarmLog?.Add("登入", "Info", $"{_user}（{Access.Text(_role)}）登入", evt: "OPERATOR");
            RefreshUsers();
        };

        // 變更自己的密碼
        C<Button>("BtnMyPw").Click += (_, _) => UserOp("變更密碼", () =>
        {
            if (_user is null || _users is null) throw new InvalidOperationException("請先登入");
            if (_users.Login(_user, C<TextBox>("TxtMyOld").Text ?? "") is null) throw new ArgumentException("目前密碼不對");
            var a = C<TextBox>("TxtMyNew").Text ?? "";
            if (a != (C<TextBox>("TxtMyNew2").Text ?? "")) throw new ArgumentException("兩次輸入的新密碼不一樣");
            _users.SetPassword(_user, a);
            foreach (var n in new[] { "TxtMyOld", "TxtMyNew", "TxtMyNew2" }) C<TextBox>(n).Text = "";
            return $"{_user} 變更密碼";
        });

        // 使用者管理（管理員）
        var roles = new[] { UserRole.Operator, UserRole.Engineer, UserRole.Admin };
        var cmbRole = C<ComboBox>("CmbURole");
        cmbRole.ItemsSource = roles.Select(Access.Text).ToList();
        cmbRole.SelectedIndex = 0;
        UserRole SelRole() => roles[Math.Max(0, cmbRole.SelectedIndex)];
        string UName() => (C<TextBox>("TxtUName").Text ?? "").Trim();
        string UPw() => C<TextBox>("TxtUPw").Text ?? "";
        var lst = C<ListBox>("LstUsers");
        lst.SelectionChanged += (_, _) =>
        {
            if (lst.SelectedItem is not string row || _users is null) return;
            var u = _users.List().FirstOrDefault(x => row.StartsWith(x.Name + " "));
            if (u is null) return;
            C<TextBox>("TxtUName").Text = u.Name;
            cmbRole.SelectedIndex = Array.IndexOf(roles, u.Role);
        };
        C<Button>("BtnUAdd").Click += (_, _) => UserOp("新增帳號", () => { _users!.Add(UName(), SelRole(), UPw()); return $"新增帳號 {UName()}（{Access.Text(SelRole())}）"; });
        C<Button>("BtnURole").Click += (_, _) => UserOp("改等級", () => { _users!.SetRole(UName(), SelRole()); return $"{UName()} 改為 {Access.Text(SelRole())}"; });
        C<Button>("BtnUPw").Click += (_, _) => UserOp("重設密碼", () => { _users!.SetPassword(UName(), UPw()); return $"重設 {UName()} 的密碼"; });
        C<Button>("BtnUDel").Click += (_, _) => UserOp("刪除帳號", () =>
        {
            if (string.Equals(UName(), _user, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("不能刪除自己");
            _users!.Delete(UName());
            return $"刪除帳號 {UName()}";
        });
        RefreshUsers();
    }

    private void UserOp(string what, Func<string> act)
    {
        try
        {
            var msg = act();
            _status = msg;
            C<TextBox>("TxtUPw").Text = "";
            _defaultPw = _users?.DefaultPasswordInUse ?? false;
            _alarmLog?.Add(OpSource, "Info", msg, evt: "OPERATOR");
            RefreshUsers();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { _status = $"{what}失敗：{e.Message}"; }
    }

    private void RefreshUsers()
    {
        if (_users is null) return;
        C<ListBox>("LstUsers").ItemsSource = Can(Permission.ManageUsers)
            ? _users.List().Select(u => $"{u.Name} — {Access.Text(u.Role)}（建立 {u.Created:yyyy-MM-dd}）").ToList()
            : new List<string>();
    }

    private void Logout(string why)
    {
        _alarmLog?.Add("登入", "Info", $"{_user} {why}", evt: "OPERATOR");
        _status = $"{_user} {why}";
        _user = null;
        _role = UserRole.Viewer;
        RefreshUsers();
    }

    private void RenderAccess()
    {
        if (_user is not null && DateTime.Now - _lastInput > IdleLogout) Logout($"{IdleLogout.TotalMinutes:0} 分鐘沒有操作，自動登出");

        C<TextBlock>("TxtUser").Text = _user is null ? "未登入（檢視）" : $"{_user}（{Access.Text(_role)}）";
        C<Button>("BtnReset").IsEnabled = Can(Permission.Reset);
        C<Button>("BtnPump").IsEnabled = Can(Permission.RunRecipe) && !Busy;
        C<Button>("BtnVent").IsEnabled = Can(Permission.RunRecipe) && !Busy;
        foreach (var b in new[] { "BtnLlPump", "BtnLlVent", "BtnSvOpen", "BtnSvClose" }) C<Button>(b).IsEnabled = Can(Permission.Loadlock) && !XferRunning && _auto?.State != SequencerState.Running && !BatchRunning;
        C<StackPanel>("PnlUsers").IsEnabled = Can(Permission.ManageUsers);
        C<Button>("BtnMyPw").IsEnabled = _user is not null;
        C<TextBlock>("TxtMe").Text = _user is null ? "請先登入" : $"目前登入：{_user}（{Access.Text(_role)}）"
            + (_defaultPw ? "\n⚠ admin 還是出廠預設密碼，請盡快變更" : "");
    }
}
