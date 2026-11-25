# 互鎖清單

## 一、三層保護

| 層 | 位置 | 反應時間 | 用途 |
| --- | --- | --- | --- |
| 1 硬體 | 背板 SAFE 線、EMO、過溫開關、TRANSFER_PERMIT 繼電器 | 立即 | 人員 / 危險氣體 / 設備損壞，**不靠軟體** |
| 2 主控板 | `firmware/main_board/app/interlock.c` | < 10 ms | **全部跨系統互鎖的真正執行者**：Loadlock 側 LL-01~08（命令經主控板 RS-485 送給 SLK）+ PM 側 PM-01~07、MON-01~05 |
| 3 IPC | `ipc/src/Ald.Core/Interlocks.cs` | 送命令前 | 預檢：送命令前先擋、在畫面顯示原因（主控板仍會再檢查一次） |

板內互鎖（例如真空板 Turbo / vent）在各執行板 `role_*.c`。

**改規則要三處一起改**：本文件、`interlock.c` + `firmware/tests/test_main_board.c`、`Interlocks.cs` + `InterlockTests.cs`。

## 二、Loadlock ↔ PM（SLK 維持全開放不改，Loadlock 命令由主控板檢查後經 RS-485 送出）

| 編號 | 動作 | 允許條件（全部成立） | 被擋原因位元 |
| --- | --- | --- | --- |
| LL-01 | 開 Slit valve | LL 資料新鮮、LL 無警報、非安全狀態、PM 各板在線、PM 沒在跑製程 / RF、PM 沒在 vent、LL 艙蓋關、LL 粗抽 / 洩壓閥都關、**Turbo GV 已關**、**壓力條件**（預設 LL 與 PM 都 < 1 Torr，SLK 讀「低於 1 Torr」算數；可切換為壓差 ≤ 50 mTorr；任一邊壓力未知 = 不准，含 LL 壓力計疑似斷線、PM 壓力計故障 / 飽和 / 離線，見 `gauges.md`） | LL_UNKNOWN、LL_ALARM、SAFE_ACTIVE、SLOT_OFFLINE、PM_BUSY、PM_VENTING、LL_LID_OPEN、LL_VALVE_CONFLICT、DP_HIGH、PRESSURE_UNKNOWN、TURBO_GV_OPEN |
| LL-02 | 關 Slit valve（含 SEQ:XFER） | LL 資料新鮮、手臂位置 ≤ 安全位置（160 mm）且沒在移動 | ARM_IN_SV_PATH |
| LL-03 | SEQ:LOAD / UNLOAD | LL-01 全部 + PM 在交接位置 + LL 沒有其他序列在跑 | 同 LL-01 + PM_NOT_AT_TRANSFER、LL_BUSY |
| LL-04 | 手臂伸進 PM（ARM:PM、GOTO > 安全位置） | SV 全開、Z 下降到位、PM 在交接位置、PM 沒在跑製程 / RF | SV_NOT_OPEN、Z_NOT_DOWN、PM_NOT_AT_TRANSFER、PM_BUSY |
| LL-05 | 手臂在 LL 側移動（HOME、ARM:LL、GOTO ≤ 安全位置、RCV） | Z 下降到位（SLK MANUAL_MODE 不檢查 IL-06；Z 軸停用時不檢查） | Z_NOT_DOWN |
| LL-06 | Z 上 / 下 | 手臂在 PM 內時：PM 在交接位置、PM 沒在跑（Z 軸停用時不檢查） | PM_NOT_AT_TRANSFER、PM_BUSY |
| LL-07 | LL 抽氣（SEQ:PUMP、粗抽閥開） | SV 關、艙蓋關、洩壓閥關、**Chamber 粗抽閥關**（共用 Dry Pump）。Turbo 前級閥開著不擋：主控板先排隊，關好前級閥再送（見下方「共用 Dry Pump」） | SV_NOT_CLOSED、LL_LID_OPEN、LL_VALVE_CONFLICT、PM_ROUGHING |
| LL-08 | LL 洩壓（SEQ:VENT、洩壓閥開） | SV 關、粗抽閥關 | SV_NOT_CLOSED、LL_VALVE_CONFLICT |
| PM-01 | ALD 製程開始 / 前驅物閥開 | PM 各板在線、LL 資料新鮮、SV 關、手臂在外 | SLOT_OFFLINE、LL_UNKNOWN、SV_NOT_CLOSED、ARM_IN_SV_PATH |
| PM-02 | RF on | 同 PM-01（另 IPC 檢查 Lid、RF 線） | 同上 |
| PM-03 | Chamber vent | 同 PM-01（另真空板板內檢查 Turbo / GV） | 同上 |
| PM-04 | Heater module 升降（軸 0 MOVE / HOME） | LL 資料新鮮、手臂已退出 SV 路徑 | LL_UNKNOWN、ARM_IN_SV_PATH |
| PM-05 | Wafer lift 上 / 下 | LL 資料新鮮、手臂沒在移動（停在 PM 內交接可以）、Heater module 在下位。不准時維持上一個狀態，不會清成 0 | LL_UNKNOWN、ARM_MOVING、HEATER_NOT_DOWN |
| PM-06 | Turbo gate valve 開 | LL 資料新鮮、SV 確定關閉；條件不成立時已開著的 GV 也會被關 | LL_UNKNOWN、SV_NOT_CLOSED |
| PM-07 | Chamber 粗抽閥開 | LL 沒在粗抽（共用 Dry Pump）；LL 抽完自動恢復 IPC 的命令 | LL_ROUGHING |

