using Ald.Core;

namespace Ald.Comm;

/// <summary>SLK TLM 一筆（欄位與 SLK PROTOCOL_v2.md 2.1 相同）。</summary>
public sealed record SlkTelemetry
{
    public int Chm { get; init; }
    public int Arm { get; init; }
    public int Sv { get; init; }
    public int Z { get; init; }
    public int Waf { get; init; }
    public int Wpr { get; init; }
    public int Seq { get; init; }
    public bool RoughOpen { get; init; }
    public bool VentOpen { get; init; }
    public bool Bypass { get; init; }
    public int PresMTorr { get; init; } = 760000;   // -1 = 低於 1 Torr 量測下限
    public bool LidOpen { get; init; }
    public int Alm { get; init; }
    public int Pos { get; init; }
    public int Spd { get; init; }
}

/// <summary>
/// Loadlock（SLK 模組）ASCII 協議：一行一個，主機送 ">SEQ:LOAD\n"，SLK 回 "TLM:..." / "EVT:..."。
/// 來源：SLK_Firmware_ManualMode/PROTOCOL_v2.md。SLK 韌體不改。
/// </summary>
public static class SlkProtocol
{
    public static string? Build(LoadlockCommand cmd, int arg = 0) => cmd switch
    {
        LoadlockCommand.Status => ">SYS:ST",
        LoadlockCommand.EStop => ">SYS:ES",
        LoadlockCommand.Reset => ">SYS:RST",
        LoadlockCommand.Recover => ">SYS:RCV",
        LoadlockCommand.Pump => ">SEQ:PUMP",
        LoadlockCommand.Vent => ">SEQ:VENT",
        LoadlockCommand.Load => ">SEQ:LOAD",
        LoadlockCommand.Unload => ">SEQ:UNLOAD",
        LoadlockCommand.Abort => ">SEQ:ABORT",
        LoadlockCommand.XferDone => ">SEQ:XFER",
        LoadlockCommand.SvOpen => ">VLV:SV:O",
        LoadlockCommand.SvClose => ">VLV:SV:C",
        LoadlockCommand.RoughOn => ">VLV:ROUGH:1",
        LoadlockCommand.RoughOff => ">VLV:ROUGH:0",
        LoadlockCommand.VentOn => ">VLV:VENT:1",
        LoadlockCommand.VentOff => ">VLV:VENT:0",
        LoadlockCommand.ArmHome => ">ARM:HOME",
        LoadlockCommand.ArmAutoHome => ">ARM:AHOME",
        LoadlockCommand.ArmToLoadlock => ">ARM:LL",
        LoadlockCommand.ArmToPm => ">ARM:PM",
        LoadlockCommand.ArmGoto when arg >= 0 => $">ARM:GOTO:{arg}",
        LoadlockCommand.ZUp => ">Z:UP",
        LoadlockCommand.ZDown => ">Z:DN",
        _ => null,
    };

    /// <summary>互鎖會用到的欄位，缺任何一個就整筆不採用（不能用預設值或舊值頂替）。</summary>
    private static readonly string[] Required = { "ARM", "SV", "Z", "SEQ", "RGH", "VNT", "PRES", "LID", "ALM", "POS" };

    /// <summary>解析 "TLM:CHM=0,ARM=0,..."。格式錯或缺少互鎖必要欄位回傳 null（破損資料不採用）。</summary>
    public static SlkTelemetry? ParseTlm(string line)
    {
        if (!line.StartsWith("TLM:", StringComparison.Ordinal)) return null;
        var kv = new Dictionary<string, int>();
        foreach (var part in line[4..].Split(','))
        {
            var i = part.IndexOf('=');
            if (i <= 0 || !int.TryParse(part[(i + 1)..], out var v)) return null;
            kv[part[..i]] = v;
        }
        if (Required.Any(k => !kv.ContainsKey(k))) return null;
        int G(string k, int d = 0) => kv.TryGetValue(k, out var v) ? v : d;
        return new SlkTelemetry
        {
            Chm = G("CHM"), Arm = G("ARM"), Sv = G("SV"), Z = G("Z"), Waf = G("WAF"), Wpr = G("WPR"),
            Seq = G("SEQ"), RoughOpen = G("RGH") != 0, VentOpen = G("VNT") != 0, Bypass = G("BYP") != 0,
            PresMTorr = G("PRES", 760000), LidOpen = G("LID") != 0, Alm = G("ALM"), Pos = G("POS"), Spd = G("SPD"),
        };
    }

    /// <summary>這個事件是不是代表 cmd 完成（true）/ 失敗（false）/ 無關（null）。</summary>
    public static bool? Completes(LoadlockCommand cmd, string evt)
    {
        if (cmd == LoadlockCommand.EStop) return evt.StartsWith("EVT:SYS:ESTOP") ? true : null;
        string[] fail = { "EVT:SYS:ESTOP", "EVT:SEQ:ERR", "EVT:SEQ:IL_FAIL", "EVT:ARM:STALL", "EVT:DIAG:ALM",
                          "EVT:SYS:ONLINE_REJECT", "EVT:SYS:RCV_REJECT" };
        if (fail.Any(f => evt.StartsWith(f, StringComparison.Ordinal))) return false;
        string? done = cmd switch
        {
            LoadlockCommand.SvOpen => "EVT:VLV:SV_OPEN",
            LoadlockCommand.SvClose or LoadlockCommand.XferDone => "EVT:VLV:SV_CLOSE",
            LoadlockCommand.Pump => "EVT:SEQ:PUMP_DONE",
            LoadlockCommand.Vent => "EVT:SEQ:VENT_DONE",
            LoadlockCommand.Load or LoadlockCommand.Unload => "EVT:SEQ:DONE",
            LoadlockCommand.ArmHome or LoadlockCommand.ArmAutoHome or LoadlockCommand.Recover => "EVT:ARM:HOMED",
            LoadlockCommand.ArmToLoadlock or LoadlockCommand.ArmToPm or LoadlockCommand.ArmGoto => "EVT:ARM:AT_POS",
            LoadlockCommand.ZUp => "EVT:ARM:Z_UP_DONE",
            LoadlockCommand.ZDown => "EVT:ARM:Z_DOWN_DONE",
            LoadlockCommand.Reset => "EVT:SYS:RESET_DONE",
            _ => null,
        };
        return done is not null && evt.StartsWith(done, StringComparison.Ordinal) ? true : null;
    }
}
