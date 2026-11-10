using System.IO.Ports;

namespace Ald.Comm;

/// <summary>
/// IPC ↔ Loadlock（SLK 模組）RS-485 串列埠，115200 8N1，ASCII 一行一個。
/// SLK 韌體不改；SLK 端是 RS-232，中間用一顆「自動收發切換」的 RS-232 ↔ RS-485 轉換器。
/// 一個背景執行緒收資料；另每 PollMs 送一次 ">SYS:ST" 取 TLM（SLK 不會主動推送）。
/// </summary>
public sealed class LoadlockLink : IDisposable
{
    private readonly SerialPort _port;
    private readonly object _txLock = new();
    private CancellationTokenSource? _cts;

    public int PollMs { get; set; } = 200;
    public SlkTelemetry? Last { get; private set; }
    public DateTime LastTlmTime { get; private set; } = DateTime.MinValue;

    public event Action<SlkTelemetry>? Telemetry;
    public event Action<string>? EventReceived;   // "EVT:..." 整行

    public LoadlockLink(string portName, int baud = 115200)
    {
        _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
        {
            NewLine = "\n",
            ReadTimeout = 100,
            WriteTimeout = 100,
        };
    }

    public bool IsFresh(TimeSpan stale) => Last is not null && DateTime.Now - LastTlmTime <= stale;

    public void Start()
    {
        _port.Open();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        new Thread(() => RxLoop(token)) { IsBackground = true, Name = "Loadlock RX" }.Start();
        new Thread(() => PollLoop(token)) { IsBackground = true, Name = "Loadlock Poll" }.Start();
    }

    /// <summary>送一行原始指令（不檢查互鎖！一般請用 LoadlockService.Execute）。</summary>
    internal void SendRaw(string line)
    {
        lock (_txLock) _port.Write(line + "\n");
    }

    private void PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { SendRaw(">SYS:ST"); } catch (Exception) { /* 埠錯誤：靠 TLM 逾時判斷離線 */ }
            Thread.Sleep(PollMs);
        }
    }

    private void RxLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string line;
            try { line = _port.ReadLine().TrimEnd('\r'); }
            catch (TimeoutException) { continue; }
            catch (Exception) { Thread.Sleep(200); continue; }

            if (line.StartsWith("TLM:", StringComparison.Ordinal))
            {
                var t = SlkProtocol.ParseTlm(line);
                if (t is null) continue;               // 破損資料不採用
                Last = t;
                LastTlmTime = DateTime.Now;
                Telemetry?.Invoke(t);
            }
            else if (line.StartsWith("EVT:", StringComparison.Ordinal))
            {
                if (line.StartsWith("EVT:SYS:BOOT_OK", StringComparison.Ordinal)) Last = null;   // SLK 重開機，狀態作廢
                EventReceived?.Invoke(line);
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _port.Dispose();
    }
}
