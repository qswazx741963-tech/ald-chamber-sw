using Ald.Comm.Generated;
using Ald.Core;
using LlCmd = Ald.Comm.Generated.MainBoard.Holding.LlCmdValues;
using LlResult = Ald.Comm.Generated.MainBoard.Input.LlCmdResultValues;

namespace Ald.Comm;

/// <summary>
/// Loadlock 命令都經主控板送出：IPC 寫 HR LL_CMD / LL_CMD_ARG / LL_CMD_SEQ，
/// 主控板（firmware/main_board/app/ll_gateway.c）跑 LL-01~08 互鎖後經 RS-485 送給 SLK，
/// 結果回在 IR LL_CMD_ACK（= 處理完的 SEQ）與 LL_CMD_RESULT。SLK 韌體維持全開放、不做互鎖。
/// 這裡先用 Interlocks.Check 預檢，只是為了在畫面上立刻顯示被擋的原因；真正的卡控在主控板。
/// </summary>
public sealed class LoadlockService
{
    private readonly MainBoardLink _main;
    private readonly Func<MachineSnapshot> _snapshot;
    private ushort _seq;
    private bool _seqSynced;

    public int ArmSafePosSteps { get; set; } = 5894;

    public LoadlockService(MainBoardLink main, Func<MachineSnapshot> snapshot)
    {
        _main = main;
        _snapshot = snapshot;
    }

    private SlotImage? Main => _main.Slots.FirstOrDefault(s => s.Slot == 0);

    /// <summary>IPC 命令 → 主控板 LL_CMD 值。</summary>
    public static LlCmd ToRegister(LoadlockCommand cmd) => cmd switch
    {
        LoadlockCommand.Status => LlCmd.Status,
        LoadlockCommand.EStop => LlCmd.Estop,
        LoadlockCommand.Reset => LlCmd.Reset,
        LoadlockCommand.Recover => LlCmd.Recover,
        LoadlockCommand.Pump => LlCmd.Pump,
        LoadlockCommand.Vent => LlCmd.Vent,
        LoadlockCommand.Load => LlCmd.Load,
        LoadlockCommand.Unload => LlCmd.Unload,
        LoadlockCommand.Abort => LlCmd.Abort,
        LoadlockCommand.XferDone => LlCmd.XferDone,
        LoadlockCommand.SvOpen => LlCmd.SvOpen,
        LoadlockCommand.SvClose => LlCmd.SvClose,
        LoadlockCommand.RoughOn => LlCmd.RoughOn,
        LoadlockCommand.RoughOff => LlCmd.RoughOff,
        LoadlockCommand.VentOn => LlCmd.VentOn,
        LoadlockCommand.VentOff => LlCmd.VentOff,
        LoadlockCommand.ArmHome => LlCmd.ArmHome,
        LoadlockCommand.ArmAutoHome => LlCmd.ArmAhome,
        LoadlockCommand.ArmToLoadlock => LlCmd.ArmLl,
        LoadlockCommand.ArmToPm => LlCmd.ArmPm,
        LoadlockCommand.ArmGoto => LlCmd.ArmGoto,
        LoadlockCommand.ZUp => LlCmd.ZUp,
        LoadlockCommand.ZDown => LlCmd.ZDown,
        _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
    };

