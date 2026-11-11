# 系統架構（方案 B：EtherCAT，2026-11-11 定案）

> 2026-11-10 原定方案 A（IPC ↔ 主控板 Modbus TCP、IPC Windows + WPF）；2026-11-11 改為方案 B。
> 只換「IPC ↔ 主控板」這一段與 IPC 作業系統，背板、執行板、互鎖都不變。

## 一、三階架構

```
【第一階】研華 ARK-1222 工業電腦（Debian Linux + PREEMPT_RT）
   ├─ 人機 AldChamber（C# .NET 8 + Avalonia）：畫面 / Recipe / 資料記錄 / 警報 / 互鎖預檢
   │     │ 本機 Modbus TCP 127.0.0.1:502（位址與主控板相同）
   ├─ ald-ecat（C，IgH EtherLAB 主站，1 ms SCHED_FIFO 即時執行緒）  ipc/ecat-master
   └─ LAN2 ── 屏蔽網路線（點對點直連）── EtherCAT
          │                                     LAN1 → 廠務網路（以後 SECS/GEM）
【第二階】主控板（自製，STM32H743 + LAN9252 EtherCAT 從站 + FreeRTOS）
   ├─ UART：RF1 / RF2（預計鎧泰科技 600 W / 300 W）；Throttle valve 預留（目前不用）
   ├─ UART（RS-485）── Loadlock（SLK 模組，韌體不改、全開放；互鎖在這裡做）
   ├─ 硬體輸出：SAFE 線、燈塔、蜂鳴器（TRANSFER_PERMIT 預留，目前不接 Loadlock）
   └─ 背板：CAN FD（5 Mbps）+ SAFE 線 + SYNC 線 + 槽位腳位 SLOT_ID[3:0]
          │
【第三階】執行板（自製，全部 STM32G474，同一份韌體，依板型跳線決定角色）
   ├─ 槽 1 製程板：ALD 閥 ×7、MFC 上游閥 ×8、MFC / 壓力控制器 ×8（Bronkhorst）、腔體壓力計、RF 線檢知
   ├─ 槽 2 加熱板：16 組 SSR / K 型熱電偶（14 用 + 2 預留）、4 個過溫開關、各組溫度上限
   ├─ 槽 3 真空板：Turbo GV、粗抽 / vent 閥、Dry pump、Turbo、真空計
   ├─ 槽 4 馬達板：Heater module 軸、Wafer lift 氣缸
   └─ 槽 5~8 預留擴充（執行板最多 8 塊）
```

> Loadlock 的閥、Slit valve、手臂、Indexer 由 **SLK 模組**自己控制，SLK 維持現狀不改。
> 主控板經 RS-485 控制 SLK，所有互鎖在主控板做；IPC 透過主控板下 Loadlock 命令（見 `loadlock-integration.md`）。ALD 的真空板 / 馬達板不接這些點。

## 二、誰負責什麼

| 層 | 負責 | 不做 |
| --- | --- | --- |
| IPC | 畫面、Recipe 編輯與下載、資料記錄、警報紀錄、操作權限、Loadlock 操作畫面、所有動作先預檢並顯示被擋原因 | 任何需要 ms 等級反應的事 |
| 主控板 | **全部跨系統互鎖**（Loadlock LL-01~08 + PM 側）、**Loadlock RS-485 閘道**、TV / RF 閘道、兩層看門狗、安全狀態總指揮、把各槽資料整理成一張表 | 直接驅動閥件（交給執行板） |
| 執行板 | 實際 I/O、ALD 閥時序（1 kHz 計時器）、加熱 PID、板內互鎖、自己進安全狀態 | 跨板判斷 |

## 三、通訊

