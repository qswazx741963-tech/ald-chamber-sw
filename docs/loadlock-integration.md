# Loadlock（SLK 模組）對接

來源：`C:\Users\user\Desktop\Cloude\Single loadlock project to 原子科技\SLK_Firmware_ManualMode`
（`PROTOCOL_v2.md`、`INTERLOCK_FLOW.md`、`Arduino/SLK_main/*`，2026-11-10 讀取）

## 一、現況重點

- SLK 是 **Arduino Mega + MANUAL_MODE 韌體**：`interlock_bypass_active()` 永遠回傳 true，**所有互鎖都不擋**，收到什麼就做什麼。
- SLK 自己控制：Slit valve（SY7220 雙線圈，開 / 關到位感測）、LL 粗抽閥、LL 洩壓閥、手臂（步進，36.84 step/mm，PM 約 380 mm）、Z 軸氣缸（Indexer）、艙蓋開關、Wafer 感測器、LL 壓力計（4–20 mA，下限 1 Torr）。
- 通訊：RS-232 115200 8N1，ASCII 一行一個指令（`>SEQ:LOAD\n`），回傳 `TLM:` / `EVT:`。**TLM 不會自己推送**，要送 `>SYS:ST` 才回一筆。
- SLK 只看得到 **LL 自己的壓力**，不知道 PM 壓力、PM 是否在製程 → 跨系統條件一定要由我們這邊判斷。

## 二、接線建議

| 項目 | 建議 |
| --- | --- |
| 通訊 | 主控板 UART（RS-232 收發器）→ SLK `Serial2` 或 `Serial3`。SLK 會鎖定第一個收到合法指令的埠，**其他埠（例如 USB、樹莓派 GUI）在機台運轉時不要接** |
| TRANSFER_PERMIT | 主控板一個乾接點，**串在 SLK 的 SV「開」線圈電源上**。主控板判斷不准開 SV 時，就算有人從 SLK 的 GUI 下開指令也開不了。關線圈不要串（永遠可以關） |
| SAFE / EMO | SLK 的馬達驅動器致能、閥件電源也串 EMO 迴路 |

## 三、軟體流程（`firmware/main_board/app/ll_gateway.c`）

1. 主控板每 200 ms 送 `>SYS:ST`，解析 TLM 更新狀態；超過 500 ms 沒有新 TLM → 視為「狀態不明」，所有 Loadlock 相關動作與 PM 製程開始都擋下。
2. IPC 要操作 Loadlock：寫主控板 HR `LL_CMD`、`LL_CMD_ARG`，最後 `LL_CMD_SEQ` +1。
3. 主控板依命令判斷互鎖動作（`ll_cmd_action`）→ `il_check` → 通過才轉成 ASCII 送出。
4. 結果在 IR `LL_CMD_RESULT`：SENT（已送出等完成事件）、DONE、DENIED（原因在 `IL_DENY`）、ERROR、BUSY（上一個還沒完成）。
5. 完成判斷用 SLK 的完成事件（例如 `EVT:VLV:SV_OPEN`、`EVT:SEQ:DONE`），ESTOP / SEQ:ERR / STALL / 警報視為失敗。
6. ESTOP、ABORT 不受 BUSY 限制，隨時可送。

命令對照（HR `LL_CMD` 值 → SLK 指令）：

| 值 | 命令 | SLK 指令 | 互鎖 |
| --- | --- | --- | --- |
| 1 | STATUS | `>SYS:ST` | 不擋 |
| 2 | ESTOP | `>SYS:ES` | 不擋 |
| 3 | RESET | `>SYS:RST` | 不擋 |
| 4 | RECOVER | `>SYS:RCV` | LL-05（會自動 Homing） |
| 10 / 11 | PUMP / VENT | `>SEQ:PUMP` / `>SEQ:VENT` | LL-07 / LL-08 |
| 12 / 13 | LOAD / UNLOAD | `>SEQ:LOAD` / `>SEQ:UNLOAD` | LL-03 |
| 14 | ABORT | `>SEQ:ABORT` | 不擋 |
| 15 | XFER_DONE | `>SEQ:XFER`（會關 SV） | LL-02 |
| 20 / 21 | SV_OPEN / SV_CLOSE | `>VLV:SV:O` / `>VLV:SV:C` | LL-01 / LL-02 |
| 22 / 23 | ROUGH_ON / OFF | `>VLV:ROUGH:1` / `0` | LL-07 / 不擋 |
| 24 / 25 | VENT_ON / OFF | `>VLV:VENT:1` / `0` | LL-08 / 不擋 |
| 30 / 31 | ARM_HOME / AHOME | `>ARM:HOME` / `>ARM:AHOME` | LL-05 |
| 32 / 33 | ARM_LL / ARM_PM | `>ARM:LL` / `>ARM:PM` | LL-05 / LL-04 |
| 34 | ARM_GOTO | `>ARM:GOTO:<steps>` | 超過安全位置 LL-04，否則 LL-05 |
| 35 / 36 | Z_UP / Z_DOWN | `>Z:UP` / `>Z:DN` | LL-06 |

## 四、SLK 這邊發現的風險（建議之後修 SLK 韌體）

1. **TLM 輸出會干擾馬達脈衝**：`CHANGES_2026-08.md` 記錄過 `println` 阻塞 `loop()` 造成馬達頓挫，已改非阻塞（緩衝不夠就丟 TLM）。
   主控板 200 ms 輪詢比 GUI 的 1 s 頻繁，**要實機確認手臂移動是否頓挫**；若有，移動中可降到 500 ms 並靠完成事件判斷。
   根本解法：SLK 改成用計時器中斷產生步進脈衝。
2. **Z 下降到位的判斷不一致**：`PROTOCOL_v2.md` 一處寫 D11 真感測器，另一處與 `motor.h` 寫「無感測、5 s 計時推估」。
   手臂橫移的防撞靠這個訊號，**請確認 Z DOWN 是否有實體感測器**。若沒有，建議加裝。
3. **SLK 看門狗 60 s**（MANUAL_MODE 為了手動調機拉長）：主控板當機時 SLK 60 s 內不會自己停。
   靠 TRANSFER_PERMIT 硬體繼電器 + EMO 迴路補足；量產版建議改回 1~2 s。
4. **開 SV 的壓力門檻**：SLK 原設計 IL-03 是 LL < 10 Torr，我們這邊預設改成 LL ≤ 2 Torr 且 PM ≤ 1 Torr（`HR IL_SV_*` 可調），請依實際抽氣能力確認。
5. **重複的 I/O 定義**：舊版 ALD 定義表裡真空板有 Loadlock vent / rough、Slit valve，馬達板有 Loadlock 軸、Indexer。
   這次已從 ALD 執行板移除，以 SLK 為準。**若實際配線不是這樣請告訴我。**
