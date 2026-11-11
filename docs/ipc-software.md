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

`ProcessRecipe`：抽真空（Dry pump → 粗抽到 crossover → Turbo 到速 → 開 GV → base pressure）→ 確認溫度
→ 通氣穩定 → ALD（步驟表下載到製程板，時序由執行板 1 kHz 計時器跑）→ 製程結束（關氣）→（可選）回填。

- **加熱是常駐設定**（手動操作頁設好就不動，製程中不升降溫，2026-11-11 使用者確認）：配方的 `heaterSpDegC` 是「需要的溫度」，
  流程**只檢查、不改加熱設定**。開始時已在 ±容差內就直接往下；不在（例如剛開加熱）就等它進範圍並維持 `heatSettleS`，
  超過 `heatTimeoutS` 失敗並提示到手動頁設定。人機在執行前會先比對配方溫度與加熱板目前設定，對不上就不給跑。
- 手動頁顯示的加熱設定是從加熱板讀回的值（IPC 重開也看得到）。
- **常駐設定存檔與斷電恢復**（`Ald.Core.HeaterPreset`，檔案 config `heaters.presetPath`，預設 `data/heaters.json`）：
  手動頁每次「開啟 / 套用」「關閉此組」「全部關閉」成功後存檔。斷電 / ald-ecat 重開後加熱板設定會變 0，
  IPC 連上（讀回加熱板設定）後跟常駐設定比對，不一樣就在畫面頂部顯示黃條（列出哪幾組、常駐多少、目前多少）+ 藍燈 + 事件記錄。
  **不自動恢復**（SEMI S2：復電不自動重新加熱）：操作員以上按「恢復常駐加熱設定」才只把不一致的組寫回；
  工程師可按「以目前為準」把常駐設定改成加熱板現在的樣子。比對容差 0.05 °C（設定解析度 0.1 °C）。從沒設定過（沒有檔案）不比對。

- 每一步都有逾時；失敗 / 中止 / 進安全狀態 → 停 ALD、關前驅物閥與 MFC，**不自動 vent**，加熱與真空維持。
- 保護不靠流程引擎：主控板與執行板的互鎖一直有效，IPC 當掉也一樣。

## 3b. 傳片流程（`TransferSequencer`）與晶圓位置（`WaferMap`）

用 SLK 單步命令組成，每一步等狀態回來（不用 SLK 的 `SEQ:LOAD`：它在 PM 內固定等 2 秒就退手臂，沒跟 Wafer lift 交握）。

| 送片 LL→PM | 取片 PM→LL |
| --- | --- |
| Heater module 回下位、Wafer lift 下 | Heater module 回下位（片落回 lift pin）、Wafer lift 上 |
| （Z 啟用時）LL Z 下降 → 關 Turbo GV → 關 LL 粗抽閥 → 開 SV → 手臂進 PM | 同左 |
| **Wafer lift 上（從手臂接片）** → 記錄：片在 PM | **Wafer lift 下（片放到手臂）** → 記錄：片在 LL |
| 手臂退回 → 關 SV → 恢復 Turbo GV（原本開著才開）→ Heater module 到 Process 位 | 手臂退回 → 關 SV → 恢復 Turbo GV |

- 執行前檢查：各板在線、非安全狀態、沒在跑 ALD / LL 序列、晶圓位置符合（送片要 LL 有片 + PM 無片）、PM 與 LL 都 < 1 Torr、LL 洩壓閥 / 艙蓋關。
- 每步逾時（config `transfer.*`）；被互鎖擋下會顯示主控板的原因。
- 失敗 / 中止：SLK ABORT 停手臂，**不自動往回動**；交接途中（手臂在 PM 內）停下時晶圓位置設為「不明」，
  要工程師看過實物在畫面設定後才能再傳片。
- 晶圓位置存 `transfer.waferMapPath`（JSON，重開機讀回）。目前沒有晶圓感測器（WAFER_PRESENT 待接）。
- Heater module Process 位置 `transfer.heaterProcessSteps`（預設 20000 step，**實機待確認**），到位以 HEATER_PROCESS 感測器為準。

## 3c. 自動跑片（`AutoRunJob`）

Recipe 頁「自動跑片」：準備（LL 抽氣、PM 抽到 base）→ 送片 → 製程（不 vent）→ 取片 →（勾「結束後回填」時）PM 回填。
任一段失敗 / 中止就整個停下，停在該段的安全處理，**不會自動取片或 vent**（片在哪由晶圓位置記錄顯示）。
需要：Loadlock 手臂上有片、PM 無片。「只跑製程」給片已在 PM 或空跑時用。

