# 真空計型號與換算

來源：使用者提供的原廠資料（2026-11-11）
- Edwards APGX-H 對流式：資料表 3601-0114-01、手冊 D02392880
- Edwards WRG200 寬範圍（Pirani + 冷陰極）：資料表 3601-0764-01、手冊 D2G0000880
- Edwards Barocel 7000 系列電容式：型錄 3601-0551-01、手冊 W601100880（7025m）
- Leybold TURBOVAC 350 iR：手冊 301011470（Turbo 介面，見最後一節）

## 一、型號選單

`config/machine.json` 的 `gauges` 區塊每顆壓力計選一個 `model`。主控板互鎖用的那一顆（`pmInterlockGauge`）
在啟動時寫進主控板 HR `PM_GAUGE_MODEL` / `PM_GAUGE_AI_ADDR` / `PM_GAUGE_FS_MTORR`，主控板自己換算（`firmware/common/gauge.c`），
結果放在 IR `PM_PRES_MTORR` / `PM_GAUGE_STATUS`。IPC 顯示用同一張表（`ipc/src/Ald.Core/Gauges.cs`）。

| 編號 | model | 型號 | 公式（V = 輸出電壓） | 有效範圍 | 故障電壓（→ 壓力未知） |
| --- | --- | --- | --- | --- | --- |
| 0 | None | 未使用 | — | — | — |
| 1 | Linear | 電容式 0–10 V（Barocel 7000 等） | P = V / 10 × 滿刻度 | 0 – 9.9 V（≥ 9.9 V 視為飽和） | 無（見下方注意） |
| 2 | ApgxH | Edwards APGX-H | P(Torr) = 10^(V − 6.125) | 1.9 – 9.125 V | < 1.8 V、> 9.2 V（9.5 燈絲斷 / 9.6 校正錯 / 9.7 無管） |
| 3 | Wrg200X | Edwards WRG200 標準輸出 | P(Torr) = 10^((V − 8.083) / 0.667) | 2.0 – 10.0 V | < 1.5 V（0.9 EEPROM / 1.0 Pirani / 1.2 Striker / 1.3 未點火） |
| 4 | Wrg200G | WRG200 matched | P(Torr) = 10^((V − 6.875) / 0.6) | 1.4 – 8.6 V | < 1.0 V、> 9.5 V（10.2 Pirani 故障） |
| 5 | Wrg200Q | WRG200 matched | P(Torr) = 10^((V − 5.5) / 0.5) | 1.5 – 6.9375 V | < 1.4 V |

介於故障電壓與有效範圍之間：低於下限 = UNDER_RANGE（算「已知、很低」），高於上限 = OVER_RANGE（互鎖當成壓力未知）。
WRG200 的輸出型號看料號最後的 Output 碼：0 = 標準（Wrg200X）、2 = 1.4–8.6 V（G）、4 = 1.5–6.9375 V（Q）。

**新增型號**：`gauge.c` 表加一列 → `registers.yaml` 的 `PM_GAUGE_MODEL` 加同編號 → `Gauges.cs` 加 → 兩邊單元測試各加一個手冊上的對照點。

## 二、硬體注意（執行板類比輸入）

1. **輸入範圍要到 10.6 V 以上**：WRG200 滿量程 10 V、Barocel 輸出最高 10.24 V，ADC 前端建議做 0–12 V。
2. **電容式（Linear）斷線會讀成 0 V = 完美真空**，軟體分不出來。類比輸入加一顆弱上拉（例如 1 MΩ 拉到 +12 V）：
   壓力計接著時輸出阻抗 10 Ω，上拉影響可忽略；斷線時輸入被拉高 → 讀成飽和 → 互鎖當成壓力未知（安全方向）。
   APGX-H / WRG200 斷線讀 0 V 本來就落在故障電壓，不用另外處理。
3. APGX-H 電源地（pin 2）與訊號地（pin 5）不要短接；WRG200 / Barocel 的訊號地也走差動或獨立回線。

## 三、Loadlock 壓力（SLK）

SLK 原本的壓力計 0–1000 Torr，低於 1 Torr 回報 -1（主控板 IR `LL_FLAGS.PRES_UNDER_RANGE`）。
4–20 mA 斷線也可能讀成「低於下限」，所以主控板（`ll_gateway.c`）用前後讀值判斷，以下情況標成 `PRES_SUSPECT`、不採信：
- 開機後還沒看過有效讀值（≥ 1 Torr）
- 最後一個有效讀值 > `IL_LL_JUMP_MTORR`（預設 10 Torr）就直接變成低於下限（例如 760 Torr 突然變 -1）
- 洩壓閥或艙蓋開著，卻還是讀低於下限

一旦有有效讀值就恢復。**副作用**：主控板重開機時 LL 已經在真空，要先讓 LL 回到 1 Torr 以上（短暫 vent）再抽一次，SV 才開得了。

## 四、Turbo（Leybold TURBOVAC 350 iR）介面摘要

REMOTE X1（15 pin）：pin 8 Start（> 10 V = 啟動）、pin 10 Standby、pin 11 Cooling/Venting valve、
pin 3/4/5 Normal operation 繼電器（≥ 90 % 設定轉速）、pin 1/2/9 Error 繼電器、pin 14/15 Warning 繼電器、pin 12 類比輸出（預設轉速 0–10 V）。
真空板目前用 DI `TURBO_AT_SPEED`（接 Normal operation 繼電器）與 `TURBO_ALARM`（接 Error 繼電器），pin 6 訊號地與 pin 13 類比地要分開。
選配 Anybus 模組有 RS-485 / Profibus / **EtherCAT**，之後可以直接掛在 EtherCAT 線上讀轉速、電流、溫度。
