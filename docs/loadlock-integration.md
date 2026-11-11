# Loadlock（SLK 模組）對接

來源：`C:\Users\user\Desktop\Cloude\Single loadlock project to 原子科技\SLK_Firmware_ManualMode`
（`PROTOCOL_v2.md`、`INTERLOCK_FLOW.md`、`Arduino/SLK_main/*`，2026-11-10 讀取）

## 一、現況重點

- SLK 是 **Arduino Mega + MANUAL_MODE 韌體**：`interlock_bypass_active()` 永遠回傳 true，**所有互鎖都不擋**，收到什麼就做什麼。
- SLK 自己控制：Slit valve（SY7220 雙線圈，開 / 關到位感測）、LL 粗抽閥、LL 洩壓閥、手臂（步進，36.84 step/mm，PM 約 380 mm）、Z 軸氣缸（Indexer）、艙蓋開關、Wafer 感測器、LL 壓力計（4–20 mA，下限 1 Torr）。
- 通訊：RS-232 115200 8N1，ASCII 一行一個指令（`>SEQ:LOAD\n`），回傳 `TLM:` / `EVT:`。**TLM 不會自己推送**，要送 `>SYS:ST` 才回一筆。
- SLK 只看得到 **LL 自己的壓力**，不知道 PM 壓力、PM 是否在製程 → 跨系統條件一定要由我們這邊判斷。

## 二、決定

- **SLK 維持現狀、全開放，韌體不改。**（2026-11-10）
- **Loadlock 的 RS-485 接在第 2 階主控板**，LL-01~08 互鎖由主控板執行；IPC 透過主控板下命令。（2026-11-11，取代原本「IPC 直接接 RS-485」）
  理由：互鎖跟 PM 狀態在同一顆 MCU、反應 < 10 ms；IPC 當機或重開不影響 Loadlock 卡控；IPC 不用管序列埠。

## 三、接線

| 項目 | 說明 |
| --- | --- |
| 主控板 UART | 第 3 組 UART（`HAL_UART_LOADLOCK`）+ RS-485 收發器（例如 THVD1450，DE/RE 由 MCU 控制），115200 8N1 |
| SLK 端 | 目前是 RS-232（Serial2 / Serial3 的 RS232-TTL 模組）。中間加一顆**自動收發切換**的 RS-232 ↔ RS-485 轉換器，SLK 韌體不用改 |
| 埠鎖定 | SLK 會鎖定第一個收到合法指令的埠，機台運轉時 SLK 的其他埠（USB、樹莓派 GUI）不要接 |
| 半雙工 | 主控板每 200 ms 送一次 `>SYS:ST`，命令與輪詢由同一個閘道排隊送出，不會撞在一起 |

## 四、軟體流程

| 位置 | 檔案 | 作用 |
| --- | --- | --- |
| 主控板 | `firmware/main_board/app/slk_link.c/.h` | 組 SLK 指令、解析 TLM / EVT（TLM 缺任何安全欄位就整筆丟掉） |
| 主控板 | `firmware/main_board/app/ll_gateway.c/.h` | 每 200 ms 輪詢、收 IPC 命令 → 互鎖 → 送出 → 等完成事件、把狀態放進 IR LL_* |
| 主控板 | `firmware/main_board/app/interlock.c` | LL-01~08 + PM 側規則（真正執行） |
| IPC | `Ald.Comm/LoadlockService.cs` | 畫面唯一的入口：預檢 → 寫 LL_CMD → 等 LL_CMD_RESULT；把 IR LL_* 填進畫面快照 |
| IPC | `Ald.Core/LoadlockCommands.cs`、`Ald.Core/Interlocks.cs` | 預檢用（跟主控板同規則，只為了立刻顯示原因） |

命令交握（暫存器見 `shared/modbus/register-map.md` 槽 0）：

1. IPC 寫 HR `LL_CMD`（命令碼）、`LL_CMD_ARG`（ARM_GOTO 步數）、`LL_CMD_SEQ` = 上次 + 1（一次 FC16 寫完）。
2. 主控板看到 `LL_CMD_SEQ` 改變才處理一次：互鎖不過 → `LL_CMD_RESULT = DENIED`，原因在 IR `IL_DENY` / `IL_DENY2`；
   上一個動作還沒完成 → `BUSY`；送出 → `SENT`，收到完成事件 → `DONE`，失敗事件或逾時 70 s → `ERROR`。
   處理完把 `LL_CMD_ACK` 設成該 SEQ。沒有完成事件的命令（STATUS、粗抽 / 洩壓閥開關）送出即 `DONE`。
