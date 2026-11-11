using System.Text.Json;

namespace Ald.Core;

/// <summary>
/// 晶圓位置（軟體追蹤）：Loadlock（手臂上）、PM（Wafer lift / Heater 上）各有沒有片。
/// 目前沒有晶圓感測器（WAFER_PRESENT 待接），所以靠傳片流程更新；重開機從檔案讀回。
/// null = 不明（剛裝機、流程中途失敗）→ 傳片流程拒絕執行，要工程師在畫面確認後設定。
/// 寫檔先寫暫存檔再取代，斷電不會壞。
/// </summary>
public sealed class WaferMap
{
    private readonly string? _path;
    private readonly object _lock = new();

    public bool? Loadlock { get; private set; }
    public bool? Pm { get; private set; }
    public DateTime Updated { get; private set; }
    public string Note { get; private set; } = "";
    /// <summary>晶圓編號（有片時才有意義；送片 / 取片時跟著片移動，變無片就清掉）。</summary>
    public string LoadlockId { get; private set; } = "";
    public string PmId { get; private set; } = "";
    public event Action<WaferMap>? Changed;

    public WaferMap(string? path = null) { _path = path; }

    private sealed record FileData(bool? Loadlock, bool? Pm, DateTime Updated, string Note, string? LoadlockId = null, string? PmId = null);
    private static readonly JsonSerializerOptions Opt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>從檔案讀；沒有檔案或讀不懂 = 全部不明。</summary>
    public static WaferMap Load(string path)
    {
        var m = new WaferMap(path);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<FileData>(File.ReadAllText(path), Opt) is { } d)
                (m.Loadlock, m.Pm, m.Updated, m.Note, m.LoadlockId, m.PmId) = (d.Loadlock, d.Pm, d.Updated, d.Note, d.LoadlockId ?? "", d.PmId ?? "");
        }
        catch (Exception e) when (e is JsonException or IOException) { m.Note = "晶圓位置檔讀取失敗，請重新確認"; }
        return m;
    }

    /// <param name="loadlockId">放進 Loadlock 的晶圓編號（null = 不改）</param>
    public void Set(bool? loadlock, bool? pm, string note, string? loadlockId = null)
    {
        lock (_lock)
        {
            string llId = LoadlockId, pmId = PmId;
            // 編號跟著片走：手臂 → PM（送片）、PM → 手臂（取片）
            if (Loadlock == true && loadlock == false && pm == true && Pm != true) (pmId, llId) = (llId, "");
            else if (Pm == true && pm == false && loadlock == true && Loadlock != true) (llId, pmId) = (pmId, "");
            if (loadlockId is not null) llId = loadlockId.Trim();
            if (loadlock != true) llId = "";
            if (pm != true) pmId = "";
            (Loadlock, Pm, Updated, Note, LoadlockId, PmId) = (loadlock, pm, DateTime.Now, note, llId, pmId);
            Save();
        }
        Changed?.Invoke(this);
    }

    /// <summary>只改編號（片已經在那裡，例如人員在 Recipe 頁填了晶圓編號）。null = 不改；該位置無片就不記。</summary>
    public void SetIds(string? loadlockId, string? pmId)
    {
        lock (_lock)
        {
            if (loadlockId is not null && Loadlock == true) LoadlockId = loadlockId.Trim();
            if (pmId is not null && Pm == true) PmId = pmId.Trim();
            Save();
        }
        Changed?.Invoke(this);
    }

    private void Save()
    {
        if (_path is null) return;
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(new FileData(Loadlock, Pm, Updated, Note, LoadlockId, PmId), Opt));
        File.Move(_path + ".tmp", _path, overwrite: true);
    }

    public static string Text(bool? v) => v switch { true => "有片", false => "無片", null => "不明" };
}
