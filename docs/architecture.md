# 系統架構（方案 A，2026-11-10 定案）

## 一、三階架構

```
【第一階】研華 ARK-1222 工業電腦（Windows，C# .NET 8 + WPF）
   ├─ HMI 畫面 / Recipe / 資料記錄 / 警報
   └─ LAN2 ── 屏蔽網路線（點對點直連，不經過 Switch）── Modbus TCP
          │                                     LAN1 → 廠務網路（以後 SECS/GEM）
【第二階】主控板（自製，STM32H743 + 內建乙太網路 + LwIP + FreeRTOS）
   ├─ UART：Loadlock（SLK 模組，RS-232）、Throttle valve、RF1 / RF2
   ├─ 硬體輸出：TRANSFER_PERMIT 繼電器、SAFE 線、燈塔、蜂鳴器
   └─ 背板：CAN FD（5 Mbps）+ SAFE 線 + SYNC 線 + 槽位腳位 SLOT_ID[3:0]
          │
【第三階】執行板（自製，全部 STM32G474，同一份韌體，依板型跳線決定角色）
   ├─ 槽 1 製程板：ALD 閥 ×6、MFC ×6、腔體壓力計、RF 線檢知
   ├─ 槽 2 加熱板：12 組 SSR / 熱電偶、3 個過溫開關
   ├─ 槽 3 真空板：Turbo GV、粗抽 / vent 閥、Dry pump、Turbo、真空計
   ├─ 槽 4 馬達板：Susceptor 軸、Wafer lift
   └─ 槽 5~15 預留擴充
```

> Loadlock 的閥、Slit valve、手臂、Indexer 由 **SLK 模組**自己控制（見 `loadlock-integration.md`），
> ALD 這邊的真空板 / 馬達板**不再**接這些點。

## 二、誰負責什麼

| 層 | 負責 | 不做 |
| --- | --- | --- |
| IPC | 畫面、Recipe 編輯與下載、資料記錄、警報紀錄、操作權限、第三層互鎖（先擋、顯示原因） | 任何需要 ms 等級反應的事 |
| 主控板 | 跨板 / 跨系統互鎖（第二層）、Loadlock / TV / RF 閘道、兩層看門狗、安全狀態總指揮、把各槽資料整理成一張表 | 直接驅動閥件（交給執行板） |
| 執行板 | 實際 I/O、ALD 閥時序（1 kHz 計時器）、加熱 PID、板內互鎖、自己進安全狀態 | 跨板判斷 |

## 三、通訊

| 段 | 方式 | 週期 | 說明 |
| --- | --- | --- | --- |
| IPC ↔ 主控板 | Modbus TCP，Unit ID 1，port 502 | 50 ms | 一張表、依槽位分區：絕對位址 = 槽號 × 200 + 區內位址（`shared/modbus/register-map.md`） |
| 主控板 ↔ 執行板 | CAN FD 背板 | 10 ms | 週期性整份複製 IR / HR，掉一包下一輪補回（`backplane.md`） |
| 主控板 ↔ Loadlock | RS-232 115200 ASCII | 200 ms 輪詢 TLM | `loadlock-integration.md` |
| 主控板 ↔ TV / RF | RS-232 / RS-485 | TODO | 依設備手冊 |

**IPC 寫的是「請求」，主控板送出去的才算數**：IPC 寫進表裡的命令，主控板會先過互鎖，不准的位元清掉才轉給執行板，
被擋下的原因寫在主控板 IR `IL_DENY`。

## 四、保留 EtherCAT 擴充

- 主控板電路板預留 LAN9252 的位置與 SPI 走線。
- 韌體分層：`fieldbus_if.h` 是通訊層介面，控制邏輯只讀寫 `g_ir` / `g_hr` 兩張表。
  以後新增 `fieldbus_ethercat.c`，把同一張表對應成 PDO，控制邏輯不用改。
- IPC 端：`Ald.Comm/MainBoardLink.cs` 是唯一碰網路的地方，換 EtherCAT 主站只換這一層。

## 五、穩定度原則

1. **時序在最底層**：ALD 閥脈衝由執行板 1 kHz 計時器中斷執行，開始時複製步驟表、累計計時不漂移。
2. **三層互鎖**：硬體線 → 主控板 → IPC（`interlocks.md`）。危險氣體相關一定要有硬體那一層。
3. **兩層看門狗**：IPC → 主控板（HR HEARTBEAT）、主控板 → 執行板（背板 SYNC）。斷線就進安全狀態（`safe-state.md`）。
4. **開機即安全狀態**，要 IPC 連上並下 RESET_ALARM 才解除。
5. **版本握手**：每塊板回報板型、韌體版本、map_version，不符就拒用。
6. **MCU 韌體不用動態記憶體**，開硬體看門狗（IWDG）。
7. **邏輯與硬體分離**：`app/` 只透過 `hal_port.h` 碰硬體，所以能在 PC 上跑單元測試（`firmware/tests`）。
