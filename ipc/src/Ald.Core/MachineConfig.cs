using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ald.Core;

/// <summary>
/// config/machine.json（換機台只改這個檔）。以 "_" 開頭的鍵是說明，讀取時忽略。
/// 啟動 / 每次重新連上主控板時，MachineService 會把互鎖、壓力計、加熱上限寫進主控板。
/// </summary>
public sealed record MachineConfig
{
    public MainBoardConfig MainBoard { get; init; } = new();
    public List<SlotConfig> Slots { get; init; } = new();
    public LoadlockConfig Loadlock { get; init; } = new();
    public int WatchdogMs { get; init; } = 1000;
    public InterlockConfig Interlock { get; init; } = new();
    public GaugesConfig Gauges { get; init; } = new();
    public MfcConfig Mfc { get; init; } = new();
    public HeatersConfig Heaters { get; init; } = new();
    public DataLogConfig DataLog { get; init; } = new();
    public HmiConfig Hmi { get; init; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static MachineConfig Load(string path) => Parse(File.ReadAllText(path));

    public static MachineConfig Parse(string json)
    {
        var cfg = JsonSerializer.Deserialize<MachineConfig>(json, Options) ?? throw new InvalidDataException("machine.json 是空的");
        var errors = cfg.Validate().ToList();
        if (errors.Count > 0) throw new InvalidDataException("machine.json 有錯誤：\n  " + string.Join("\n  ", errors));
        return cfg;
    }

    public IEnumerable<string> Validate()
    {
        if (WatchdogMs is < 200 or > 10000) yield return "watchdogMs 需在 200–10000";
        if (Mfc.Channels.Count != 8) yield return "mfc.channels 需 8 組（通道 1~8）";
        foreach (var (c, i) in Mfc.Channels.Select((c, i) => (c, i)))
            if (!(c.FullScale > 0)) yield return $"mfc.channels[{i}].fullScale 需 > 0";
        if (Heaters.Channels.Count != 16) yield return "heaters.channels 需 16 組";
        if (Hmi.DefaultAdminPasswordHash.Length > 0 && Hmi.DefaultAdminPasswordHash.Split('$') is not ["pbkdf2-sha256", _, _, _])
            yield return "hmi.defaultAdminPasswordHash 格式需為 pbkdf2-sha256$次數$salt$hash（用 --hash-password 產生）";
        if (Hmi.IdleLogoutMinutes is < 1 or > 1440) yield return "hmi.idleLogoutMinutes 需在 1–1440";
        foreach (var (h, i) in Heaters.Channels.Select((h, i) => (h, i)))
            if (h.LimitDegC is < 0 or > 650) yield return $"heaters.channels[{i}].limitDegC 需在 0–650";
        if (Interlock.SvBelowTorr is <= 0 or > 10) yield return "interlock.svBelowTorr 需在 0–10";
        if (Gauges.Find(Gauges.PmInterlockGauge) is null) yield return $"gauges.pmInterlockGauge 指到不存在的壓力計 {Gauges.PmInterlockGauge}";
    }

    /// <summary>互鎖預檢用的參數（同主控板 HR IL_*）。</summary>
    public InterlockSettings ToInterlockSettings() => new()
    {
        SvMode = Interlock.SvOpenMode,
        SvBelowTorr = Interlock.SvBelowTorr,
        SvMaxDiffTorr = Interlock.SvMaxDiffMtorr / 1000.0,
        LoadlockZEnabled = Interlock.LoadlockZEnabled,
    };
}

public sealed record MainBoardConfig
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 502;
    public byte UnitId { get; init; } = 1;
    public int PollMs { get; init; } = 50;
}

public sealed record SlotConfig
{
    public int Slot { get; init; }
    public string Type { get; init; } = "";
}

public sealed record LoadlockConfig
{
    public int CommandTimeoutMs { get; init; } = 75000;
}

public sealed record InterlockConfig
{
    public SvOpenMode SvOpenMode { get; init; } = SvOpenMode.Below;
    public double SvBelowTorr { get; init; } = 1.0;
    public int SvMaxDiffMtorr { get; init; } = 50;
    public double LoadlockJumpTorr { get; init; } = 10;
    public bool LoadlockZEnabled { get; init; } = true;
    public int LoadlockStaleMs { get; init; } = 500;
    public int ArmSafePosSteps { get; init; } = 5894;
}

public sealed record GaugeConfig
{
    public GaugeModel Model { get; init; } = GaugeModel.None;
    public double FullScaleTorr { get; init; }
    public int Slot { get; init; }
    /// <summary>真空板 AI_RAW 第幾路（slot 3）。</summary>
    public int? AiIndex { get; init; }
    /// <summary>或直接指定暫存器名稱（例如製程板 MANOMETER_RAW）。</summary>
    public string? Register { get; init; }
}

public sealed record GaugesConfig
{
    public string PmInterlockGauge { get; init; } = "chamberConvectron";
    public GaugeConfig ChamberManometer { get; init; } = new();
    public GaugeConfig ChamberConvectron { get; init; } = new();
    public GaugeConfig ChamberIonGauge { get; init; } = new();
    public GaugeConfig ForelineConvectron { get; init; } = new();
    public GaugeConfig LoadlockGauge { get; init; } = new();

    public GaugeConfig? Find(string name) => name.ToLowerInvariant() switch
    {
        "chambermanometer" => ChamberManometer,
        "chamberconvectron" => ChamberConvectron,
        "chamberiongauge" => ChamberIonGauge,
        "forelineconvectron" => ForelineConvectron,
        "loadlockgauge" => LoadlockGauge,
        _ => null,
    };
}

public sealed record MfcChannelConfig
{
    public string Name { get; init; } = "";
    public string Use { get; init; } = "";
    public double FullScale { get; init; }
    public string Unit { get; init; } = "sccm";
}

public sealed record MfcConfig
{
    public List<MfcChannelConfig> Channels { get; init; } = new();
}

public sealed record HeaterChannelConfig
{
    public string Name { get; init; } = "";
    public double LimitDegC { get; init; }
}

public sealed record HeatersConfig
{
    public List<HeaterChannelConfig> Channels { get; init; } = new();
}

/// <summary>人機設定。</summary>
public sealed record HmiConfig
{
    /// <summary>
    /// 第一次啟動（帳號資料庫沒有任何帳號）時建立的 admin 出廠密碼，只存雜湊：
    /// "pbkdf2-sha256$次數$salt(base64)$hash(base64)"，用 <c>AldChamber --hash-password 密碼</c> 產生。空白 = 密碼 admin。
    /// </summary>
    public string DefaultAdminPasswordHash { get; init; } = "";
    /// <summary>幾分鐘沒有操作自動登出。</summary>
    public int IdleLogoutMinutes { get; init; } = 30;
}

public sealed record DataLogConfig
{
    public string Path { get; init; } = "data/ald.db";
    public int IntervalMs { get; init; } = 1000;
    public int KeepDays { get; init; } = 90;
}
