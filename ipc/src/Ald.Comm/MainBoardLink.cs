using System.Net.Sockets;
using Ald.Comm.Generated;
using NModbus;

namespace Ald.Comm;

/// <summary>某一槽最新一次讀到的 Input Register（區內位址 0 起算）。</summary>
public sealed class SlotImage
{
    public required int Slot { get; init; }
    public required BoardType ExpectedType { get; init; }
    public required ushort InputSize { get; init; }
    public ushort[] Input = Array.Empty<ushort>();
    public bool Online;
    public DateTime LastOk;

    /// <summary>用「絕對位址」讀值（例如 HeaterBoard.Input.TcTemp），內部自動扣掉槽位基底。</summary>
    public ushort this[ushort absAddr] => Input[absAddr - Slot * RegisterMap.SlotStride];
}

/// <summary>
/// IPC ↔ 主控板：Modbus TCP、Unit ID 1，連本機 ald-ecat（EtherCAT 主站程式，127.0.0.1:502），它再經 EtherCAT 跟主控板交換整張表。
/// 主控板是唯一的從站；各執行板的資料都在主控板那張表裡，依槽位分區（見 shared/modbus/register-map.md）。
/// 一個背景執行緒：寫心跳 → 讀主控板 + 各槽 IR → 發出 Updated 事件。
/// 斷線（ald-ecat 回例外 0x0B = EtherCAT 掉線）時 Connected=false、全部槽位離線。
/// </summary>
public sealed class MainBoardLink : IDisposable
{
    private const int MaxRead = 120;            // Modbus 一次最多 125 個暫存器
    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly object _lock = new();
    private readonly List<SlotImage> _slots;
    private TcpClient? _tcp;
    private IModbusMaster? _master;
    private ushort _heartbeat;
    private CancellationTokenSource? _cts;

    public bool Connected { get; private set; }
    public IReadOnlyList<SlotImage> Slots => _slots;
    public event Action<MainBoardLink>? Updated;

    public MainBoardLink(string host, int port, byte unitId, IEnumerable<SlotImage> slots)
    {
        _host = host;
        _port = port;
        _unitId = unitId;
        _slots = slots.ToList();
    }

    public void Start(TimeSpan period)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        new Thread(() => Loop(period, token)) { IsBackground = true, Name = "MainBoardLink" }.Start();
    }

    /// <summary>寫 Holding Register（絕對位址）。主控板會再經過互鎖才轉給執行板。</summary>
    public void Write(ushort absAddr, params ushort[] values)
    {
        lock (_lock)
        {
            if (_master is null) throw new InvalidOperationException("主控板未連線");
            _master.WriteMultipleRegisters(_unitId, absAddr, values);
        }
    }

    /// <summary>讀 Holding Register（絕對位址），例如重新連線後讀回目前的命令值。</summary>
    public ushort[] ReadHolding(ushort absAddr, ushort count)
    {
        lock (_lock)
        {
            if (_master is null) throw new InvalidOperationException("主控板未連線");
            return _master.ReadHoldingRegisters(_unitId, absAddr, count);
        }
    }

    /// <summary>從斷線變成連線的那一刻（在背景執行緒、讀完第一輪之前呼叫）：用來寫 config、讀回命令值。</summary>
    public event Action<MainBoardLink>? Connecting;

    /// <summary>
    /// 下一個 Loadlock 命令給主控板：一次寫 HR LL_CMD、LL_CMD_ARG（i32，高字在前）、LL_CMD_SEQ。
    /// 主控板看到 SEQ 改變才執行一次（先過 LL-01~08 互鎖，再經 RS-485 送給 SLK），結果回在 IR LL_CMD_ACK / LL_CMD_RESULT。
    /// </summary>
    public void WriteLoadlockCommand(MainBoard.Holding.LlCmdValues cmd, int arg, ushort seq)
        => Write(MainBoard.Holding.LlCmd, (ushort)cmd, (ushort)(arg >> 16), (ushort)arg, seq);

    private void Connect()
    {
        _tcp?.Dispose();
        _tcp = new TcpClient { ReceiveTimeout = 200, SendTimeout = 200 };
        _tcp.Connect(_host, _port);
        _master = new ModbusFactory().CreateMaster(_tcp);
        _master.Transport.Retries = 1;
    }

    private ushort[] ReadInput(ushort start, ushort count)
    {
        var result = new ushort[count];
        for (ushort off = 0; off < count; off += MaxRead)
        {
            var n = (ushort)Math.Min(MaxRead, count - off);
            _master!.ReadInputRegisters(_unitId, (ushort)(start + off), n).CopyTo(result, off);
        }
        return result;
    }

    private void Loop(TimeSpan period, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var t0 = DateTime.Now;
            try
            {
                lock (_lock)
                {
                    if (!Connected)
                    {
                        Connect();
                        // 先確認 ald-ecat 跟主控板是通的（主控板沒在 OP 時 ald-ecat 回例外 0x0B），再寫 config
                        _master!.ReadInputRegisters(_unitId, 0, 1);
                        Connected = true;
                        Connecting?.Invoke(this);
                    }
                    _heartbeat++;
                    // 心跳寫在槽 0 HR 0，主控板超過 WATCHDOG_MS 沒看到變化就全機進安全狀態
                    _master!.WriteSingleRegister(_unitId, MainBoard.Holding.Heartbeat, _heartbeat);
                    foreach (var s in _slots)
                    {
                        s.Input = ReadInput((ushort)(s.Slot * RegisterMap.SlotStride), s.InputSize);
                        s.Online = s.Input[2] == RegisterMap.MapVersion        // 區內 IR 2 = MAP_VERSION
                                   && s.Input[0] == (ushort)s.ExpectedType;    // 區內 IR 0 = BOARD_TYPE
                        if (s.Online) s.LastOk = DateTime.Now;
                    }
                    // 槽 1~8 是否真的在線，以主控板 SLOT_ONLINE 為準
                    var main = _slots.FirstOrDefault(x => x.Slot == 0);
                    if (main is not null)
                        foreach (var s in _slots.Where(x => x.Slot != 0))
                            s.Online &= (main[MainBoard.Input.SlotOnline] & (1 << s.Slot)) != 0;
                }
                Updated?.Invoke(this);
            }
            catch (Exception)   // SocketException / IOException / TimeoutException / SlaveException
            {
                Connected = false;
                foreach (var s in _slots) s.Online = false;
                Updated?.Invoke(this);
                Thread.Sleep(500);   // 斷線重連間隔
            }
            var wait = period - (DateTime.Now - t0);
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _master?.Dispose();
        _tcp?.Dispose();
    }
}
