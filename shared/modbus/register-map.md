# 暫存器表（map_version 2）

> 自動產生，請勿手改。來源：shared/modbus/registers.yaml

IPC 透過 Modbus TCP 連主控板（Unit ID 1）。**絕對位址 = 槽號 × 200 + 區內位址**。執行板韌體只用區內位址。

| 槽 | 板 | 板型 | IR 範圍 | HR 範圍 |
| --- | --- | --- | --- | --- |
| 0 | main | MAIN | 0–45 | 0–154 |
| 1 | process | PROCESS | 200–250 | 200–345 |
| 2 | heater | HEATER | 400–461 | 400–551 |
| 3 | vacuum | VACUUM | 600–637 | 600–610 |
| 4 | motion | MOTION | 800–841 | 800–841 |

## 槽 0：main

主控板（系統狀態、跨板互鎖、Throttle valve / RF 閘道、Loadlock 狀態鏡像）

### Input Register (FC04)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IR 0 | 0 | BOARD_TYPE | u16 | 1 |  |  |  | 板型（見 board_types），與槽位設定不符時主控板拒用該槽 |
| IR 1 | 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 2 | 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則拒絕連線 |
| IR 3 | 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK, 6=ONLINE |
| IR 4 | 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 5 | 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 6 | 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 7 | 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 8 | 8 | SLOT_ID | u16 | 1 |  |  |  | 背板腳位讀到的槽號 |
| IR 10 | 10 | DI | u16 | 1 |  |  |  | bits: 0=SAFE_LINE_OK, 1=EMO_OK, 2=CDA_OK, 3=TRANSFER_PERMIT_OUT |
| IR 11 | 11 | SLOT_ONLINE | u16 | 1 |  |  |  | bit n = 槽 n 在線且板型/版本正確 |
| IR 12 | 12 | SYS_STATE | u16 | 1 |  |  |  | 值: 0=BOOT, 1=SAFE, 2=IDLE, 3=PROCESS, 4=TRANSFER, 5=ALARM |
| IR 13 | 13 | IL_DENY | u16 | 1 |  |  |  | 最近一次被主控板互鎖擋下的原因（可能多個同時成立） bits: 0=SAFE_ACTIVE, 1=SLOT_OFFLINE, 2=LL_UNKNOWN, 3=LL_ALARM, 4=SV_NOT_CLOSED, 5=SV_NOT_OPEN, 6=LL_PRESSURE_HIGH, 7=PM_PRESSURE_HIGH, 8=PM_BUSY, 9=PM_VENTING, 10=ARM_IN_SV_PATH, 11=Z_NOT_DOWN, 12=LL_LID_OPEN, 13=LL_VALVE_CONFLICT, 14=PM_NOT_AT_TRANSFER, 15=LL_BUSY |
| IR 14 | 14 | IL_DENY_ACTION | u16 | 1 |  |  |  | 最近一次被擋下的動作（IL_ACT_*，見 docs/interlocks.md） |
| IR 30 | 30 | TV_PRESSURE | u16 | 1 | 0.001 | Torr |  |  |
| IR 31 | 31 | TV_POSITION | u16 | 1 | 0.1 | % |  |  |
| IR 32 | 32 | TV_STATUS | u16 | 1 |  |  |  |  |
| IR 40–41 | 40–41 | RF_FWD_W | u16 | 2 |  | W |  |  |
| IR 42–43 | 42–43 | RF_REF_W | u16 | 2 |  | W |  |  |
| IR 44–45 | 44–45 | RF_STATUS | u16 | 2 |  |  |  |  |

