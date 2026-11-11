namespace Ald.Core;

/// <summary>燈塔 / 蜂鳴器輸出（同主控板 HR DO_CMD / IR DO_OUT 位元）。</summary>
[Flags]
public enum TowerLamp : ushort { None = 0, Buzzer = 1, Red = 2, Yellow = 4, Green = 8, Blue = 16 }

/// <summary>燈號規則的設定（config tower）。</summary>
public sealed record TowerConfig
{
    /// <summary>待機（沒有流程、沒有警報）亮黃燈；false = 待機全暗。</summary>
    public bool IdleYellow { get; init; } = true;
    /// <summary>未確認的警告（Warning）也叫蜂鳴器。</summary>
    public bool BuzzOnWarning { get; init; }
    /// <summary>蜂鳴器跟著閃爍節奏斷續叫；false = 連續叫。</summary>
    public bool BuzzerIntermittent { get; init; } = true;
    /// <summary>閃爍半週期（ms）。</summary>
    public int BlinkMs { get; init; } = 500;
}

/// <summary>決定燈號需要的狀態。</summary>
/// <param name="UnackedAlarm">有還沒按「確認」的異常（含已自己消失但沒人確認過的、流程異常停止）</param>
/// <param name="NeedOperator">需要人員處理：流程做完等取片、晶圓位置不明</param>
public readonly record struct TowerInputs(
    bool Safe, bool AnyAlarm, bool UnackedAlarm, bool AnyWarning, bool UnackedWarning, bool Running, bool NeedOperator);

/// <summary>
/// 燈號規則（4 色，依 SEMI S2 慣例）：
///   紅 = 異常 / 安全狀態（未確認閃爍）；黃 = 警告（未確認閃爍）或待機；綠 = 流程執行中；藍 = 需要人員處理（閃爍）。
///   蜂鳴器 = 有未確認的異常（設定可含警告），按「確認 / 消音」停止。
/// 主控板另外保底：安全狀態一定亮紅燈；IPC 斷線時只留紅燈。
/// </summary>
public static class Tower
{
    public static TowerLamp Compute(TowerInputs i, TowerConfig c, bool blinkOn)
    {
        var o = TowerLamp.None;
        bool red = i.Safe || i.AnyAlarm || i.UnackedAlarm;
        if (red && (!i.UnackedAlarm || blinkOn)) o |= TowerLamp.Red;
        if (i.AnyWarning || i.UnackedWarning) { if (!i.UnackedWarning || blinkOn) o |= TowerLamp.Yellow; }
        else if (c.IdleYellow && !i.Running && !red) o |= TowerLamp.Yellow;
        if (i.Running) o |= TowerLamp.Green;
        if (i.NeedOperator && blinkOn) o |= TowerLamp.Blue;
        bool buzz = i.UnackedAlarm || (c.BuzzOnWarning && i.UnackedWarning);
        if (buzz && (!c.BuzzerIntermittent || blinkOn)) o |= TowerLamp.Buzzer;
        return o;
    }

    /// <summary>閃爍相位（依時間，同一台各燈同步）。</summary>
    public static bool BlinkOn(DateTime t, TowerConfig c) => (long)(t.TimeOfDay.TotalMilliseconds / Math.Max(100, c.BlinkMs)) % 2 == 0;
}
