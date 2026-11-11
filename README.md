# ald-chamber-sw

ALD 真空製程腔體設備控制軟體。三階架構（2026-11-10 方案 A）：

```
研華 ARK-1222（Linux：C# / Avalonia 人機 + ald-ecat IgH 主站）
   ├─ EtherCAT ─► 主控板（STM32H743 + LAN9252）─ CAN FD 背板 ─► 執行板 ×8（STM32G474）
   │                   └─ UART ─► RF（Throttle valve 預留）
   └─（主控板 UART）RS-485 ─► Loadlock（SLK 模組，韌體不改；互鎖在主控板做）
```

詳見 [`docs/architecture.md`](docs/architecture.md)。

| 資料夾 | 用途 |
| --- | --- |
| `shared/modbus/registers.yaml` | ★ 暫存器唯一定義（改這裡），依槽位分區 |
| `tools/codegen/` | 產生器：yaml → C 標頭 / C# / Markdown 對照表 |
| `firmware/` | 主控板與執行板韌體（可攜 C + PC 單元測試） |
| `ipc/` | C# .NET 8 方案（Core / Comm / Data / Hmi）+ `ecat-master/`（ald-ecat EtherCAT 主站程式） |
| `sim/` | 整機模擬器：正式韌體 + 物理模型 + SLK 模擬，`ald-ecat --sim` 使用（見 `docs/ipc-software.md`） |
| `recipes/` | 範例 Recipe |
| `docs/` | 架構、背板、互鎖、安全狀態、Loadlock 對接 |

## 開發環境（VS Code）
- 擴充套件：C/C++、C# Dev Kit、Python、Claude Code
- 安裝 .NET 8 SDK、Python 3.11+、gcc、STM32CubeIDE；IPC 端見 `docs/linux-ipc-setup.md`

## 常用指令
```bash
# 1. 改完 registers.yaml 後重新產生
pip install -r tools/codegen/requirements.txt
python tools/codegen/gen_registers.py

# 2. 在 PC 上跑韌體邏輯測試（不需要板子）
make -C firmware/tests

# 3. 整機模擬（正式韌體 + 物理模型），再接人機
make -C sim
make -C ipc/ecat-master
./ipc/ecat-master/ald-ecat --sim --port 1502 &
dotnet run --project ipc/src/Ald.Hmi -- --port 1502

# 4. 編譯 / 測試 IPC（含對虛擬機台的端到端測試）
cd ipc && dotnet test
```

> 自動產生的檔案（`firmware/common/registers/*.h`、`ipc/src/Ald.Comm/Generated/*.cs`、
> `shared/modbus/register-map.md`）不要手改。