## 3c-2. 多片批次（`BatchJob`）

單 Loadlock、手臂一次一片，所以每片之間要人員換片。批次頁排好「晶圓編號 + 配方」清單後：

1. Loadlock 洩壓（等 SLK 序列跑完、壓力回到 ≥ 1 Torr）。
2. **藍燈閃 + 提示框**：「取出上一片、放入 Wxx，關好上蓋後按確認」。按確認時上蓋還開著會再提示一次。
3. 自動跑片（LL 抽氣 → 送片 → 製程 → 取片），各片配方的「結束後回填」不做，PM 保持真空給下一片。
4. 下一片重複 1～3；最後一片做完洩壓、提示取片；勾「整批結束後 PM 回填」才回填。

- 任一片失敗或中止：整批停下，後面的片標「等待」不會跑；失敗會紅燈閃 + 蜂鳴（同流程失敗）。
- 開始條件：PM 確認無片、Loadlock 位置不是不明、晶圓編號不重複、配方檢查通過、不在安全狀態。
- 晶圓編號「加入」時會自動往下編（W01 → W02、LOT7-09 → LOT7-10）。
- 權限：開始 / 確認 = 操作員；中止任何人。等人員時綠燈熄、藍燈閃。

## 3d. 漏率測試（`LeakTestSequencer`）與保養計數（`Maintenance` / `MaintenanceStore`）

**漏率測試（壓升法 rate-of-rise）**，保養頁「開始漏率測試」（工程師）：

1. 關 ALD 閥、MFC 與上游閥。
2. 不在 `leakTest.startBelowTorr`（預設 5E-5 Torr）以下就先抽真空（用 ProcessSequencer 的抽真空段）。
3. 再抽 `pumpS` 秒 → **關 Turbo GV 隔離 PM**（Turbo 保持全速）→ 等 `settleS` 秒。
4. 記錄 `measureS` 秒壓力（預設 Ion gauge / WRG），最小平方法直線擬合：
   壓升速率 mTorr/min = 斜率 × 60000；漏氣量 Q（Torr·L/s）= 斜率 × `chamberVolumeL`；R² 看是不是直線。
5. 開回 Turbo GV。結果存 SQLite `leak_test`，並寫一筆事件記錄。

- 壓升 ≤ `limitMtorrPerMin` 合格；量測中超過 `abortAboveTorr` 立刻結束、判大漏。
- 量到的是**真漏 + 壁面放氣**：剛開腔、溫度不同時會偏高，比較請固定溫度與抽氣時間。曲線往下彎（R² 偏低）通常是放氣。
- 壓力計：Barocel 1000 Torr 滿刻度解析度不夠，APGX 互鎖暫存器只到 1 mTorr，所以預設用 WRG（精度約 ±30 %，看趨勢夠用）。
  要做準確的驗收，建議加一顆 0.1 或 1 Torr 的電容式真空計，`leakTest.gauge` 改成它。
- 中止 / 失敗：關氣體，Turbo 還在全速就把 GV 開回去（主控板互鎖會再檢查），不會 vent。

**保養計數**：每筆狀態（MachineService.Updated）算增量，只看主控板回報的**實際狀態**：

| 項目 | 計法 |
| --- | --- |
| Dry pump / Turbo 運轉時數 | 運轉中（Turbo 為全速）累計；兩筆間隔 > 5 s（斷線）不算 |
| Turbo GV、Slit valve、PM 回填、手臂伸入 PM、Wafer lift、Heater module 升到 Process | 開啟 / 到位的上升緣次數 |
| ALD 製程、ALD cycle | AldRunning 上升緣、AldCycle 增量 |
| ALD V1~V7 動作次數 | cycle 增量 × 該步驟表每 cycle 每顆閥從關到開的次數（整個 cycle 都開的不算） |

- 每項有「保養後累計」（保養完成按歸零）與「總累計」（不歸零）；提醒上限 0 = 不提醒，**請依各零件原廠手冊設定**（預設都不提醒）。
- 超過上限：事件記錄一筆 Warning、保養頁頁籤顯示「保養 ⚠n」，只提醒一次（歸零或改上限後重新判斷，重開程式不重複提醒）。
- 增量先放記憶體，每 60 秒與關程式時寫回 SQLite `maint`，斷電最多少算一分鐘。歸零 / 改上限記在事件記錄。

## 3e. 燈塔 / 蜂鳴器與警報確認（`Ald.Core.Tower`、`AlarmManager.Acknowledge`）

