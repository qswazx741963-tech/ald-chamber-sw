# Modbus 暫存器表（map_version 1）

> 自動產生，請勿手改。來源：shared/modbus/registers.yaml

## board1_process（Slave ID 1，COM1）

製程控制（ALD 閥、MFC、腔體壓力計）

### Input Register (FC04)

| 位址 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| IR 0 | BOARD_ID | u16 | 1 |  |  |  |  |
| IR 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則 IPC 拒絕連線 |
| IR 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK |
| IR 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 10 | DI | u16 | 1 |  |  |  | bits: 0=RF1_POWER_CABLE_OK, 1=RF1_MATCH_CABLE_OK |
| IR 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=ALD_V1, 1=ALD_V2, 2=ALD_V3, 3=ALD_V4, 4=ALD_V5, 5=ALD_V6, 6=MFC_UP_V1, 7=MFC_UP_V2, 8=MFC_UP_V3, 9=MFC_UP_V4, 10=MFC_UP_V5, 11=MFC_UP_V6 |
| IR 30–35 | MFC_PV | u16 | 6 | 0.1 | sccm |  |  |
| IR 36 | MANOMETER_RAW | u16 | 1 |  | mV |  | 原始電壓，換算在 IPC |
| IR 40 | ALD_CYCLE_NOW | u16 | 1 |  |  |  |  |
| IR 41 | ALD_STEP_NOW | u16 | 1 |  |  |  |  |

### Holding Register (FC03/FC16)

| 位址 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| HR 0 | HEARTBEAT | u16 | 1 |  |  |  | IPC 每次輪詢 +1 |
| HR 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=ALD_V1, 1=ALD_V2, 2=ALD_V3, 3=ALD_V4, 4=ALD_V5, 5=ALD_V6, 6=MFC_UP_V1, 7=MFC_UP_V2, 8=MFC_UP_V3, 9=MFC_UP_V4, 10=MFC_UP_V5, 11=MFC_UP_V6 |
| HR 20–25 | MFC_SP | u16 | 6 | 0.1 | sccm | 0–1000 |  |
| HR 100 | ALD_CYCLES | u16 | 1 |  |  | 1–60000 |  |
| HR 101 | ALD_STEP_COUNT | u16 | 1 |  |  | 1–16 |  |
| HR 110–125 | ALD_STEP_VALVES | u16 | 16 |  |  |  | 每步要開的 ALD 閥位元（bit0 = ALD_V1） |
| HR 130–145 | ALD_STEP_MS | u16 | 16 |  | ms | 1–60000 |  |

## board2_heater（Slave ID 2，COM2）

加熱器與熱電偶（12 組，板內 PID）

### Input Register (FC04)

| 位址 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| IR 0 | BOARD_ID | u16 | 1 |  |  |  |  |
| IR 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則 IPC 拒絕連線 |
| IR 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK |
| IR 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 10 | DI | u16 | 1 |  |  |  | bits: 0=OT_LID_CONE, 1=OT_CHAMBER_LID, 2=OT_CHAMBER_WALL |
| IR 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=LID_CONE, 1=CHAMBER_LID, 2=CHAMBER_WALL, 3=SUSCEPTOR_UP, 4=SUSCEPTOR_LOW, 5=AMPOULE1, 6=AMPOULE2, 7=GASLINE1, 8=GASLINE2, 9=FORELINE, 10=SPARE1, 11=SPARE2 |
| IR 30–41 | TC_TEMP | i16 | 12 | 0.1 | degC |  |  |
| IR 50–61 | HEATER_DUTY | u16 | 12 | 0.1 | % |  |  |

### Holding Register (FC03/FC16)

| 位址 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| HR 0 | HEARTBEAT | u16 | 1 |  |  |  | IPC 每次輪詢 +1 |
| HR 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 10 | HEATER_ENABLE | u16 | 1 |  |  |  | bits: 0=LID_CONE, 1=CHAMBER_LID, 2=CHAMBER_WALL, 3=SUSCEPTOR_UP, 4=SUSCEPTOR_LOW, 5=AMPOULE1, 6=AMPOULE2, 7=GASLINE1, 8=GASLINE2, 9=FORELINE, 10=SPARE1, 11=SPARE2 |
| HR 20–31 | HEATER_SP | u16 | 12 | 0.1 | degC | 0–300 | TODO 各組上限 |
| HR 40–51 | HEATER_RAMP | u16 | 12 | 0.1 | degC/min | 0–50 |  |
| HR 100–111 | PID_KP | u16 | 12 | 0.01 |  |  |  |
| HR 120–131 | PID_KI | u16 | 12 | 0.001 |  |  |  |
| HR 140–151 | PID_KD | u16 | 12 | 0.01 |  |  |  |

## board3_vacuum（Slave ID 3，COM2）

壓力 / 真空、閥件、泵（板內互鎖）

### Input Register (FC04)