### Holding Register (FC03/FC06/FC16)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HR 0 | 0 | HEARTBEAT | u16 | 1 |  |  |  | 上層每次輪詢 +1（IPC→主控板、主控板→執行板各一層） |
| HR 1 | 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 2 | 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 10 | 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=BUZZER, 1=TOWER_RED, 2=TOWER_YELLOW, 3=TOWER_GREEN |
| HR 20 | 20 | TV_MODE | u16 | 1 |  |  |  | 值: 0=OPEN, 1=CLOSE, 2=POSITION, 3=PRESSURE |
| HR 21 | 21 | TV_SP_POSITION | u16 | 1 | 0.1 | % | 0–100 |  |
| HR 22 | 22 | TV_SP_PRESSURE | u16 | 1 | 0.001 | Torr | 0–10 |  |
| HR 30 | 30 | RF_ON | u16 | 1 |  |  |  | bits: 0=RF1, 1=RF2 |
| HR 31–32 | 31–32 | RF_SP_W | u16 | 2 |  | W | 0–600 | TODO 依 RF 電源規格 |
| HR 100 | 100 | LL_M_SEQ | u16 | 1 |  |  |  | IPC 每收到一筆新 TLM +1 |
| HR 101 | 101 | LL_M_SV | u16 | 1 |  |  |  | Slit valve 0 UNKNOWN,1 CLOSED,2 OPENING,3 OPEN,4 CLOSING,5 ERROR |
| HR 102 | 102 | LL_M_Z | u16 | 1 |  |  |  | Z 軸 0 UNKNOWN,1 DOWN,2 RAISING,3 UP,4 LOWERING |
| HR 103 | 103 | LL_M_ARM | u16 | 1 |  |  |  | 手臂狀態 0 IDLE,1 HOMING,2 READY,3 MOVING,4 AT_POS,5 Z_RAISING,6 Z_UP,7 Z_LOWERING,99 ERROR |
| HR 104–105 | 104–105 | LL_M_POS | i32 | 1 |  | step |  | 手臂位置（36.84 step/mm） |
| HR 106 | 106 | LL_M_ALM | u16 | 1 |  |  |  | SLK 警報碼，0 = 正常 |
| HR 107 | 107 | LL_M_FLAGS | u16 | 1 |  |  |  | bits: 0=ROUGH_OPEN, 1=VENT_OPEN, 2=LID_OPEN |
| HR 108–109 | 108–109 | LL_M_PRES | i32 | 1 |  | mTorr |  | -1 = 低於量測下限（1 Torr） |
| HR 150 | 150 | IL_SV_LL_MAX_TORR | u16 | 1 | 0.01 | Torr | 0–10 | 開 SV 前 Loadlock 壓力上限，預設 2.00（SLK 原為 10）TODO |
| HR 151 | 151 | IL_SV_PM_MAX_TORR | u16 | 1 | 0.01 | Torr | 0–10 | 開 SV 前 PM 壓力上限，預設 1.00 TODO |
| HR 152 | 152 | IL_LL_STALE_MS | u16 | 1 |  | ms | 100–5000 | LL_M_SEQ 超過此時間沒更新 → 視為 Loadlock 狀態不明，預設 500 |
| HR 153–154 | 153–154 | IL_ARM_SAFE_POS | i32 | 1 |  | step |  | 手臂 ≤ 此位置才算退出 SV 路徑，預設 5894（160 mm） |

## 槽 1：process

製程控制（ALD 閥、MFC、腔體壓力計、RF 線檢知）

### Input Register (FC04)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IR 200 | 0 | BOARD_TYPE | u16 | 1 |  |  |  | 板型（見 board_types），與槽位設定不符時主控板拒用該槽 |
| IR 201 | 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 202 | 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則拒絕連線 |
| IR 203 | 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK, 6=ONLINE |
| IR 204 | 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 205 | 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 206 | 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 207 | 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 208 | 8 | SLOT_ID | u16 | 1 |  |  |  | 背板腳位讀到的槽號 |
| IR 210 | 10 | DI | u16 | 1 |  |  |  | bits: 0=RF1_POWER_CABLE_OK, 1=RF1_MATCH_CABLE_OK |
| IR 220 | 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=ALD_V1, 1=ALD_V2, 2=ALD_V3, 3=ALD_V4, 4=ALD_V5, 5=ALD_V6, 6=MFC_UP_V1, 7=MFC_UP_V2, 8=MFC_UP_V3, 9=MFC_UP_V4, 10=MFC_UP_V5, 11=MFC_UP_V6 |
| IR 230–235 | 30–35 | MFC_PV | u16 | 6 | 0.1 | sccm |  |  |
| IR 236 | 36 | MANOMETER_RAW | u16 | 1 |  | mV |  | 原始電壓，換算在 IPC |
| IR 240 | 40 | ALD_CYCLE_NOW | u16 | 1 |  |  |  |  |
| IR 241 | 41 | ALD_STEP_NOW | u16 | 1 |  |  |  |  |
| IR 250 | 50 | WAVE_SEQ | u16 | 1 |  |  |  | 壓力波形緩衝序號，每完成一個 cycle +1（波形本體走另外的區塊讀取，TODO） |