| 段 | 方式 | 週期 | 說明 |
| --- | --- | --- | --- |
| 人機 ↔ ald-ecat（同一台 IPC） | 本機 Modbus TCP，Unit ID 1，127.0.0.1:502 | 50 ms | 一張表、依槽位分區：絕對位址 = 槽號 × 200 + 區內位址（`shared/modbus/register-map.md`） |
| ald-ecat ↔ 主控板 | EtherCAT，RxPDO 0x1600 = 所有 HR（162 word）、TxPDO 0x1A00 = 所有 IR（117 word） | 1 ms | 對應表 `pdo_map.h`、從站描述檔 `shared/ethercat/ald_mainboard_esi.xml`，都由 registers.yaml 產生 |
| 主控板 ↔ 執行板 | CAN FD 背板 | 10 ms | 週期性整份複製 IR / HR，掉一包下一輪補回（`backplane.md`） |
| 主控板 ↔ Loadlock | RS-485 115200 ASCII（SLK 端 RS-232 + 轉換器） | 200 ms 輪詢 TLM | `loadlock-integration.md` |
| IPC → 主控板（Loadlock 命令） | HR LL_CMD / LL_CMD_ARG / LL_CMD_SEQ → IR LL_*（經 EtherCAT） | 有命令時 | SEQ 改變才執行一次 |
| 主控板 ↔ RF | 待確認（預計鎧泰科技 600 W / 300 W） | TODO | 依設備手冊；Throttle valve 目前製程不需要，UART 與暫存器位址保留 |

**IPC 寫的是「請求」，主控板送出去的才算數**：IPC 寫進表裡的命令，主控板會先過互鎖，不准的位元清掉才轉給執行板，
被擋下的原因寫在主控板 IR `IL_DENY`。

## 四、EtherCAT 設計重點

- **只打包有定義的暫存器**：LAN9252 process RAM 4 KB，SyncManager 3-buffer 要佔 3 倍，所以 PDO 不送整個 200 位址槽位空間。
  產生器會檢查放不放得下、單一 PDO 不超過 254 個 entry。
- **一次性命令（COMMAND）**：PDO 每 1 ms 重送，所以主控板只在「值改變」時才收；ald-ecat 送 20 ms 後自動清回 NONE。
- **看門狗**：人機當掉 → 心跳停 → 主控板 IPC 看門狗；ald-ecat 當掉 / 網路斷 → 主控板 SM 看門狗、掉出 OP → `link_up()` = false。兩種都進安全狀態。
- **授權**：IgH 主站 GPLv2（核心模組）+ LGPL（libethercat），ald-ecat 動態連結可不開源，出貨需提供 IgH 原始碼；
  主控板 SSC 由 ETG 會員免費取得；Avalonia MIT。全套免授權費。出貨前要向 ETG 申請 Vendor ID（目前 0 為暫用）。
- **分層**：控制邏輯只讀寫 `g_ir` / `g_hr`。主控板通訊層 `fieldbus_ecat.c`（備用 Modbus TCP 也還在介面上），
  IPC 端人機只認本機 Modbus，主站細節都在 ald-ecat。
- Linux 安裝與即時設定見 `linux-ipc-setup.md`。

## 五、穩定度原則

1. **時序在最底層**：ALD 閥脈衝由執行板 1 kHz 計時器中斷執行，開始時複製步驟表、累計計時不漂移。
2. **三層互鎖**：硬體線 → 主控板 → IPC（`interlocks.md`）。危險氣體相關一定要有硬體那一層。
3. **兩層看門狗**：IPC → 主控板（HR HEARTBEAT）、主控板 → 執行板（背板 SYNC）。斷線就進安全狀態（`safe-state.md`）。
4. **開機即安全狀態**，要 IPC 連上並下 RESET_ALARM 才解除。
5. **版本握手**：每塊板回報板型、韌體版本、map_version，不符就拒用。
6. **MCU 韌體不用動態記憶體**，開硬體看門狗（IWDG）。
7. **邏輯與硬體分離**：`app/` 只透過 `hal_port.h` 碰硬體，所以能在 PC 上跑單元測試（`firmware/tests`）。