| 位址 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| IR 0 | BOARD_ID | u16 | 1 |  |  |  |  |
| IR 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則 IPC 拒絕連線 |
| IR 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK |
| IR 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 10 | DI | u16 | 1 |  |  |  | bits: 0=CHAMBER_LID_CLOSED, 1=LID_COVER_CLOSED, 2=LOADLOCK_LID_CLOSED, 3=TURBO_GV_OPENED, 4=TURBO_GV_CLOSED, 5=SLIT_OPENED, 6=SLIT_CLOSED, 7=INDEXER_UP, 8=INDEXER_DOWN, 9=DRYPUMP_RUNNING, 10=DRYPUMP_ALARM, 11=TURBO_AT_SPEED, 12=TURBO_ALARM |
| IR 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=TURBO_GV, 1=TURBO_ROUGH, 2=CHAMBER_ROUGH, 3=CHAMBER_VENT, 4=LOADLOCK_VENT, 5=LOADLOCK_ROUGH, 6=SLIT_OPEN, 7=SLIT_CLOSE, 8=DRYPUMP_ON, 9=TURBO_ON |
| IR 21 | DO_BLOCKED | u16 | 1 |  |  |  | IPC 有下命令但被板內互鎖擋下的閥 bits: 0=TURBO_GV, 1=TURBO_ROUGH, 2=CHAMBER_ROUGH, 3=CHAMBER_VENT, 4=LOADLOCK_VENT, 5=LOADLOCK_ROUGH, 6=SLIT_OPEN, 7=SLIT_CLOSE, 8=DRYPUMP_ON, 9=TURBO_ON |
| IR 30–37 | AI_RAW | u16 | 8 |  | mV |  | 0 CDA, 1 冷卻水流量, 2 Ion gauge, 3 Chamber convectron, 4 Foreline convectron, 5 Loadlock convectron, 6-7 Spare |

### Holding Register (FC03/FC16)

| 位址 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| HR 0 | HEARTBEAT | u16 | 1 |  |  |  | IPC 每次輪詢 +1 |
| HR 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=TURBO_GV, 1=TURBO_ROUGH, 2=CHAMBER_ROUGH, 3=CHAMBER_VENT, 4=LOADLOCK_VENT, 5=LOADLOCK_ROUGH, 6=SLIT_OPEN, 7=SLIT_CLOSE, 8=DRYPUMP_ON, 9=TURBO_ON |

## board4_motion（Slave ID 4，COM2）

馬達、升降、感測器（3 軸：0 Susceptor, 1 Wafer lift, 2 Loadlock）

### Input Register (FC04)

| 位址 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| IR 0 | BOARD_ID | u16 | 1 |  |  |  |  |
| IR 1 | FW_VERSION | u16 | 1 |  |  |  | 例如 0x0102 = v1.2 |
| IR 2 | MAP_VERSION | u16 | 1 |  |  |  | 必須等於 map_version，否則 IPC 拒絕連線 |
| IR 3 | STATUS | u16 | 1 |  |  |  | bits: 0=READY, 1=RUNNING, 2=SAFE_STATE, 3=WATCHDOG_TRIPPED, 4=PARAM_REJECTED, 5=LOCAL_INTERLOCK |
| IR 4 | ALARM_BITS | u16 | 1 |  |  |  | 各板自訂警報位元（TODO 定義） |
| IR 5 | HEARTBEAT_ECHO | u16 | 1 |  |  |  | 回傳最後收到的 HEARTBEAT |
| IR 6 | BOARD_COUNTER | u16 | 1 |  |  |  | 板內主迴圈計數，確認板子沒當機 |
| IR 7 | LAST_ERROR | u16 | 1 |  |  |  |  |
| IR 10 | DI | u16 | 1 |  |  |  | bits: 0=SUSCEPTOR_HOME, 1=SUSCEPTOR_PROCESS, 2=WAFER_LIFT_HOME, 3=WAFER_LIFT_UP, 4=LOADLOCK_HOME, 5=WAFER_PRESENT |
| IR 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=WAFER_LIFT_UP, 1=WAFER_LIFT_DOWN, 2=INDEXER_UP, 3=INDEXER_DOWN |
| IR 30–35 | AXIS_POS | i32 | 3 |  | step |  |  |
| IR 40–42 | AXIS_STATE | u16 | 3 |  |  |  | 0 idle, 1 moving, 2 homing, 3 error |

### Holding Register (FC03/FC16)

| 位址 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| HR 0 | HEARTBEAT | u16 | 1 |  |  |  | IPC 每次輪詢 +1 |
| HR 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=WAFER_LIFT_UP, 1=WAFER_LIFT_DOWN, 2=INDEXER_UP, 3=INDEXER_DOWN |
| HR 20–25 | AXIS_TARGET | i32 | 3 |  | step |  |  |
| HR 30–32 | AXIS_SPEED | u16 | 3 |  | step/s | 1–20000 |  |
| HR 40–42 | AXIS_CMD | u16 | 3 |  |  |  | 值: 0=NONE, 1=MOVE, 2=HOME, 3=STOP |
