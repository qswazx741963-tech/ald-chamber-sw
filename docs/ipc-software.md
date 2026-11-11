# IPC 底層軟體與整機模擬器

2026-11-11 起的第一階段：沒有硬體也能整台跑起來，人機 / 流程 / 記錄先開發，板子回來直接接上。

```
 Avalonia 人機（Ald.Hmi）
   │  只讀 Snapshot、只呼叫 MachineService / ProcessSequencer
 MachineService（Ald.Comm）── AlarmManager（Ald.Core）── AlarmLog / TrendLog（Ald.Data，SQLite）
   │  Modbus TCP 127.0.0.1:502（心跳 50 ms）
 ald-ecat（ipc/ecat-master，C，1 ms 即時迴圈）
   ├─ 正式：IgH EtherCAT 主站 ─► 主控板（LAN9252）
   └─ --sim：虛擬機台（sim/）＝ 正式主控板韌體 + 4 塊執行板韌體 + SLK 模擬 + 物理模型
```

## 1. 整機模擬器（`sim/`）

**跑的是正式韌體原始碼**（`firmware/main_board/app`、`firmware/exec_board/app`），不是另外寫的假邏輯：

- 每塊板的韌體各編一份，連結時用 `objcopy` 把符號加上前綴（`n0_` 主控板、`n1_`~`n4_` 槽 1~4），
  所以 5 份韌體的 static 變數、`g_ir` / `g_hr` 互不干擾，韌體一行都不用改。
- `sim_hal.c` 實作 `hal_port.h`：虛擬 CAN FD 背板（廣播）、開集極 SAFE 線、主控板 UART。
- `slk_emu.c`：SLK Loadlock（同 PROTOCOL_v2 的 ASCII 協議；跟真的 SLK 一樣不做互鎖，用來驗證主控板有沒有把關）。
- `plant.c`：簡易物理模型（只求趨勢合理）——PM 20 L、Dry pump 5 L/s、Turbo 300 L/s（20 s 到速）、
  GV 2 s、vent、MFC 進氣、SV 導通、加熱一階模型、Heater module 步進、Wafer lift 氣缸、各壓力計輸出電壓。
- 故障注入：`vm_knobs()`（EMO、CDA、lid、Dry pump / Turbo 警報、熱電偶斷線、SLK 壓力計斷線、漏氣量）。

```bash
make -C sim                                  # 編 + 整合測試（42 項）
make -C ipc/ecat-master                      # ald-ecat（含虛擬機台）
./ipc/ecat-master/ald-ecat --sim --port 1502 --speed 10   # 10 倍速
dotnet run --project ipc/src/Ald.Hmi -- --port 1502
```

> `--speed` 加速時，IPC 心跳（真實 50 ms）在模擬時間變長；超過 10 倍速請把 config `watchdogMs` 調大。

模擬已經抓到並修正的韌體問題（單元測試抓不到，因為假硬體太理想）：

| 問題 | 修正 |
| --- | --- |
| 主控板在安全狀態時自己拉低 SAFE 線，Reset 時讀到的永遠是低 → 永遠解除不了 | Reset 先放開 SAFE 線 30 ms 再確認（`SAFE_RELEASE_MS`），外部還在拉就維持安全狀態 |
| 主控板開機預設的互鎖參數（壓力門檻、手臂安全位置…）被 EtherCAT RxPDO 的 0 洗掉 | `registers.yaml` 加 `default:`，產生 `ALD_HR_DEFAULTS`；主控板與 ald-ecat 啟動都載入同一份 |
| 真空板 / 製程板在安全狀態時 DO 回授沒更新（畫面看到閥還開著） | 回授改成實際輸出 |
| 執行板只送了 HELLO、還沒送 IR 就被算在線（表裡還是 0） | 要收到過 IR 才算在線 |

## 2. MachineService（`ipc/src/Ald.Comm`）

- **每次連上**（包含 ald-ecat 或主控板重開後）：把 config 寫進主控板——看門狗、互鎖參數（`IL_*`）、
  PM / LL 壓力計型號與位址、16 組加熱上限；再讀回目前命令值，避免 IPC 記憶與實際不同步。
- **每輪輪詢**：`SnapshotBuilder` 把整張表換成 `MachineSnapshot`（壓力、溫度、MFC、閥、馬達、Loadlock），
  `AlarmManager` 比較前後產生「發生 / 解除」事件。
- **命令**：`SetVacuum`、`SetHeater`（超過 config 上限直接拒絕）、`SetMfc`（config 單位 → % 滿刻度）、
  `SetAldValves`、`DownloadAld` / `StartAld` / `StopAld`、`HeaterHomeAsync` / `HeaterMoveAsync`、`SetWaferLift`、
  `Loadlock.Execute`、`ResetAsync`、`EnterSafe`。全部都是「請求」，主控板 / 執行板還會再過互鎖。
- **Reset 順序**：先主控板（等它放開 SAFE 線確認完）→ 再各執行板。

## 3. 流程引擎（`ProcessSequencer`）

`ProcessRecipe`：抽真空（Dry pump → 粗抽到 crossover → Turbo 到速 → 開 GV → base pressure）→ 升溫（全部進 ±容差並維持）
→ 通氣穩定 → ALD（步驟表下載到製程板，時序由執行板 1 kHz 計時器跑）→ 製程結束（關氣、加熱回待機）→（可選）回填。

- 每一步都有逾時；失敗 / 中止 / 進安全狀態 → 停 ALD、關前驅物閥與 MFC，**不自動 vent**，加熱與真空維持。
- 保護不靠流程引擎：主控板與執行板的互鎖一直有效，IPC 當掉也一樣。

## 4. 記錄（SQLite，`dataLog.path`）

- `alarm`：警報發生 / 解除、互鎖擋下原因（中文）、流程步驟、操作紀錄。
- `trend`：每 `dataLog.intervalMs` 一筆寬表（各壓力、16 組溫度與輸出、8 路 MFC、閥狀態、ALD cycle、Heater 位置、SV）。
- WAL 模式（斷電不壞），保存 `keepDays` 天。

## 5. 測試

| 指令 | 內容 |
| --- | --- |
| `make -C firmware/tests` | 韌體邏輯單元測試（主控板 186、執行板 40） |
| `make -C sim` | 整機模擬整合測試 42 項：開機上線、Reset、抽真空、開 SV 互鎖、Turbo GV 聯動、加熱上限、馬達、心跳中斷 |
| `make -C ipc/ecat-master test` | ald-ecat bridge（含 HR 預設值） |
| `cd ipc && dotnet test` | C# 63 項，含**端到端**：MachineService ↔ ald-ecat --sim（20 倍速）跑完整段配方、心跳中斷進安全狀態 |

## 待做

- 人機：手動操作頁、Recipe 編輯 / 執行頁、趨勢圖、權限（目前只有總覽 + 警報）。
- RF（鎧泰 600 W / 300 W）協議確認後：主控板 UART 驅動 + 模擬。
- 晶圓位置追蹤（傳片流程、WAFER_PRESENT）。
- STM32 HAL（`hal_port_stm32.c`）：等腳位分配。