### Holding Register (FC03/FC06/FC16)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HR 200 | 0 | HEARTBEAT | u16 | 1 |  |  |  | 上層每次輪詢 +1（IPC→主控板、主控板→執行板各一層） |
| HR 201 | 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 202 | 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 210 | 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=ALD_V1, 1=ALD_V2, 2=ALD_V3, 3=ALD_V4, 4=ALD_V5, 5=ALD_V6, 6=MFC_UP_V1, 7=MFC_UP_V2, 8=MFC_UP_V3, 9=MFC_UP_V4, 10=MFC_UP_V5, 11=MFC_UP_V6 |
| HR 220–225 | 20–25 | MFC_SP | u16 | 6 | 0.1 | sccm | 0–1000 |  |
| HR 300 | 100 | ALD_CYCLES | u16 | 1 |  |  | 1–60000 |  |
| HR 301 | 101 | ALD_STEP_COUNT | u16 | 1 |  |  | 1–16 |  |
| HR 302 | 102 | PURGE_VALVES | u16 | 1 |  |  |  | 安全狀態下保持開啟的閥（bit0 = ALD_V1），例如 N2 purge。TODO 依管路 |
| HR 310–325 | 110–125 | ALD_STEP_VALVES | u16 | 16 |  |  |  | 每步要開的 ALD 閥位元（bit0 = ALD_V1） |
| HR 330–345 | 130–145 | ALD_STEP_MS | u16 | 16 |  | ms | 1–60000 |  |

## 槽 2：heater

加熱器與熱電偶（12 組，板內 PID）

### Input Register (FC04)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IR 400 | 0 | BOARD_TYPE | u16 | 1 |  |  |  | 板型（見 board_types），與槽位設定不符時主控板拒用該槽 |
| IR 401 | 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 402 | 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則拒絕連線 |
| IR 403 | 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK, 6=ONLINE |
| IR 404 | 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 405 | 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 406 | 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 407 | 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 408 | 8 | SLOT_ID | u16 | 1 |  |  |  | 背板腳位讀到的槽號 |
| IR 410 | 10 | DI | u16 | 1 |  |  |  | bits: 0=OT_LID_CONE, 1=OT_CHAMBER_LID, 2=OT_CHAMBER_WALL |
| IR 420 | 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=LID_CONE, 1=CHAMBER_LID, 2=CHAMBER_WALL, 3=SUSCEPTOR_UP, 4=SUSCEPTOR_LOW, 5=AMPOULE1, 6=AMPOULE2, 7=GASLINE1, 8=GASLINE2, 9=FORELINE, 10=SPARE1, 11=SPARE2 |
| IR 430–441 | 30–41 | TC_TEMP | i16 | 12 | 0.1 | degC |  |  |
| IR 450–461 | 50–61 | HEATER_DUTY | u16 | 12 | 0.1 | % |  |  |

### Holding Register (FC03/FC06/FC16)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HR 400 | 0 | HEARTBEAT | u16 | 1 |  |  |  | 上層每次輪詢 +1（IPC→主控板、主控板→執行板各一層） |
| HR 401 | 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 402 | 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 410 | 10 | HEATER_ENABLE | u16 | 1 |  |  |  | bits: 0=LID_CONE, 1=CHAMBER_LID, 2=CHAMBER_WALL, 3=SUSCEPTOR_UP, 4=SUSCEPTOR_LOW, 5=AMPOULE1, 6=AMPOULE2, 7=GASLINE1, 8=GASLINE2, 9=FORELINE, 10=SPARE1, 11=SPARE2 |
| HR 420–431 | 20–31 | HEATER_SP | u16 | 12 | 0.1 | degC | 0–300 | TODO 各組上限 |
| HR 440–451 | 40–51 | HEATER_RAMP | u16 | 12 | 0.1 | degC/min | 0–50 |  |
| HR 460–471 | 60–71 | HOLD_SP | u16 | 12 | 0.1 | degC | 0–300 | 安全狀態時的保溫溫度（避免前驅物在管路冷凝），0 = 安全狀態關閉該組。TODO 依製程 |
| HR 500–511 | 100–111 | PID_KP | u16 | 12 | 0.01 |  |  |  |
| HR 520–531 | 120–131 | PID_KI | u16 | 12 | 0.001 |  |  |  |
| HR 540–551 | 140–151 | PID_KD | u16 | 12 | 0.01 |  |  |  |

## 槽 3：vacuum

