namespace Ald.Core;

/// <summary>使用者等級。未登入 = 檢視（只能看、按安全停止 / 中止）。</summary>
public enum UserRole { Viewer = 0, Operator = 1, Engineer = 2, Admin = 3 }

/// <summary>人機上需要權限的動作。</summary>
public enum Permission
{
    /// <summary>安全停止、中止流程：任何人都能按（停機永遠不鎖）。</summary>
    Stop,
    /// <summary>Reset（解除安全狀態）。</summary>
    Reset,
    /// <summary>執行配方（含總覽的抽真空 / 回填）。</summary>
    RunRecipe,
    /// <summary>Loadlock 抽氣 / 洩壓 / 開關 SV。</summary>
    Loadlock,
    /// <summary>傳片（送片 / 取片流程）。</summary>
    Transfer,
    /// <summary>手動修改晶圓位置記錄（確認實物後）。</summary>
    EditWaferMap,
    /// <summary>手動操作頁（各閥、加熱、MFC、馬達）。</summary>
    ManualControl,
    /// <summary>編輯 / 存檔配方。</summary>
    EditRecipe,
    /// <summary>使用者管理。</summary>
    ManageUsers,
    /// <summary>漏率測試、保養計數歸零 / 改提醒上限。</summary>
    Maintenance,
}

/// <summary>權限表：等級 ≥ 需求等級即可。改權限分配只改這裡。</summary>
public static class Access
{
    public static UserRole Required(Permission p) => p switch
    {
        Permission.Stop => UserRole.Viewer,
        Permission.Reset or Permission.RunRecipe or Permission.Loadlock or Permission.Transfer => UserRole.Operator,
        Permission.ManualControl or Permission.EditRecipe or Permission.EditWaferMap or Permission.Maintenance => UserRole.Engineer,
        Permission.ManageUsers => UserRole.Admin,
        _ => UserRole.Admin,
    };

    public static bool Can(UserRole role, Permission p) => role >= Required(p);

    public static string Text(UserRole r) => r switch
    {
        UserRole.Operator => "操作員",
        UserRole.Engineer => "工程師",
        UserRole.Admin => "管理員",
        _ => "檢視",
    };
}