    /// <summary>
    /// 預檢互鎖後把命令寫給主控板。被擋下回傳 Allowed = false 與原因（給畫面顯示）。
    /// 主控板仍可能拒絕（例如狀態剛變），用 WaitAsync 取得最終結果。
    /// </summary>
    public InterlockResult Execute(LoadlockCommand cmd, int arg = 0)
    {
        bool urgent = cmd is LoadlockCommand.EStop or LoadlockCommand.Abort;
        if (!urgent)
        {
            var action = LoadlockCommands.ActionFor(cmd, arg, ArmSafePosSteps);
            if (action is { } a)
            {
                var r = Interlocks.Check(a, _snapshot());
                if (!r.Allowed) return r;
            }
        }
        if (cmd == LoadlockCommand.ArmGoto && arg < 0) return InterlockResult.Deny("命令參數錯誤");
        if (!_main.Connected || Main is not { Online: true } m) return InterlockResult.Deny("主控板未連線");

        // 第一次下命令時從主控板 LL_CMD_ACK 接續序號，避免 IPC 重開後跟主控板記住的值撞號
        if (!_seqSynced) { _seq = m[MainBoard.Input.LlCmdAck]; _seqSynced = true; }
        _seq = (ushort)(_seq + 1);
        if (_seq == 0) _seq = 1;
        try { _main.WriteLoadlockCommand(ToRegister(cmd), arg, _seq); }
        catch (Exception) { _seqSynced = false; return InterlockResult.Deny("主控板通訊失敗"); }
        return InterlockResult.Ok;
    }

    /// <summary>主控板目前回報的命令結果（只在 ACK 等於最後送出的序號時有效）。</summary>
    public LlResult? LastResult
        => Main is { Online: true } m && _seqSynced && m[MainBoard.Input.LlCmdAck] == _seq
            ? (LlResult)m[MainBoard.Input.LlCmdResult] : null;

    /// <summary>
    /// 等最後一個命令完成：DONE → true；DENIED / ERROR / BUSY / 逾時 → false。
    /// SENT 表示已送給 SLK、還在等 SLK 的完成事件，會繼續等。沒有完成事件的命令（如 ROUGH_ON）主控板直接回 DONE。
    /// </summary>
    public async Task<bool> WaitAsync(TimeSpan timeout)
    {
        var end = DateTime.Now + timeout;
        while (DateTime.Now < end)
        {
            switch (LastResult)
            {
                case LlResult.Done: return true;
                case LlResult.Denied or LlResult.Error or LlResult.Busy: return false;
            }
            await Task.Delay(50);
        }
        return false;
    }

    /// <summary>把主控板回報的 Loadlock 狀態（IR LL_*）填進 MachineSnapshot。</summary>
    public MachineSnapshot Fill(MachineSnapshot s)
    {
        if (Main is not { Online: true } m) return s with { LoadlockFresh = false };
        var link = (MainBoard.Input.LlLinkBits)m[MainBoard.Input.LlLink];
        if (!link.HasFlag(MainBoard.Input.LlLinkBits.TlmFresh)) return s with { LoadlockFresh = false };
        var flags = (MainBoard.Input.LlFlagsBits)m[MainBoard.Input.LlFlags];
        int pres = (m[MainBoard.Input.LlPres] << 16) | m[(ushort)(MainBoard.Input.LlPres + 1)];
        int pos = (m[MainBoard.Input.LlPos] << 16) | m[(ushort)(MainBoard.Input.LlPos + 1)];
        ushort arm = m[MainBoard.Input.LlArm];
        return s with
        {
            LoadlockFresh = true,
            LoadlockAlarm = m[MainBoard.Input.LlAlm],
            SlitValve = (LoadlockSv)m[MainBoard.Input.LlSv],
            ArmZ = (LoadlockZ)m[MainBoard.Input.LlZ],
            ArmMoving = arm is 1 or 3,             // HOMING / MOVING
            ArmPosSteps = pos,
            ArmSafePosSteps = ArmSafePosSteps,
            LoadlockPressureTorr = pres < 0 ? double.NaN : pres / 1000.0,   // -1 = 低於 SLK 量測下限
            LoadlockLidOpen = flags.HasFlag(MainBoard.Input.LlFlagsBits.LidOpen),
            LoadlockRoughOpen = flags.HasFlag(MainBoard.Input.LlFlagsBits.RoughOpen),
            LoadlockVentOpen = flags.HasFlag(MainBoard.Input.LlFlagsBits.VentOpen),
            LoadlockBusy = m[MainBoard.Input.LlSeq] != 0 || LastResult == LlResult.Sent,
        };
    }
}
