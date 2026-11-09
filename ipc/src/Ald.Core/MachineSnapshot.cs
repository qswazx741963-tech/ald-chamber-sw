namespace Ald.Core;

/// <summary>某一時刻整台機台的狀態（由通訊層每次輪詢更新）。Core 只依賴它，不直接碰串列埠。</summary>
public sealed record MachineSnapshot
{
    public DateTime Time { get; init; } = DateTime.Now;

    // 連線
    public bool[] BoardOnline { get; init; } = new bool[5];   // index 1-4 = Arduino 1-4

    // Arduino 3：真空
    public bool ChamberLidClosed { get; init; }
    public bool LidCoverClosed { get; init; }
    public bool SlitOpened { get; init; }
    public bool SlitClosed { get; init; }
    public bool TurboAtSpeed { get; init; }
    public bool TurboGateClosed { get; init; }
    public double ChamberPressureTorr { get; init; } = double.NaN;   // 由 config 公式換算

    // Arduino 1：RF 線
    public bool Rf1PowerCableOk { get; init; }
    public bool Rf1MatchCableOk { get; init; }

    // Arduino 4：升降
    public bool WaferLiftHome { get; init; }
    public bool IndexerDown { get; init; }   // TODO 感測器在 Arduino 3，待確認搬板
}
