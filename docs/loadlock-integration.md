# Loadlock（SLK 模組）對接

來源：`C:\Users\user\Desktop\Cloude\Single loadlock project to 原子科技\SLK_Firmware_ManualMode`
（`PROTOCOL_v2.md`、`INTERLOCK_FLOW.md`、`Arduino/SLK_main/*`，2026-11-10 讀取）

## 一、現況重點

- SLK 是 **Arduino Mega + MANUAL_MODE 韌體**：`interlock_bypass_active()` 永遠回傳 true，**所有互鎖都不擋**，收到什麼就做什麼。
- SLK 自己控制：Slit valve（SY7220 雙線圈，開 / 關到位感測）、LL 粗抽閥、LL 洩壓閥、手臂（步進，36.84 step/mm，PM 約 380 mm）、Z 軸氣缸（Indexer）、艙蓋開關、Wafer 感測器、LL 壓力計（4–20 mA，下限 1 Torr）。
- 通訊：RS-232 115200 8N1，ASCII 一行一個指令（`>SEQ:LOAD\n`），回傳 `TLM:` / `EVT:`。**TLM 不會自己推送**，要送 `>SYS:ST` 才回一筆。
- SLK 只看得到 **LL 自己的壓力**，不知道 PM 壓力、PM 是否在製程 → 跨系統條件一定要由我們這邊判斷。

## 二、決定（2026-11-10，使用者指示）

- **SLK 維持現狀、全開放，韌體不改。**
- **由 ARK-1222 透過 RS-485 直接控制 Loadlock**，互鎖在 ARK-1222 做。主控板不接 Loadlock。

## 三、接線

| 項目 | 說明 |
| --- | --- |
| ARK-1222 COM | 設成 RS-485 模式（TODO 確認 ARK-1222 哪個 COM 支援 RS-485，BIOS / 跳線設定） |
| SLK 端 | 目前是 RS-232（Serial2 / Serial3 的 RS232-TTL 模組）。中間加一顆**自動收發切換**的 RS-232 ↔ RS-485 轉換器，SLK 韌體不用改 |
| 埠鎖定 | SLK 會鎖定第一個收到合法指令的埠，機台運轉時 SLK 的其他埠（USB、樹莓派 GUI）不要接 |
| 半雙工 | RS-485 一來一回，IPC 每 200 ms 送一次 `>SYS:ST`，命令與輪詢共用同一把鎖，不會撞在一起 |

## 四、軟體流程（IPC）

| 檔案 | 作用 |
| --- | --- |
| `Ald.Comm/LoadlockLink.cs` | RS-485 串列埠：收 TLM / EVT、每 200 ms 輪詢 |
| `Ald.Comm/SlkProtocol.cs` | 組指令、解析 TLM、判斷完成事件 |
| `Ald.Core/LoadlockCommands.cs` | 每個命令對應哪條互鎖 |
| `Ald.Core/Interlocks.cs` | LL-01~08 規則 |
| `Ald.Comm/LoadlockService.cs` | **唯一送命令的入口**：互鎖 → 送出 → 等完成事件；並把狀態鏡像寫到主控板 |

1. 畫面按鈕一律呼叫 `LoadlockService.Execute(命令)`，被擋下回傳原因直接顯示。
2. TLM 超過 500 ms 沒更新 → Loadlock 狀態不明 → 所有 Loadlock 動作與 PM 製程開始都擋。
3. 完成判斷用 SLK 的完成事件（`EVT:VLV:SV_OPEN`、`EVT:SEQ:DONE` 等），ESTOP / SEQ:ERR / STALL / 警報視為失敗。
4. ESTOP、ABORT 不受「上一個還沒完成」限制，隨時可送。
5. **鏡像**：每筆 TLM 寫進主控板 HR `LL_M_*` 並把 `LL_M_SEQ` +1。主控板靠它做 PM 側互鎖：SV 沒確定關、手臂沒退出，就不准 ALD / RF / vent；PM 在跑時 SV 不是關的，立刻停。IPC 當機 → 鏡像停止更新 → 主控板視為不明 → 擋 PM 動作（加上 IPC 心跳逾時本來就會進安全狀態）。

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

## 五、SLK 這邊發現的風險（只記錄，SLK 不改；由 IPC 端補強的方式寫在每點後面）

1. **TLM 輸出會干擾馬達脈衝**：`CHANGES_2026-08.md` 記錄過 `println` 阻塞 `loop()` 造成馬達頓挫，已改非阻塞（緩衝不夠就丟 TLM）。
   IPC 200 ms 輪詢比原本 GUI 的 1 s 頻繁，**要實機確認手臂移動是否頓挫**；若有，手臂移動中 `LoadlockLink.PollMs` 降到 500 ms~1 s，並靠完成事件判斷。
2. **Z 下降到位的判斷不一致**：`PROTOCOL_v2.md` 一處寫 D11 真感測器，另一處與 `motor.h` 寫「無感測、5 s 計時推估」。
   手臂橫移的防撞（LL-04 / LL-05）靠這個訊號，**請確認 Z DOWN 是否有實體感測器**。
3. **SLK 看門狗 60 s**（MANUAL_MODE 為了手動調機拉長）：IPC 當機時 SLK 60 s 內不會自己停，正在跑的序列會跑完。
   靠 EMO 迴路補足。
4. **開 SV 的壓力門檻**：SLK 原設計 IL-03 是 LL < 10 Torr，IPC 預設 LL ≤ 2 Torr 且 PM ≤ 1 Torr（`machine.json` interlock 可調），請依實際抽氣能力確認。
5. **重複的 I/O 定義**：舊版 ALD 定義表裡真空板有 Loadlock vent / rough、Slit valve，馬達板有 Loadlock 軸、Indexer。
   已從 ALD 執行板移除，以 SLK 為準。**若實際配線不是這樣請告訴我。**
6. **SLK 只看得到 LL 壓力**：PM 壓力、PM 是否在製程，SLK 都不知道，所以 LL-01 / LL-03 的 PM 條件一定要在 IPC 判斷。
