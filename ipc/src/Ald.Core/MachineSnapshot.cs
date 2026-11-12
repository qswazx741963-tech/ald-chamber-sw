namespace Ald.Core;

/// <summary>某一時刻整台機台的狀態（由通訊層每次輪詢更新）。Core 只依賴它，不直接碰網路。</summary>
public sealed record MachineSnapshot
{
    public DateTime Time { get; init; } = DateTime.Now;

    // 連線：index = 槽號（0 主控板、1 製程、2 加熱、3 真空、4 馬達）
    public bool Connected { get; init; }              // IPC ↔ ald-ecat ↔ 主控板（EtherCAT OP）
    public bool[] SlotOnline { get; init; } = new bool[5];
    /// <summary>主控板不在線時，從它讀到的 MAP_VERSION（0 = 沒讀到）。跟 IPC 的不同 = 兩邊程式版本不一致。</summary>
    public int MapVersionSeen { get; init; }
    /// <summary>各槽 IR STATUS 位元（READY、RUNNING、SAFE_STATE、WATCHDOG_TRIPPED、PARAM_REJECTED、LOCAL_INTERLOCK、ONLINE）。</summary>
    public ushort[] SlotStatus { get; init; } = new ushort[5];
    public bool SafeState { get; init; }
    public SystemState SysState { get; init; } = SystemState.Boot;
    public bool EmoOk { get; init; }
    public bool SafeLineOk { get; init; }
    public bool CdaOk { get; init; }
    /// <summary>主控板最後一次擋下命令的原因（IR IL_DENY + IL_DENY2，bit 定義見 registers.yaml）與動作。</summary>
    public uint LastDenyBits { get; init; }
    public ushort LastDenyAction { get; init; }
    public TowerLamp Tower { get; init; }                      // 燈塔 / 蜂鳴器實際輸出（主控板 IR DO_OUT）

    // 槽 3 真空板（PM）
    public bool ChamberLidClosed { get; init; }
    public bool LidCoverClosed { get; init; }
    public bool TurboAtSpeed { get; init; }
    public bool TurboGateClosed { get; init; }
    public bool ChamberVenting { get; init; }
    public double ChamberPressureTorr { get; init; } = double.NaN;   // 主控板 IR PM_PRES_MTORR（依壓力計型號換算），NaN = 未知
    public GaugeStatus ChamberGaugeStatus { get; init; } = GaugeStatus.Offline;
    // 以下由 IPC 依 config 型號換算，只做顯示 / 記錄（互鎖用上面主控板算的那個）
    public double ManometerTorr { get; init; } = double.NaN;
    public double IonGaugeTorr { get; init; } = double.NaN;
    public double ForelineTorr { get; init; } = double.NaN;
    public VacuumOutputs VacuumOutputs { get; init; }          // 真空板 DO 實際輸出
    public VacuumOutputs VacuumBlocked { get; init; }          // 有命令但被板內互鎖擋下
    public bool TurboGateOpened { get; init; }
    public bool DryPumpRunning { get; init; }
    public bool DryPumpAlarm { get; init; }
    public bool TurboAlarm { get; init; }

    // 槽 1 製程板
    public bool Rf1PowerCableOk { get; init; }
    public bool Rf1MatchCableOk { get; init; }
    public bool AldRunning { get; init; }
    public bool RfOn { get; init; }
    public ushort AldValves { get; init; }                     // ALD_V1~7 實際輸出（bit0 = V1）
    public ushort MfcUpstreamValves { get; init; }             // MFC_UP_V1~8（bit0 = V1）
    public double[] MfcPct { get; init; } = Filled(8, double.NaN);    // % 滿刻度
    public double[] MfcValue { get; init; } = Filled(8, double.NaN);  // 換成 config 單位（sccm / psi）
    public int AldCycle { get; init; }
    public int AldStep { get; init; }

    // 槽 2 加熱板
    public double[] TempC { get; init; } = Filled(16, double.NaN);     // NaN = 熱電偶斷線 / 讀不到
    public double[] HeaterDutyPct { get; init; } = new double[16];
    public ushort HeaterOutputs { get; init; }
    public HeaterOverTemp OverTemp { get; init; }