4 色燈塔（紅 / 黃 / 綠 / 藍），依 SEMI S2 慣例：

| 燈 | 亮法 | 意思 |
| --- | --- | --- |
| 紅 | 閃 + 蜂鳴 | 有**未確認**的異常（Alarm 等級警報、執行中流程失敗；已自己消失但沒人確認過的也算） |
| 紅 | 常亮 | 異常已確認但還在，或系統在安全狀態 |
| 黃 | 閃 | 有未確認的警告（Warning） |
| 黃 | 常亮 | 警告已確認但還在；或待機（沒有流程、沒有異常，`tower.idleYellow`） |
| 綠 | 常亮 | 流程執行中（配方、自動跑片、傳片、漏率測試） |
| 藍 | 閃 | 需要人員處理：自動跑片 / 取片完成等人取片、晶圓位置不明 |

- 頂部「確認 / 消音」：所有警報標為已確認、蜂鳴器停、清掉已消失的異常與流程失敗 / 藍燈提示；**任何人都能按**（同安全停止，不鎖），記在事件記錄。
  有未確認項目時按鈕會閃橘色。警報頁以「●」標示未確認。
- 開機寬限：程式啟動後第一次連上的 10 秒內自己消失的異常（各板陸續上線）不留存，避免一開機就叫。
- 開始前檢查沒過（按鈕條件不符）不算異常，不會叫。
- 分工：IPC 決定燈號與閃爍，寫主控板 HR `DO_CMD`（有變化才寫）；**主控板保底**：安全狀態一定亮紅燈，
  IPC 斷線（看門狗）時其他燈與蜂鳴器關掉、只留紅燈。實際輸出回報在 IR `DO_OUT`，頂部小圓點顯示的是這個回報值。
- 主控板 DO 通道：0 傳送許可、1 蜂鳴器、2 紅、3 黃、4 綠、5 藍（`main_app.c`，實際腳位待 HAL）。
- 設定（config `tower`）：`idleYellow`、`buzzOnWarning`（警告也叫，預設否）、`buzzerIntermittent`（斷續叫，預設是）、`blinkMs`。

## 4. 人機（Ald.Hmi）

- **總覽**：真空、Loadlock、16 組加熱、8 路 MFC、PM 機構 / ALD 一頁看完。
- **警報**：目前警報 + 事件記錄（SQLite `alarm`）。事件記錄可選時間範圍（1 小時～30 天）、等級、關鍵字（比對來源與訊息，
  不分大小寫，`%` `_` 當一般字）查詢；「自動更新」每 2 秒用目前條件重查；畫面最多列 2000 筆；「匯出 CSV」不限筆數，存到 `export/`。
- **手動操作**：真空閥 / 泵（勾選＝命令，命令與實際不同或被真空板擋下會列出來）、ALD V1~V7、
  加熱（單組設定 / 升溫速率，數字框上限 = config `limitDegC`）、MFC（含上游閥）、Heater module 移動 / 原點、Wafer lift。
  命令被拒會在狀態列顯示原因；主控板最後擋下的原因也會顯示。**流程執行中整頁鎖住**。
- **傳片**：LL / PM 有沒有片（綠 = 有、灰 = 無、橘 = 不明）、送片 / 取片 / 中止、不能按時列出原因、步驟記錄；
  工程師可在確認實物後修正晶圓位置。傳片與配方流程互斥，傳片中手動操作頁與 Loadlock 按鈕鎖住。
- **Recipe**：從配方資料夾（`--recipes`，預設程式旁 `recipes/`）選檔 → 表單編常用欄位 → 檢查 / 存檔 / 執行 / 中止，
  右邊顯示步驟記錄。ALD 步驟一行一步：`名稱, 閥, ms`，閥寫 `V1+V3`、`-`（全關）或數字。
  逾時等進階欄位直接改 JSON，存檔會保留。存檔先寫暫存檔再取代（斷電不壞原檔）。
- **趨勢**：讀 SQLite `trend` 表；上圖壓力（對數）、下圖溫度 / MFC，左邊勾選訊號；10 分鐘～7 天；
  「即時更新」每 2 秒重畫，關掉後可滑鼠拖曳 / 滾輪縮放；超過 4000 點自動抽樣；「匯出 CSV」存到資料庫旁 `export/`（全部欄位，Excel 可直接開）。
  圖表元件 ScottPlot（MIT）。
