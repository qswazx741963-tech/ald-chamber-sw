using System.IO.Ports;
using Ald.Comm.Generated;
using NModbus;
using NModbus.Serial;

namespace Ald.Comm;

/// <summary>一條 RS-485 線（一個 COM 埠）上的一塊 Arduino 輪詢設定。</summary>
public sealed record SlaveSpec(byte SlaveId, ushort InputSize, ushort HoldingSize);

/// <summary>某塊板最新一次讀到的原始暫存器。</summary>
public sealed class SlaveImage
{
    public required SlaveSpec Spec { get; init; }
    public ushort[] Input = Array.Empty<ushort>();
    public bool Online;
    public bool MapVersionOk;
    public int ConsecutiveErrors;
    public DateTime LastOk;
}

/// <summary>
/// 一個 COM 埠一個執行緒：依序輪詢該線上的每塊板，寫心跳、讀 Input Register。
/// COM1 只有 Arduino 1；COM2 輪流讀 ID 2 / 3 / 4。
/// </summary>
public sealed class ModbusBus : IDisposable
{
    private readonly SerialPort _port;
    private readonly IModbusMaster _master;
    private readonly Dictionary<byte, SlaveImage> _slaves;
    private readonly object _lock = new();
    private ushort _heartbeat;
    private CancellationTokenSource? _cts;

    public event Action<SlaveImage>? Updated;

    public ModbusBus(string portName, int baud, IEnumerable<SlaveSpec> slaves)
    {
        _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 100,
            WriteTimeout = 100,
        };
        _slaves = slaves.ToDictionary(s => s.SlaveId, s => new SlaveImage { Spec = s });
        var factory = new ModbusFactory();
        _port.Open();
        _master = factory.CreateRtuMaster(new SerialPortAdapter(_port));
        _master.Transport.Retries = 1;
    }

    public void Start(TimeSpan period)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        new Thread(() => Loop(period, token)) { IsBackground = true, Name = $"Modbus {_port.PortName}" }.Start();
    }

    /// <summary>寫入 Holding Register（例如 DO 命令、設定值）。與輪詢共用同一條線，所以要上鎖。</summary>
    public void Write(byte slaveId, ushort addr, params ushort[] values)
    {
        lock (_lock) _master.WriteMultipleRegisters(slaveId, addr, values);
    }

    private void Loop(TimeSpan period, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var t0 = DateTime.Now;
            _heartbeat++;
            foreach (var s in _slaves.Values)
            {
                try
                {
                    lock (_lock)
                    {
                        // 心跳寫在 HR 0（四塊板相同位址，見 registers.yaml common）
                        _master.WriteSingleRegister(s.Spec.SlaveId, Board1Process.Holding.Heartbeat, _heartbeat);
                        s.Input = _master.ReadInputRegisters(s.Spec.SlaveId, 0, s.Spec.InputSize);
                    }
                    s.MapVersionOk = s.Input[Board1Process.Input.MapVersion] == RegisterMap.MapVersion;
                    s.Online = s.MapVersionOk;
                    s.ConsecutiveErrors = 0;
                    s.LastOk = DateTime.Now;
                }
                catch (Exception)   // TimeoutException / SlaveException / IOException
                {
                    if (++s.ConsecutiveErrors >= 3) s.Online = false;   // 連續 3 次失敗才判離線
                }
                Updated?.Invoke(s);
            }
            var wait = period - (DateTime.Now - t0);
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _master.Dispose();
        _port.Dispose();
    }
}
