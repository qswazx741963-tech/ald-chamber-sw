using Ald.Core;

namespace Ald.Comm;

/// <summary>
/// Loadlock 的「卡控」就在這裡：每個命令送出前先跑 Interlocks.Check（LL-01~08），
/// 通過才送給 SLK。SLK 韌體維持全開放、不做互鎖。
/// 另外每收到一筆 TLM 就寫一份鏡像到主控板（HR LL_M_*），讓主控板做 PM 側互鎖（SV 沒關不准製程 / RF / vent）。
/// </summary>
public sealed class LoadlockService
{
    private readonly LoadlockLink _link;
    private readonly MainBoardLink? _main;
    private readonly Func<MachineSnapshot> _snapshot;
    private ushort _mirrorSeq;
    private LoadlockCommand? _pending;
    private TaskCompletionSource<bool>? _pendingDone;

    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMilliseconds(500);
    public int ArmSafePosSteps { get; set; } = 5894;

    public LoadlockService(LoadlockLink link, MainBoardLink? main, Func<MachineSnapshot> snapshot)
    {
        _link = link;
        _main = main;
        _snapshot = snapshot;
        _link.Telemetry += OnTelemetry;
        _link.EventReceived += OnEvent;
    }

    public bool Busy => _pending is not null;

    /// <summary>
    /// 檢查互鎖後送出命令。被擋下回傳 Allowed = false 與原因（給畫面顯示）。
    /// 有完成事件的命令（例如 LOAD）用 WaitAsync 等結果。
    /// </summary>
    public InterlockResult Execute(LoadlockCommand cmd, int arg = 0)
    {
        bool urgent = cmd is LoadlockCommand.EStop or LoadlockCommand.Abort;
        var action = LoadlockCommands.ActionFor(cmd, arg, ArmSafePosSteps);
        if (action is { } a)
        {
            var r = Interlocks.Check(a, _snapshot());
            if (!r.Allowed) return r;
        }
        if (Busy && !urgent) return InterlockResult.Deny($"上一個動作（{_pending}）還沒完成");
        var line = SlkProtocol.Build(cmd, arg);
        if (line is null) return InterlockResult.Deny("命令參數錯誤");

        _link.SendRaw(line);
        if (urgent) { _pendingDone?.TrySetResult(false); _pending = null; _pendingDone = null; }   // 急停 / 中止：前一個動作視為失敗
        if (HasCompletion(cmd))
        {
            _pending = cmd;
            _pendingDone = new TaskCompletionSource<bool>();
        }
        return InterlockResult.Ok;
    }

    /// <summary>等目前命令完成；true = 完成，false = 失敗或逾時。</summary>
    public async Task<bool> WaitAsync(TimeSpan timeout)
    {
        var tcs = _pendingDone;
        if (tcs is null) return true;
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (done != tcs.Task) { _pending = null; return false; }
        return tcs.Task.Result;
    }

    private static bool HasCompletion(LoadlockCommand cmd) => cmd is not (
        LoadlockCommand.Status or LoadlockCommand.Abort or LoadlockCommand.RoughOn or LoadlockCommand.RoughOff
        or LoadlockCommand.VentOn or LoadlockCommand.VentOff);

    private void OnEvent(string evt)
    {
        if (_pending is not { } cmd) return;
        var r = SlkProtocol.Completes(cmd, evt);
        if (r is null) return;
        _pending = null;
        _pendingDone?.TrySetResult(r.Value);
    }

    private void OnTelemetry(SlkTelemetry t)
    {
        if (_main is null || !_main.Connected) return;
        try { _main.WriteLoadlockMirror(t, ++_mirrorSeq == 0 ? ++_mirrorSeq : _mirrorSeq); }
        catch (Exception) { /* 主控板斷線：主控板那邊會判斷鏡像逾時 → Loadlock 狀態不明 → 擋 PM 動作 */ }
    }

    /// <summary>把 Loadlock 最新狀態填進 MachineSnapshot（HMI 組快照時呼叫）。</summary>
    public MachineSnapshot Fill(MachineSnapshot s)
    {
        var t = _link.Last;
        if (t is null || !_link.IsFresh(StaleAfter)) return s with { LoadlockFresh = false };
        return s with
        {
            LoadlockFresh = true,
            LoadlockAlarm = t.Alm,
            SlitValve = (LoadlockSv)t.Sv,
            ArmZ = (LoadlockZ)t.Z,
            ArmMoving = t.Arm is 1 or 3,           // HOMING / MOVING
            ArmPosSteps = t.Pos,
            ArmSafePosSteps = ArmSafePosSteps,
            LoadlockPressureTorr = t.PresMTorr / 1000.0,
            LoadlockLidOpen = t.LidOpen,
            LoadlockRoughOpen = t.RoughOpen,
            LoadlockVentOpen = t.VentOpen,
            LoadlockBusy = t.Seq != 0 || Busy,
        };
    }
}