    // 槽 4 馬達板（PM）：軸 0 Heater module、Wafer lift 氣缸
    public bool HeaterDown { get; init; }
    public bool HeaterMoving { get; init; }
    public bool WaferLiftDown { get; init; }
    public bool WaferLiftUp { get; init; }
    public bool HeaterAtProcess { get; init; }
    public bool WaferPresent { get; init; }
    public int HeaterPosSteps { get; init; }
    public AxisState HeaterAxis { get; init; }

    /// <summary>
    /// 交接位置：Heater module 在下位且沒在動，Wafer lift 停在上 / 下任一定位。
    /// 送片：lift down → 手臂進來 → lift up 接片 → 手臂退出 → Heater up 到 Process。取片時 lift 在 up。
    /// </summary>
    public bool PmAtTransfer => HeaterDown && !HeaterMoving && WaferLiftDown != WaferLiftUp;

    // Loadlock（SLK 模組，經主控板）
    public bool LoadlockFresh { get; init; }          // TLM 在 IL_LL_STALE_MS 內有更新
    public int LoadlockAlarm { get; init; }
    public LoadlockSv SlitValve { get; init; } = LoadlockSv.Unknown;
    public LoadlockZ ArmZ { get; init; } = LoadlockZ.Unknown;
    public bool ArmMoving { get; init; }
    public int ArmPosSteps { get; init; }
    public int ArmSafePosSteps { get; init; } = 5894; // 160 mm × 36.84 step/mm
    public double LoadlockPressureTorr { get; init; } = double.NaN;   // NaN = 未知（低於下限時也是 NaN，看 UnderRange）
    public bool LoadlockPressureUnderRange { get; init; }   // SLK 讀「低於量測下限 1 Torr」
    public bool LoadlockPressureSuspect { get; init; }      // 主控板判斷疑似壓力計斷線（LL_FLAGS.PRES_SUSPECT）
    public bool LoadlockLidOpen { get; init; }
    public bool LoadlockRoughOpen { get; init; }
    public bool LoadlockVentOpen { get; init; }
    public bool LoadlockBusy { get; init; }           // SLK 序列執行中

    /// <summary>手臂確定已退出 SV 路徑（資料不新鮮 = 不確定 = 不安全）。</summary>
    public bool ArmClearOfSv => LoadlockFresh && ArmPosSteps <= ArmSafePosSteps && !ArmMoving;
    public bool SlitClosed => LoadlockFresh && SlitValve == LoadlockSv.Closed;

    public bool AllOnline => Connected && SlotOnline.All(x => x);

    private static double[] Filled(int n, double v) { var a = new double[n]; Array.Fill(a, v); return a; }
}

/// <summary>主控板 IR SYS_STATE。</summary>
public enum SystemState : ushort { Boot = 0, Safe = 1, Idle = 2, Process = 3, Transfer = 4, Alarm = 5 }

/// <summary>真空板 DO（順序 = registers.yaml vacuum valves）。</summary>
[Flags]
public enum VacuumOutputs : ushort
{
    None = 0, TurboGv = 1 << 0, TurboRough = 1 << 1, ChamberRough = 1 << 2, ChamberVent = 1 << 3,
    DryPumpOn = 1 << 4, TurboOn = 1 << 5,
}

/// <summary>加熱板過溫開關（硬體）。</summary>
[Flags]
public enum HeaterOverTemp : ushort { None = 0, Chamber = 1, Precursor = 2, Delivery = 4, Chuck = 8 }

/// <summary>馬達板 AXIS_STATE。</summary>
public enum AxisState : ushort { Idle = 0, Moving = 1, Homing = 2, Error = 3 }

/// <summary>與 SLK slit_valve.h 一致。</summary>
public enum LoadlockSv { Unknown = 0, Closed = 1, Opening = 2, Open = 3, Closing = 4, Error = 5 }

/// <summary>與 SLK motor.h 一致。</summary>
public enum LoadlockZ { Unknown = 0, Down = 1, Raising = 2, Up = 3, Lowering = 4 }