3. 主控板開機第一次看到 HR 時只記下目前的 SEQ、不執行，避免重開機後執行到舊命令。IPC 第一次下命令時從 `LL_CMD_ACK` 接續序號。
4. ESTOP、ABORT 不受「上一個還沒完成」與互鎖限制，隨時可送；送出後前一個動作視為失敗。
5. 主控板收到的 TLM 超過 `IL_LL_STALE_MS`（預設 500 ms）沒更新 → Loadlock 狀態不明（`LL_LINK.TLM_FRESH = 0`）→ 所有 Loadlock 動作與 PM 製程都擋，PM 在跑則觸發 MON-01。

命令對照：

| 命令 | SLK 指令 | 互鎖 |
| --- | --- | --- |
| Status / EStop / Reset / Abort | `>SYS:ST` / `>SYS:ES` / `>SYS:RST` / `>SEQ:ABORT` | 不擋 |
| Recover | `>SYS:RCV` | LL-05（會自動 Homing） |
| Pump / Vent | `>SEQ:PUMP` / `>SEQ:VENT` | LL-07 / LL-08 |
| Load / Unload | `>SEQ:LOAD` / `>SEQ:UNLOAD` | LL-03 |
| XferDone | `>SEQ:XFER`（會關 SV） | LL-02 |
| SvOpen / SvClose | `>VLV:SV:O` / `>VLV:SV:C` | LL-01 / LL-02 |
| RoughOn / RoughOff | `>VLV:ROUGH:1` / `0` | LL-07 / 不擋 |
| VentOn / VentOff | `>VLV:VENT:1` / `0` | LL-08 / 不擋 |
| ArmHome / ArmAutoHome / ArmToLoadlock | `>ARM:HOME` / `>ARM:AHOME` / `>ARM:LL` | LL-05 |
| ArmToPm | `>ARM:PM` | LL-04 |
| ArmGoto | `>ARM:GOTO:<steps>` | 超過安全位置 LL-04，否則 LL-05 |
| ZUp / ZDown | `>Z:UP` / `>Z:DN` | LL-06 |

## 五、SLK 這邊發現的風險（只記錄，SLK 不改；由主控板補強的方式寫在每點後面）

1. **TLM 輸出會干擾馬達脈衝**：`CHANGES_2026-08.md` 記錄過 `println` 阻塞 `loop()` 造成馬達頓挫，已改非阻塞（緩衝不夠就丟 TLM）。
   主控板 200 ms 輪詢比原本 GUI 的 1 s 頻繁，**要實機確認手臂移動是否頓挫**；若有，把 `ll_gateway.h` 的 `LL_POLL_MS` 調到 500 ms~1 s（`IL_LL_STALE_MS` 要一起調大），並靠完成事件判斷。
2. **Z 下降到位的判斷不一致**：`PROTOCOL_v2.md` 一處寫 D11 真感測器，另一處與 `motor.h` 寫「無感測、5 s 計時推估」。
   手臂橫移的防撞（LL-04 / LL-05）靠這個訊號，**請確認 Z DOWN 是否有實體感測器**。
3. **SLK 看門狗 60 s**（MANUAL_MODE 為了手動調機拉長）：主控板當機時 SLK 60 s 內不會自己停，正在跑的序列會跑完。
   靠 EMO 迴路補足。
4. **開 SV 的壓力門檻**：已改為 LL 與 PM 壓差 ≤ 50 mTorr（HR `IL_SV_DP_MAX_MTORR`，`machine.json` 可調）。SLK 壓力計下限 1 Torr 量不到，需加 mTorr 級壓力計，未加之前 SV 開不了（安全方向）。
5. **重複的 I/O 定義**：舊版 ALD 定義表裡真空板有 Loadlock vent / rough、Slit valve，馬達板有 Loadlock 軸、Indexer。
   已從 ALD 執行板移除，以 SLK 為準。**若實際配線不是這樣請告訴我。**
6. **SLK 只看得到 LL 壓力**：PM 壓力、PM 是否在製程，SLK 都不知道，所以 LL-01 / LL-03 的 PM 條件由主控板判斷。
