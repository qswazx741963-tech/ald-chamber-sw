namespace Ald.Core;

/// <summary>某一時刻整台機台的狀態（由通訊層每次輪詢更新）。Core 只依賴它，不直接碰網路。</summary>
public sealed record MachineSnapshot
{
    public DateTime Time { get; init; } = DateTime.Now;

    // 連線：index = 槽號（0 主控板、1 製程、2 加熱、3 真空、4 馬達）
    public bool[] SlotOnline { get; init; } = new bool[5];
    public bool SafeState { get; init; }

    // 槽 3 真空板（PM）
    public bool ChamberLidClosed { get; init; }
    public bool LidCoverClosed { get; init; }
    public bool TurboAtSpeed { get; init; }
    public bool TurboGateClosed { get; init; }
    public bool ChamberVenting { get; init; }
    public double ChamberPressureTorr { get; init; } = double.NaN;   // 由 config 公式換算

    // 槽 1 製程板
    public bool Rf1PowerCableOk { get; init; }
    public bool Rf1MatchCableOk { get; init; }
    public bool AldRunning { get; init; }
    public bool RfOn { get; init; }

    // 槽 4 馬達板（PM）
    public bool WaferLiftHome { get; init; }
    public bool PmAtTransfer { get; init; }   // TODO 交接位置定義待確認

    // Loadlock（SLK 模組，經主控板）
    public bool LoadlockFresh { get; init; }          // TLM 在 IL_LL_STALE_MS 內有更新
    public int LoadlockAlarm { get; init; }
    public LoadlockSv SlitValve { get; init; } = LoadlockSv.Unknown;
    public LoadlockZ ArmZ { get; init; } = LoadlockZ.Unknown;
    public bool ArmMoving { get; init; }
    public int ArmPosSteps { get; init; }
    public int ArmSafePosSteps { get; init; } = 5894; // 160 mm × 36.84 step/mm
    public double LoadlockPressureTorr { get; init; } = double.NaN;   // -0.001 = 低於 1 Torr 量測下限
    public bool LoadlockLidOpen { get; init; }
    public bool LoadlockRoughOpen { get; init; }
    public bool LoadlockVentOpen { get; init; }
    public bool LoadlockBusy { get; init; }           // SLK 序列執行中

    /// <summary>手臂確定已退出 SV 路徑（資料不新鮮 = 不確定 = 不安全）。</summary>
    public bool ArmClearOfSv => LoadlockFresh && ArmPosSteps <= ArmSafePosSteps && !ArmMoving;
    public bool SlitClosed => LoadlockFresh && SlitValve == LoadlockSv.Closed;
}

/// <summary>與 SLK slit_valve.h 一致。</summary>
public enum LoadlockSv { Unknown = 0, Closed = 1, Opening = 2, Open = 3, Closing = 4, Error = 5 }

/// <summary>與 SLK motor.h 一致。</summary>
public enum LoadlockZ { Unknown = 0, Down = 1, Raising = 2, Up = 3, Lowering = 4 }
