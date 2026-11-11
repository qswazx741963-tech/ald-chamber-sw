using Ald.Comm.Generated;
using Ald.Core;

namespace Ald.Comm;

/// <summary>
/// 主控板那張表（各槽 IR）→ MachineSnapshot。純函式、不碰網路，方便單元測試。
/// 互鎖用的 PM 壓力以主控板換算的 PM_PRES_MTORR 為準；其他壓力計在這裡依 config 型號換算，只做顯示 / 記錄。
/// </summary>
public static class SnapshotBuilder
{
    private const int Unknown32 = int.MaxValue;   // 主控板「壓力未知」

    /// <param name="ir">依槽號取該槽 IR（區內位址 0 起算）；離線回 null。</param>
    public static MachineSnapshot Build(bool connected, Func<int, ushort[]?> ir, MachineConfig cfg, bool llCmdPending = false)
    {
        var online = new bool[5];
        var status = new ushort[5];
        for (int i = 0; i < 5; i++)
        {
            var img = ir(i);
            online[i] = connected && img is not null;
            if (img is not null && img.Length > 3) status[i] = img[3];
        }
        var s = new MachineSnapshot { Time = DateTime.Now, Connected = connected, SlotOnline = online, SlotStatus = status };
        if (!connected || ir(0) is not { } m) return s with { SafeState = true };

        s = FillMain(s, m);
        if (online[3] && ir(3) is { } vac) s = FillVacuum(s, vac, cfg);
        if (online[1] && ir(1) is { } proc) s = FillProcess(s, proc, cfg);
        if (online[2] && ir(2) is { } heat) s = FillHeater(s, heat);
        if (online[4] && ir(4) is { } mot) s = FillMotion(s, mot);
        return FillLoadlock(s, m, cfg.Interlock.ArmSafePosSteps, llCmdPending);
    }

    private static ushort L(ushort absAddr, int slot) => (ushort)(absAddr - slot * RegisterMap.SlotStride);
    private static int I32(ushort[] r, int i) => (r[i] << 16) | r[i + 1];

    private static MachineSnapshot FillMain(MachineSnapshot s, ushort[] m)
    {
        var di = (MainBoard.Input.DiBits)m[MainBoard.Input.Di];
        var sys = (SystemState)m[MainBoard.Input.SysState];
        int pm = I32(m, MainBoard.Input.PmPresMtorr);
        var gst = (GaugeStatus)m[MainBoard.Input.PmGaugeStatus];
        return s with
        {
            SysState = sys,
            SafeState = sys == SystemState.Safe || sys == SystemState.Boot,
            EmoOk = di.HasFlag(MainBoard.Input.DiBits.EmoOk),
            SafeLineOk = di.HasFlag(MainBoard.Input.DiBits.SafeLineOk),
            CdaOk = di.HasFlag(MainBoard.Input.DiBits.CdaOk),
            LastDenyBits = m[MainBoard.Input.IlDeny] | ((uint)m[MainBoard.Input.IlDeny2] << 16),
            LastDenyAction = m[MainBoard.Input.IlDenyAction],
            Tower = (TowerLamp)m[MainBoard.Input.DoOut],
            ChamberPressureTorr = pm == Unknown32 ? double.NaN : pm / 1000.0,
            ChamberGaugeStatus = gst,
            RfOn = false,   // TODO 鎧泰 RF 協議確認後改讀 RF 實際狀態
        };
    }

    private static double ConvertGauge(GaugeConfig g, ushort mv)
    {
        if (g.Model == GaugeModel.None) return double.NaN;
        var (st, torr) = Gauges.Convert(g.Model, mv, g.FullScaleTorr);
        return st is GaugeStatus.Ok or GaugeStatus.UnderRange or GaugeStatus.OverRange ? torr : double.NaN;
    }

    private static ushort VacAi(ushort[] v, int? i) => i is >= 0 and < 8 ? v[L(VacuumBoard.Input.AiRaw, 3) + i.Value] : (ushort)0;

    private static MachineSnapshot FillVacuum(MachineSnapshot s, ushort[] v, MachineConfig cfg)
    {
        var di = (VacuumBoard.Input.DiBits)v[L(VacuumBoard.Input.Di, 3)];
        var fb = (VacuumOutputs)v[L(VacuumBoard.Input.DoFb, 3)];
        var g = cfg.Gauges;
        return s with
        {
            ChamberLidClosed = di.HasFlag(VacuumBoard.Input.DiBits.ChamberLidClosed),
            LidCoverClosed = di.HasFlag(VacuumBoard.Input.DiBits.LidCoverClosed),
            TurboAtSpeed = di.HasFlag(VacuumBoard.Input.DiBits.TurboAtSpeed),
            TurboGateClosed = di.HasFlag(VacuumBoard.Input.DiBits.TurboGvClosed),
            TurboGateOpened = di.HasFlag(VacuumBoard.Input.DiBits.TurboGvOpened),
            DryPumpRunning = di.HasFlag(VacuumBoard.Input.DiBits.DrypumpRunning),
            DryPumpAlarm = di.HasFlag(VacuumBoard.Input.DiBits.DrypumpAlarm),
            TurboAlarm = di.HasFlag(VacuumBoard.Input.DiBits.TurboAlarm),
            VacuumOutputs = fb,
            VacuumBlocked = (VacuumOutputs)v[L(VacuumBoard.Input.DoBlocked, 3)],
            ChamberVenting = fb.HasFlag(VacuumOutputs.ChamberVent),
            IonGaugeTorr = ConvertGauge(g.ChamberIonGauge, VacAi(v, g.ChamberIonGauge.AiIndex)),
            ForelineTorr = ConvertGauge(g.ForelineConvectron, VacAi(v, g.ForelineConvectron.AiIndex)),
        };
    }

