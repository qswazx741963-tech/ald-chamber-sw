# ald-chamber-sw

ALD 真空製程腔體設備控制軟體。1 台 IPC（C# / WPF）透過 Modbus RTU 控制 4 塊 Arduino。

| 資料夾 | 用途 |
| --- | --- |
| `shared/modbus/registers.yaml` | ★ 暫存器唯一定義（改這裡） |
| `tools/codegen/` | 產生器：yaml → .h / .cs / .md |
| `firmware/` | PlatformIO 專案，4 個 env 對應 4 塊板 |
| `ipc/` | C# .NET 8 方案（Core / Comm / Data / Hmi） |
| `recipes/` | 範例 Recipe |
| `docs/` | I/O 表、安全狀態、互鎖清單 |

## 開發環境（VS Code）
- 擴充套件：PlatformIO IDE、C# Dev Kit、Python、Claude Code
- 安裝 .NET 8 SDK、Python 3.11+

## 常用指令
```bash
# 1. 改完 registers.yaml 後重新產生
pip install -r tools/codegen/requirements.txt
python tools/codegen/gen_registers.py

# 2. 編譯 / 燒錄某一塊板（例如 Arduino 2）
cd firmware
pio run -e board2_heater
pio run -e board2_heater -t upload --upload-port COM5

# 3. 編譯 IPC
cd ipc
dotnet build
dotnet run --project src/Ald.Hmi
```

> 自動產生的檔案（`firmware/lib/AldRegisters/*.h`、`ipc/src/Ald.Comm/Generated/*.cs`、
> `shared/modbus/register-map.md`）不要手改。
