# 互鎖清單

## 一、三層保護

| 層 | 位置 | 反應時間 | 用途 |
| --- | --- | --- | --- |
| 1 硬體 | 背板 SAFE 線、EMO、過溫開關、TRANSFER_PERMIT 繼電器 | 立即 | 人員 / 危險氣體 / 設備損壞，**不靠軟體** |
| 2 主控板 | `firmware/main_board/app/interlock.c` | < 10 ms | PM 側跨板互鎖（PM-01~03、MON-01），**真正執行**；Loadlock 狀態來自 IPC 鏡像 |
| 3 IPC | `ipc/src/Ald.Core/Interlocks.cs` | 送命令前 | **Loadlock 側（LL-01~08）的實際執行者**；PM 側先擋、顯示原因 |

板內互鎖（例如真空板 Turbo / vent）在各執行板 `role_*.c`。

**改規則要三處一起改**：本文件、`interlock.c` + `firmware/tests/test_main_board.c`、`Interlocks.cs` + `InterlockTests.cs`。

## 二、Loadlock ↔ PM（SLK 維持全開放不改，Loadlock 命令由 IPC 經 RS-485 送出前檢查）

| 編號 | 動作 | 允許條件（全部成立） | 被擋原因位元 |
| --- | --- | --- | --- |
| LL-01 | 開 Slit valve | LL 資料新鮮、LL 無警報、非安全狀態、PM 各板在線、PM 沒在跑製程 / RF、PM 沒在 vent、LL 艙蓋關、LL 粗抽 / 洩壓閥都關、**LL 壓力 ≤ 2 Torr**、**PM 壓力 ≤ 1 Torr**（壓力計飽和視為過高） | LL_UNKNOWN、LL_ALARM、SAFE_ACTIVE、SLOT_OFFLINE、PM_BUSY、PM_VENTING、LL_LID_OPEN、LL_VALVE_CONFLICT、LL_PRESSURE_HIGH、PM_PRESSURE_HIGH |
| LL-02 | 關 Slit valve（含 SEQ:XFER） | LL 資料新鮮、手臂位置 ≤ 安全位置（160 mm）且沒在移動 | ARM_IN_SV_PATH |
| LL-03 | SEQ:LOAD / UNLOAD | LL-01 全部 + PM 在交接位置 + LL 沒有其他序列在跑 | 同 LL-01 + PM_NOT_AT_TRANSFER、LL_BUSY |
| LL-04 | 手臂伸進 PM（ARM:PM、GOTO > 安全位置） | SV 全開、Z 下降到位、PM 在交接位置、PM 沒在跑製程 / RF | SV_NOT_OPEN、Z_NOT_DOWN、PM_NOT_AT_TRANSFER、PM_BUSY |
| LL-05 | 手臂在 LL 側移動（HOME、ARM:LL、GOTO ≤ 安全位置、RCV） | Z 下降到位（SLK MANUAL_MODE 不檢查 IL-06） | Z_NOT_DOWN |
| LL-06 | Z 上 / 下 | 手臂在 PM 內時：PM 在交接位置、PM 沒在跑 | PM_NOT_AT_TRANSFER、PM_BUSY |
| LL-07 | LL 抽氣（SEQ:PUMP、粗抽閥開） | SV 關、艙蓋關、洩壓閥關 | SV_NOT_CLOSED、LL_LID_OPEN、LL_VALVE_CONFLICT |
| LL-08 | LL 洩壓（SEQ:VENT、洩壓閥開） | SV 關、粗抽閥關 | SV_NOT_CLOSED、LL_VALVE_CONFLICT |
| PM-01 | ALD 製程開始 / 前驅物閥開 | PM 各板在線、LL 資料新鮮、SV 關、手臂在外 | SLOT_OFFLINE、LL_UNKNOWN、SV_NOT_CLOSED、ARM_IN_SV_PATH |
| PM-02 | RF on | 同 PM-01（另 IPC 檢查 Lid、RF 線） | 同上 |
| PM-03 | Chamber vent | 同 PM-01（另真空板板內檢查 Turbo / GV） | 同上 |

永遠放行（往安全方向）：ESTOP、ABORT、RESET、STATUS、關粗抽 / 洩壓閥。

## 三、持續監看（主控板每圈）

| 編號 | 條件 | 反應 |
| --- | --- | --- |
| MON-01 | PM 在跑製程 / RF / vent，但 SV 不是「確定關閉」（含 LL 資料不新鮮） | 立刻停 ALD、關前驅物閥、RF off、關 vent |
| MON-02 | LL-01 任一條件不成立 | TRANSFER_PERMIT 輸出 OFF（預留，目前不接 Loadlock） |
| MON-03 | IPC 寫的 Loadlock 鏡像超過 500 ms 沒更新 | 視為 Loadlock 狀態不明 → PM-01~03 全擋，PM 在跑則觸發 MON-01 |

## 四、PM 內部

| 編號 | 動作 | 條件 | 實作位置 |
| --- | --- | --- | --- |
| PM-10 | Chamber vent | Turbo 未在轉速、Gate valve 已關 | 槽 3 板內 + IPC |
| PM-11 | Turbo gate valve 開 | Dry pump 運轉、無警報、Foreline 壓力 OK（TODO） | 槽 3 板內 |
| PM-12 | RF on | Lid / Lid cover 關、RF 線到位 | IPC + 硬體線 |
| PM-13 | 加熱器 | 過溫開關未動作 | 槽 2 板內 + 硬體切電源 |

## 五、待確認

- 開 SV 的壓力門檻（2 Torr / 1 Torr）、是否要另加壓差條件。Loadlock 壓力計下限 1 Torr，低於下限讀 -1。
- 開 SV 時 Turbo GV 要不要先關（避免 LL 氣體衝擊 Turbo）。
- PM「交接位置」定義：目前假設 Susceptor 在原點 + Wafer lift 在上。
- PM 壓力計換算（目前假設 0–10 V = 0–1 Torr）。