PM 壓力 / 真空、閥件、泵（板內互鎖）。Loadlock 的閥、Slit valve 由 SLK 模組控制

### Input Register (FC04)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IR 600 | 0 | BOARD_TYPE | u16 | 1 |  |  |  | 板型（見 board_types），與槽位設定不符時主控板拒用該槽 |
| IR 601 | 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 602 | 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則拒絕連線 |
| IR 603 | 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK, 6=ONLINE |
| IR 604 | 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 605 | 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 606 | 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 607 | 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 608 | 8 | SLOT_ID | u16 | 1 |  |  |  | 背板腳位讀到的槽號 |
| IR 610 | 10 | DI | u16 | 1 |  |  |  | bits: 0=CHAMBER_LID_CLOSED, 1=LID_COVER_CLOSED, 2=TURBO_GV_OPENED, 3=TURBO_GV_CLOSED, 4=DRYPUMP_RUNNING, 5=DRYPUMP_ALARM, 6=TURBO_AT_SPEED, 7=TURBO_ALARM |
| IR 620 | 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=TURBO_GV, 1=TURBO_ROUGH, 2=CHAMBER_ROUGH, 3=CHAMBER_VENT, 4=DRYPUMP_ON, 5=TURBO_ON |
| IR 621 | 21 | DO_BLOCKED | u16 | 1 |  |  |  | 有下命令但被板內互鎖擋下的閥 bits: 0=TURBO_GV, 1=TURBO_ROUGH, 2=CHAMBER_ROUGH, 3=CHAMBER_VENT, 4=DRYPUMP_ON, 5=TURBO_ON |
| IR 630–637 | 30–37 | AI_RAW | u16 | 8 |  | mV |  | 0 CDA, 1 冷卻水流量, 2 Ion gauge, 3 Chamber convectron, 4 Foreline convectron, 5-7 Spare |

### Holding Register (FC03/FC06/FC16)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HR 600 | 0 | HEARTBEAT | u16 | 1 |  |  |  | 上層每次輪詢 +1（IPC→主控板、主控板→執行板各一層） |
| HR 601 | 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 602 | 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 610 | 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=TURBO_GV, 1=TURBO_ROUGH, 2=CHAMBER_ROUGH, 3=CHAMBER_VENT, 4=DRYPUMP_ON, 5=TURBO_ON |

## 槽 4：motion

PM 內馬達與升降（軸 0 Susceptor、軸 1 Wafer lift）。Loadlock 手臂 / Indexer 由 SLK 模組控制

### Input Register (FC04)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IR 800 | 0 | BOARD_TYPE | u16 | 1 |  |  |  | 板型（見 board_types），與槽位設定不符時主控板拒用該槽 |
| IR 801 | 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 802 | 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則拒絕連線 |
| IR 803 | 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK, 6=ONLINE |
| IR 804 | 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 805 | 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 806 | 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 807 | 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 808 | 8 | SLOT_ID | u16 | 1 |  |  |  | 背板腳位讀到的槽號 |
| IR 810 | 10 | DI | u16 | 1 |  |  |  | bits: 0=SUSCEPTOR_HOME, 1=SUSCEPTOR_PROCESS, 2=WAFER_LIFT_HOME, 3=WAFER_LIFT_UP, 4=WAFER_PRESENT |
| IR 820 | 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=WAFER_LIFT_UP, 1=WAFER_LIFT_DOWN |
| IR 830–833 | 30–33 | AXIS_POS | i32 | 2 |  | step |  |  |
| IR 840–841 | 40–41 | AXIS_STATE | u16 | 2 |  |  |  | 0 idle, 1 moving, 2 homing, 3 error |

### Holding Register (FC03/FC06/FC16)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HR 800 | 0 | HEARTBEAT | u16 | 1 |  |  |  | 上層每次輪詢 +1（IPC→主控板、主控板→執行板各一層） |
| HR 801 | 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 802 | 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 810 | 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=WAFER_LIFT_UP, 1=WAFER_LIFT_DOWN |
| HR 820–823 | 20–23 | AXIS_TARGET | i32 | 2 |  | step |  |  |
| HR 830–831 | 30–31 | AXIS_SPEED | u16 | 2 |  | step/s | 1–20000 |  |
| HR 840–841 | 40–41 | AXIS_CMD | u16 | 2 |  |  |  | 值: 0=NONE, 1=MOVE, 2=HOME, 3=STOP |