**共用 Dry Pump**（2026-11-25 使用者選「自動切換前級閥」）：LL 粗抽、Chamber 粗抽、Turbo 前級共用同一台 Dry Pump。

1. LL 粗抽命令（PUMP / ROUGH_ON）進來時，若 Turbo 前級閥開著：主控板先排隊（IPC 看到 SENT），同一時間把前級閥關掉，前級閥 DO 關掉滿 300 ms 才送給 SLK；3 s 內關不掉 = 命令失敗（ERROR）。
2. 「LL 粗抽中」= SLK 粗抽閥開、或腔體狀態 PUMPING、或 PUMP 序列還沒完成、或剛送出粗抽命令 1.5 s 內。TLM 中斷時沿用最後狀態（偏安全）。
3. LL 粗抽中：Chamber 粗抽閥、Turbo 前級閥都由主控板強制關（MON-05），IPC 的命令不變；LL 粗抽閥關掉後下一包就自動恢復 IPC 要的狀態。
4. IPC 要前級閥開（Turbo 需要前級）但被暫關超過 `IL_FORELINE_OFF_MAX_S`（預設 60 s，config `interlock.forelineOffMaxS`，5~600）→ 主控板送 ABORT + 關 LL 粗抽閥，IL_DENY2 = FORELINE_TIMEOUT，前級閥開回。
5. 畫面：總覽前級閥下方顯示「LL 粗抽中暫關」；被擋的原因寫進警報紀錄（互鎖）。
6. 被擋的真空閥請求（GV、Chamber 粗抽）只在剛被擋那一次寫 IL_DENY，不會每 10 ms 覆寫，蓋掉 Loadlock 命令被擋的原因。

**PM 交接位置**（2026-11-11 確認）：Heater module 在下位且沒在動 + Wafer lift 停在上 / 下任一定位。
送片：lift down → 手臂把 Wafer 送進來 → lift up 接住 → 手臂退出 → Heater module up 到 Process 位置。取片相反（lift 在 up）。

永遠放行（往安全方向）：ESTOP、ABORT、RESET、STATUS、關粗抽 / 洩壓閥。

## 三、持續監看（主控板每圈）

| 編號 | 條件 | 反應 |
| --- | --- | --- |
| MON-01 | PM 在跑製程 / RF / vent，但 SV 不是「確定關閉」（含 LL 資料不新鮮） | 立刻停 ALD、關前驅物閥、RF off、關 vent |
| MON-02 | LL-01 任一條件不成立 | TRANSFER_PERMIT 輸出 OFF（預留，目前不接 Loadlock） |
| MON-03 | 主控板收到的 SLK TLM 超過 IL_LL_STALE_MS（預設 500 ms）沒更新 | 視為 Loadlock 狀態不明 → PM-01~06 全擋，PM 在跑則觸發 MON-01 |
| MON-04 | Heater module 在動，但手臂不在外面（或 LL 狀態不明） | Heater 軸立刻 STOP |
| MON-05 | LL 粗抽中（共用 Dry Pump） | Turbo 前級閥、Chamber 粗抽閥暫關；前級閥被暫關超過 IL_FORELINE_OFF_MAX_S → 停 LL 粗抽 |

## 四、PM 內部

| 編號 | 動作 | 條件 | 實作位置 |
| --- | --- | --- | --- |
| PM-10 | Chamber vent | Turbo 未在轉速、Gate valve 已關 | 槽 3 板內 + IPC |
| PM-11 | Turbo gate valve 開 | Dry pump 運轉、無警報、Foreline 壓力 OK（TODO） | 槽 3 板內 |
| PM-12 | RF on | Lid / Lid cover 關、RF 線到位 | IPC + 硬體線 |
| PM-13 | 加熱器 | 過溫開關未動作 | 槽 2 板內 + 硬體切電源 |

## 五、待確認

- 開 SV 壓力條件 2026-11-11 改為「LL、PM 都 < 1 Torr」（使用者決定，`IL_SV_MODE = BELOW`）。若之後 LL 加 mTorr 級壓力計，可切回壓差模式（`DIFF`）。
- PM 互鎖壓力計：預設 Chamber APGX-H（槽 3 AI 3）。Barocel 滿刻度 1000 Torr，1 Torr 以下量不準，不拿來判斷開 SV；型號選單見 `gauges.md`。
