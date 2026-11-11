namespace Ald.Core;

/// <summary>
/// 流程引擎（ProcessSequencer）看到的機台：讀狀態 + 下命令。
/// 正式實作是 Ald.Comm.MachineService（經主控板）；單元測試用假的。
/// 所有命令都是「請求」：主控板 / 執行板還會再過互鎖，不准的不會動作，所以流程一律「下命令 → 等狀態回來」。
/// </summary>
public interface IMachine
{
    MachineSnapshot Snapshot { get; }

    /// <summary>真空板 DO（整組覆寫）。</summary>
    void SetVacuum(VacuumOutputs outputs);
    VacuumOutputs VacuumCommand { get; }

    /// <summary>加熱設定（°C），enable = false 關閉該組。超過 config 上限會被擋。</summary>
    void SetHeater(int channel, double setpointDegC, bool enable, double rampDegCPerMin = 0);

    /// <summary>MFC 設定（config 單位：sccm / psi），同時開關該路上游閥。</summary>
    void SetMfc(int channel, double value, bool upstreamValve);

    /// <summary>手動開 ALD 閥（bit0 = ALD_V1）；ALD 序列執行中由製程板自己控制。</summary>
    void SetAldValves(ushort mask);

    /// <summary>下載 ALD 步驟表到製程板（槽 1，時序由執行板 1 kHz 計時器執行）。</summary>
    void DownloadAld(AldRecipe recipe);
    void StartAld();
    void StopAld();

    /// <summary>Loadlock 命令（主控板過 LL-01~08 互鎖後送給 SLK），等到完成。</summary>
    Task<bool> LoadlockAsync(LoadlockCommand cmd, int arg, TimeSpan timeout, CancellationToken ct);
}