    private static MachineSnapshot FillProcess(MachineSnapshot s, ushort[] p, MachineConfig cfg)
    {
        var di = (ProcessBoard.Input.DiBits)p[L(ProcessBoard.Input.Di, 1)];
        ushort fb = p[L(ProcessBoard.Input.DoFb, 1)];
        var pct = new double[8];
        var val = new double[8];
        for (int i = 0; i < 8; i++)
        {
            pct[i] = p[L(ProcessBoard.Input.MfcPv, 1) + i] * ProcessBoard.Input.MfcPvScale;
            double fs = i < cfg.Mfc.Channels.Count ? cfg.Mfc.Channels[i].FullScale : 0;
            val[i] = pct[i] / 100.0 * fs;
        }
        bool running = (s.SlotStatus[1] & (ushort)ProcessBoard.Input.StatusBits.Running) != 0;
        return s with
        {
            Rf1PowerCableOk = di.HasFlag(ProcessBoard.Input.DiBits.Rf1PowerCableOk),
            Rf1MatchCableOk = di.HasFlag(ProcessBoard.Input.DiBits.Rf1MatchCableOk),
            AldRunning = running,
            AldValves = (ushort)(fb & 0x7F),
            MfcUpstreamValves = (ushort)(fb >> 7),
            MfcPct = pct,
            MfcValue = val,
            AldCycle = p[L(ProcessBoard.Input.AldCycleNow, 1)],
            AldStep = p[L(ProcessBoard.Input.AldStepNow, 1)],
            ManometerTorr = ConvertGauge(cfg.Gauges.ChamberManometer, p[L(ProcessBoard.Input.ManometerRaw, 1)]),
        };
    }

    private static MachineSnapshot FillHeater(MachineSnapshot s, ushort[] h)
    {
        var t = new double[16];
        var duty = new double[16];
        for (int i = 0; i < 16; i++)
        {
            ushort raw = h[L(HeaterBoard.Input.TcTemp, 2) + i];
            t[i] = raw == 0x8000 ? double.NaN : (short)raw * HeaterBoard.Input.TcTempScale;
            duty[i] = h[L(HeaterBoard.Input.HeaterDuty, 2) + i] * HeaterBoard.Input.HeaterDutyScale;
        }
        return s with
        {
            TempC = t,
            HeaterDutyPct = duty,
            HeaterOutputs = h[L(HeaterBoard.Input.DoFb, 2)],
            OverTemp = (HeaterOverTemp)(h[L(HeaterBoard.Input.Di, 2)] & 0x0F),
        };
    }

    private static MachineSnapshot FillMotion(MachineSnapshot s, ushort[] m)
    {
        var di = (MotionBoard.Input.DiBits)m[L(MotionBoard.Input.Di, 4)];
        var axis = (AxisState)m[L(MotionBoard.Input.AxisState, 4)];
        return s with
        {
            HeaterDown = di.HasFlag(MotionBoard.Input.DiBits.HeaterDown),
            HeaterAtProcess = di.HasFlag(MotionBoard.Input.DiBits.HeaterProcess),
            WaferLiftDown = di.HasFlag(MotionBoard.Input.DiBits.WaferLiftDown),
            WaferLiftUp = di.HasFlag(MotionBoard.Input.DiBits.WaferLiftUp),
            WaferPresent = di.HasFlag(MotionBoard.Input.DiBits.WaferPresent),
            HeaterAxis = axis,
            HeaterMoving = axis is AxisState.Moving or AxisState.Homing,
            HeaterPosSteps = I32(m, L(MotionBoard.Input.AxisPos, 4)),
        };
    }

    /// <summary>主控板回報的 Loadlock 狀態（IR LL_*）。TLM 不新鮮 = 狀態不明。</summary>
    public static MachineSnapshot FillLoadlock(MachineSnapshot s, ushort[] m, int armSafePos, bool cmdPending)
    {
        var link = (MainBoard.Input.LlLinkBits)m[MainBoard.Input.LlLink];
        if (!link.HasFlag(MainBoard.Input.LlLinkBits.TlmFresh)) return s with { LoadlockFresh = false, ArmSafePosSteps = armSafePos };
        var flags = (MainBoard.Input.LlFlagsBits)m[MainBoard.Input.LlFlags];
        int pres = I32(m, MainBoard.Input.LlPres);
        ushort arm = m[MainBoard.Input.LlArm];
        return s with
        {
            LoadlockFresh = true,
            LoadlockAlarm = m[MainBoard.Input.LlAlm],
            SlitValve = (LoadlockSv)m[MainBoard.Input.LlSv],
            ArmZ = (LoadlockZ)m[MainBoard.Input.LlZ],
            ArmMoving = arm is 1 or 3,             // HOMING / MOVING
            ArmPosSteps = I32(m, MainBoard.Input.LlPos),
            ArmSafePosSteps = armSafePos,
            LoadlockPressureTorr = pres < 0 ? double.NaN : pres / 1000.0,   // -1 = 低於 SLK 量測下限
            LoadlockPressureUnderRange = pres < 0,
            LoadlockPressureSuspect = flags.HasFlag(MainBoard.Input.LlFlagsBits.PresSuspect),
            LoadlockLidOpen = flags.HasFlag(MainBoard.Input.LlFlagsBits.LidOpen),
            LoadlockRoughOpen = flags.HasFlag(MainBoard.Input.LlFlagsBits.RoughOpen),
            LoadlockVentOpen = flags.HasFlag(MainBoard.Input.LlFlagsBits.VentOpen),
            LoadlockBusy = m[MainBoard.Input.LlSeq] != 0 || cmdPending,
        };
    }
}