- **登入 / 權限**（`Ald.Core.Access` 權限表、`Ald.Data.UserStore` 帳號）：

  | 等級 | 可以做 |
  | --- | --- |
  | 檢視（未登入） | 看畫面、**安全停止 / 中止流程（永遠不鎖）** |
  | 操作員 | + Reset、執行配方、抽真空 / 回填、Loadlock、傳片 |
  | 工程師 | + 手動操作、編輯 / 存檔配方、修正晶圓位置、漏率測試、保養計數歸零 / 上限 |
  | 管理員 | + 使用者管理 |

  密碼只存 PBKDF2-SHA256 雜湊。第一次開機建立 `admin`，出廠密碼由 config `hmi.defaultAdminPasswordHash` 決定
  （**只放雜湊不放明碼**；換出廠密碼：`AldChamber --hash-password 新密碼`，把輸出貼進 config）。
  admin 還是出廠密碼時登入會提醒改。至少留一個管理員；`hmi.idleLogoutMinutes`（預設 30）沒操作自動登出。
  登入 / 登出 / 帳號異動 / 每個操作都記在事件記錄（來源「操作:帳號」）。
- **履歷**（`Ald.Data.RunLog`）：Recipe 頁「只跑製程 / 自動跑片」（晶圓編號欄位選填）與批次的每一片各記一筆：
  開始 / 結束時間、種類、晶圓編號、配方名稱、批次編號（`B年月日-時分秒`）、結果、失敗原因、操作人，**配方內容整份存 JSON**。
  可依時間 / 結果 / 關鍵字（晶圓編號、配方、批次、訊息、使用者）查，選一筆看當時的配方，匯出 CSV。
  總覽的「抽真空 / 回填」不記。程式被關掉或斷電留下的「執行中」下次開機標成 Interrupted（中斷）並記一筆警告。
- **批次**：排片清單（配方、晶圓編號、片數）、開始 / 中止、等人員時藍色提示框 + 確認按鈕、每片結果與耗時、步驟記錄。
- **保養**：左邊漏率測試（量測時間、合格上限可臨時改；即時畫壓升點與擬合線；歷史記錄），右邊保養計數
  （選項目 → 設提醒上限 / 保養完成歸零）。漏率測試與配方 / 傳片互斥，執行中手動操作頁鎖住。
- 配方格式（`RecipeFile`）：一個 JSON = 一個 `ProcessRecipe`，範例 `recipes/example_al2o3.json`；
  `recipes/sim_quick_test.json` 是給模擬器用的短配方（10 倍速約 35 秒跑完）。

## 5. 記錄（SQLite，`dataLog.path`）

- `alarm`：警報發生 / 解除、互鎖擋下原因（中文）、流程步驟、操作紀錄。
- `trend`：每 `dataLog.intervalMs` 一筆寬表（各壓力、16 組溫度與輸出、8 路 MFC、閥狀態、ALD cycle、Heater 位置、SV）。
- `runs`：跑片履歷（見上）。
- `maint`：保養計數（保養後累計、總累計、提醒上限、上次保養時間 / 人）。`leak_test`：漏率測試結果。
- WAL 模式（斷電不壞），保存 `keepDays` 天。

## 6. 測試

| 指令 | 內容 |
| --- | --- |
| `make -C firmware/tests` | 韌體邏輯單元測試（主控板 188、執行板 40） |
| `make -C sim` | 整機模擬整合測試 42 項：開機上線、Reset、抽真空、開 SV 互鎖、Turbo GV 聯動、加熱上限、馬達、心跳中斷 |
| `make -C ipc/ecat-master test` | ald-ecat bridge（含 HR 預設值） |
| `cd ipc && dotnet test` | C# 125 項（含配方檔、帳號 / 權限、趨勢、事件記錄查詢 / 匯出、跑片履歷、加熱常駐設定比對 / 恢復、傳片、自動跑片、多片批次、漏率測試、保養計數、燈號規則 / 警報確認），含**端到端**：MachineService ↔ ald-ecat --sim（20 倍速）跑完整段配方、送片 + 取片、從大氣自動跑片、兩片批次、漏率測試、燈塔（安全狀態強制紅燈）、心跳中斷進安全狀態 |

## 待做

- RF（鎧泰 600 W / 300 W）協議確認後：主控板 UART 驅動 + 模擬。
- 晶圓感測器（WAFER_PRESENT）接上後與 WaferMap 比對；Heater module Process 位置實機確認。
- STM32 HAL（`hal_port_stm32.c`）：等腳位分配。
- 漏率測試：實機 PM 體積、合格上限；是否加低量程電容式真空計。保養提醒上限依各零件原廠手冊填。
