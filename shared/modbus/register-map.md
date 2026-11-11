# 暫存器表（map_version 2）

> 自動產生，請勿手改。來源：shared/modbus/registers.yaml

IPC 透過 Modbus TCP 連主控板（Unit ID 1）。**絕對位址 = 槽號 × 200 + 區內位址**。執行板韌體只用區內位址。

| 槽 | 板 | 板型 | IR 範圍 | HR 範圍 |
| --- | --- | --- | --- | --- |
| 0 | main | MAIN | 0–126 | 0–168 |
| 1 | process | PROCESS | 200–250 | 200–345 |
| 2 | heater | HEATER | 400–465 | 400–555 |
| 3 | vacuum | VACUUM | 600–637 | 600–610 |
| 4 | motion | MOTION | 800–841 | 800–841 |

## 槽 0：main

主控板（系統狀態、跨板互鎖、Loadlock / Throttle valve / RF 閘道）

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
| IR 13 | 13 | IL_DENY | u16 | 1 |  |  |  | 最近一次被主控板互鎖擋下的原因（可能多個同時成立） bits: 0=SAFE_ACTIVE, 1=SLOT_OFFLINE, 2=LL_UNKNOWN, 3=LL_ALARM, 4=SV_NOT_CLOSED, 5=SV_NOT_OPEN, 6=DP_HIGH, 7=PRESSURE_UNKNOWN, 8=PM_BUSY, 9=PM_VENTING, 10=ARM_IN_SV_PATH, 11=Z_NOT_DOWN, 12=LL_LID_OPEN, 13=LL_VALVE_CONFLICT, 14=PM_NOT_AT_TRANSFER, 15=LL_BUSY |
| IR 14 | 14 | IL_DENY_ACTION | u16 | 1 |  |  |  | 最近一次被擋下的動作（IL_ACT_*，見 docs/interlocks.md） |
| IR 15 | 15 | IL_DENY2 | u16 | 1 |  |  |  | IL_DENY 的延伸（16 個 bit 不夠用） bits: 0=TURBO_GV_OPEN, 1=ARM_MOVING, 2=HEATER_NOT_DOWN |
| IR 30 | 30 | TV_PRESSURE | u16 | 1 | 0.001 | Torr |  |  |
| IR 31 | 31 | TV_POSITION | u16 | 1 | 0.1 | % |  |  |
| IR 32 | 32 | TV_STATUS | u16 | 1 |  |  |  |  |
| IR 40–41 | 40–41 | RF_FWD_W | u16 | 2 |  | W |  |  |
| IR 42–43 | 42–43 | RF_REF_W | u16 | 2 |  | W |  |  |
| IR 44–45 | 44–45 | RF_STATUS | u16 | 2 |  |  |  |  |
| IR 100 | 100 | LL_LINK | u16 | 1 |  |  |  | bits: 0=ONLINE, 1=TLM_FRESH, 2=ONLINE_MODE, 3=IL_BYPASS, 4=PORT_LOCKED |
| IR 101 | 101 | LL_CHM | u16 | 1 |  |  |  | 腔體狀態 0 IDLE,1 PUMPING,2 VAC_READY,3 SV_OPENING,4 TRANSFER_VAC,5 SV_CLOSING,6 VENTING,7 ATM_READY,99 ERROR |
| IR 102 | 102 | LL_ARM | u16 | 1 |  |  |  | 手臂狀態 0 IDLE,1 HOMING,2 READY,3 MOVING,4 AT_POS,5 Z_RAISING,6 Z_UP,7 Z_LOWERING,99 ERROR |
| IR 103 | 103 | LL_SV | u16 | 1 |  |  |  | Slit valve 0 UNKNOWN,1 CLOSED,2 OPENING,3 OPEN,4 CLOSING,5 ERROR |
| IR 104 | 104 | LL_Z | u16 | 1 |  |  |  | Z 軸 0 UNKNOWN,1 DOWN,2 RAISING,3 UP,4 LOWERING |
| IR 105 | 105 | LL_WAF | u16 | 1 |  |  |  | Wafer 位置 0 UNKNOWN,1 AT_LL,2 ON_ARM,3 AT_PM |
| IR 106 | 106 | LL_SEQ | u16 | 1 |  |  |  | 序列步 0-22，0 = 沒在跑 |
| IR 107 | 107 | LL_FLAGS | u16 | 1 |  |  |  | PRES_SUSPECT = LL 壓力從 > IL_LL_JUMP_MTORR 直接跳到「低於下限」或開機後還沒看過有效讀值，疑似壓力計斷線 → 不採信 bits: 0=WAFER_PRESENT, 1=ROUGH_OPEN, 2=VENT_OPEN, 3=LID_OPEN, 4=PRES_UNDER_RANGE, 5=PRES_SUSPECT |
| IR 108–109 | 108–109 | LL_PRES | i32 | 1 |  | mTorr |  | SLK 壓力計（0~1000 Torr）；-1 = 低於量測下限 1 Torr |
| IR 110 | 110 | LL_ALM | u16 | 1 |  |  |  | SLK 警報碼，0 = 正常 |
| IR 111–112 | 111–112 | LL_POS | i32 | 1 |  | step |  | 手臂位置（36.84 step/mm） |
| IR 113 | 113 | LL_SPD | u16 | 1 |  | step/s |  |  |
| IR 114 | 114 | LL_TLM_AGE_MS | u16 | 1 |  | ms |  | 最後一筆 TLM 距今時間 |
| IR 115 | 115 | LL_CMD_RESULT | u16 | 1 |  |  |  | 值: 0=NONE, 1=SENT, 2=DENIED, 3=DONE, 4=ERROR, 5=BUSY |
| IR 116 | 116 | LL_CMD_ACK | u16 | 1 |  |  |  | 主控板處理完的 LL_CMD_SEQ |
| IR 117 | 117 | LL_LAST_EVT | u16 | 1 |  |  |  | 最後一個 SLK 事件代碼（見 docs/loadlock-integration.md） |
| IR 120–121 | 120–121 | PM_PRES_MTORR | i32 | 1 |  | mTorr |  | PM 壓力；狀態不是 OK / UNDER_RANGE 時為 2147483647 |
| IR 122 | 122 | PM_GAUGE_STATUS | u16 | 1 |  |  |  | 值: 0=OK, 1=UNDER_RANGE, 2=OVER_RANGE, 3=FAULT, 4=OFFLINE, 5=NOT_CONFIGURED |
| IR 124–125 | 124–125 | LL_GAUGE_PRES_MTORR | i32 | 1 |  | mTorr |  | LL 擴充壓力計（LL_GAUGE_MODEL ≠ NONE 時）；未知為 2147483647 |
| IR 126 | 126 | LL_GAUGE_STATUS | u16 | 1 |  |  |  | 值: 0=OK, 1=UNDER_RANGE, 2=OVER_RANGE, 3=FAULT, 4=OFFLINE, 5=NOT_CONFIGURED |

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
| HR 100 | 100 | LL_CMD | u16 | 1 |  |  |  | 值: 0=NONE, 1=STATUS, 2=ESTOP, 3=RESET, 4=RECOVER, 10=PUMP, 11=VENT, 12=LOAD, 13=UNLOAD, 14=ABORT, 15=XFER_DONE, 20=SV_OPEN, 21=SV_CLOSE, 22=ROUGH_ON, 23=ROUGH_OFF, 24=VENT_ON, 25=VENT_OFF, 30=ARM_HOME, 31=ARM_AHOME, 32=ARM_LL, 33=ARM_PM, 34=ARM_GOTO, 35=Z_UP, 36=Z_DOWN |
| HR 101–102 | 101–102 | LL_CMD_ARG | i32 | 1 |  |  |  | ARM_GOTO 目標步數 |
| HR 103 | 103 | LL_CMD_SEQ | u16 | 1 |  |  |  | 命令序號，改變才會執行一次（主控板開機時先記下目前值，不會執行舊命令） |
| HR 150 | 150 | IL_SV_DP_MAX_MTORR | u16 | 1 |  | mTorr | 1–10000 | 開 SV 前 Loadlock 與 PM 壓差上限，預設 50（config 可改） |
| HR 151 | 151 | IL_LL_Z_ENABLE | u16 | 1 |  |  |  | SLK Z 軸是否啟用；之後取消 Z 軸（改用 PM Wafer lift）時設 DISABLED，Z 相關互鎖就不檢查 值: 0=DISABLED, 1=ENABLED |
| HR 152 | 152 | IL_LL_STALE_MS | u16 | 1 |  | ms | 100–5000 | Loadlock TLM 超過此時間沒更新 → 視為狀態不明，預設 500 |
| HR 153–154 | 153–154 | IL_ARM_SAFE_POS | i32 | 1 |  | step |  | 手臂 ≤ 此位置才算退出 SV 路徑，預設 5894（160 mm） |
| HR 155 | 155 | IL_SV_MODE | u16 | 1 |  |  |  | 開 SV 的壓力條件：BELOW = LL 與 PM 都低於 IL_SV_BELOW_MTORR（預設，SLK 壓力計低於下限算低於 1 Torr）；DIFF = 壓差 ≤ IL_SV_DP_MAX_MTORR（LL 需有 mTorr 級壓力計） 值: 0=BELOW, 1=DIFF |
| HR 156 | 156 | IL_SV_BELOW_MTORR | u16 | 1 |  | mTorr | 1–10000 | BELOW 模式門檻，預設 1000（1 Torr） |
| HR 157 | 157 | IL_LL_JUMP_MTORR | u16 | 1 |  | mTorr | 1000–60000 | LL 壓力最後有效讀值高於此值就直接變「低於下限」= 疑似斷線，預設 10000（10 Torr） |
| HR 160 | 160 | PM_GAUGE_MODEL | u16 | 1 |  |  |  | 換算公式見 docs/gauges.md 值: 0=NONE, 1=LINEAR, 2=APGX_H, 3=WRG200_X, 4=WRG200_G, 5=WRG200_Q |
| HR 161 | 161 | PM_GAUGE_AI_ADDR | u16 | 1 |  |  |  | 壓力計電壓（mV）所在的 IR 絕對位址，預設 633 = 槽 3 AI_RAW[3] Chamber APGX-H |
| HR 162–163 | 162–163 | PM_GAUGE_FS_MTORR | i32 | 1 |  | mTorr |  | LINEAR（電容式 0~10 V）滿刻度，例 1000 Torr = 1000000 |
| HR 165 | 165 | LL_GAUGE_MODEL | u16 | 1 |  |  |  | NONE = 用 SLK 壓力；設了型號就改用這顆判斷 LL 壓力（不受 SLK 1 Torr 下限與斷線疑慮限制） 值: 0=NONE, 1=LINEAR, 2=APGX_H, 3=WRG200_X, 4=WRG200_G, 5=WRG200_Q |
| HR 166 | 166 | LL_GAUGE_AI_ADDR | u16 | 1 |  |  |  | LL 壓力計電壓（mV）所在的 IR 絕對位址，例 635 = 槽 3 AI_RAW[5] |
| HR 167–168 | 167–168 | LL_GAUGE_FS_MTORR | i32 | 1 |  | mTorr |  | 只有 LINEAR 用 |

## 槽 1：process

製程控制（ALD 閥 ×7、MFC 上游閥 ×8、MFC / 壓力控制器 ×8、腔體壓力計、RF 線檢知）

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
| IR 220 | 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=ALD_V1, 1=ALD_V2, 2=ALD_V3, 3=ALD_V4, 4=ALD_V5, 5=ALD_V6, 6=ALD_V7, 7=MFC_UP_V1, 8=MFC_UP_V2, 9=MFC_UP_V3, 10=MFC_UP_V4, 11=MFC_UP_V5, 12=MFC_UP_V6, 13=MFC_UP_V7, 14=MFC_UP_V8 |
| IR 230–237 | 30–37 | MFC_PV | u16 | 8 | 0.01 | %FS |  | Bronkhorst EL-FLOW F-201CB ×7 + EL-Press P-602CV ×1（通道 8，壓力）；換成 sccm / psi 在 IPC（config 每台滿刻度） |
| IR 238 | 38 | MANOMETER_RAW | u16 | 1 |  | mV |  | Barocel 原始電壓（1000 Torr FS），換算在 IPC |
| IR 240 | 40 | ALD_CYCLE_NOW | u16 | 1 |  |  |  |  |
| IR 241 | 41 | ALD_STEP_NOW | u16 | 1 |  |  |  |  |
| IR 250 | 50 | WAVE_SEQ | u16 | 1 |  |  |  | 壓力波形緩衝序號，每完成一個 cycle +1（波形本體走另外的區塊讀取，TODO） |

### Holding Register (FC03/FC06/FC16)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HR 200 | 0 | HEARTBEAT | u16 | 1 |  |  |  | 上層每次輪詢 +1（IPC→主控板、主控板→執行板各一層） |
| HR 201 | 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 202 | 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 210 | 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=ALD_V1, 1=ALD_V2, 2=ALD_V3, 3=ALD_V4, 4=ALD_V5, 5=ALD_V6, 6=ALD_V7, 7=MFC_UP_V1, 8=MFC_UP_V2, 9=MFC_UP_V3, 10=MFC_UP_V4, 11=MFC_UP_V5, 12=MFC_UP_V6, 13=MFC_UP_V7, 14=MFC_UP_V8 |
| HR 220–227 | 20–27 | MFC_SP | u16 | 8 | 0.01 | %FS | 0–100 | 0–100 % 滿刻度 = 0–5 V |
| HR 300 | 100 | ALD_CYCLES | u16 | 1 |  |  | 1–60000 |  |
| HR 301 | 101 | ALD_STEP_COUNT | u16 | 1 |  |  | 1–16 |  |
| HR 302 | 102 | PURGE_VALVES | u16 | 1 |  |  |  | 安全狀態下保持開啟的 ALD 閥（bit0 = ALD_V1 … bit6 = ALD_V7），例如 N2 purge。TODO 依管路 |
| HR 310–325 | 110–125 | ALD_STEP_VALVES | u16 | 16 |  |  |  | 每步要開的 ALD 閥位元（bit0 = ALD_V1 … bit6 = ALD_V7） |
| HR 330–345 | 130–145 | ALD_STEP_MS | u16 | 16 |  | ms | 1–60000 |  |

## 槽 2：heater

加熱器與 K 型熱電偶（16 組，板內 PID；2026-11-11 新設計全部改 K type）

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
| IR 410 | 10 | DI | u16 | 1 |  |  |  | bits: 0=OT_CHAMBER, 1=OT_PRECURSOR, 2=OT_DELIVERY, 3=OT_CHUCK |
| IR 420 | 20 | DO_FB | u16 | 1 |  |  |  | bits: 0=PRECURSOR1, 1=PRECURSOR2, 2=PRECURSOR3, 3=PRECURSOR4, 4=PRECURSOR5, 5=BUBBLER, 6=CHAMBER_UPPER, 7=CHAMBER_LOWER, 8=DELIVERY, 9=PRECURSOR_MODULE, 10=FIVE_WAY, 11=MAIN_VALVE, 12=CHUCK1, 13=CHUCK2, 14=SPARE1, 15=SPARE2 |
| IR 430–445 | 30–45 | TC_TEMP | i16 | 16 | 0.1 | degC |  | -3276.8 = 熱電偶斷線 / 讀不到 |
| IR 450–465 | 50–65 | HEATER_DUTY | u16 | 16 | 0.1 | % |  |  |

### Holding Register (FC03/FC06/FC16)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HR 400 | 0 | HEARTBEAT | u16 | 1 |  |  |  | 上層每次輪詢 +1（IPC→主控板、主控板→執行板各一層） |
| HR 401 | 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 402 | 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 410 | 10 | HEATER_ENABLE | u16 | 1 |  |  |  | bits: 0=PRECURSOR1, 1=PRECURSOR2, 2=PRECURSOR3, 3=PRECURSOR4, 4=PRECURSOR5, 5=BUBBLER, 6=CHAMBER_UPPER, 7=CHAMBER_LOWER, 8=DELIVERY, 9=PRECURSOR_MODULE, 10=FIVE_WAY, 11=MAIN_VALVE, 12=CHUCK1, 13=CHUCK2, 14=SPARE1, 15=SPARE2 |
| HR 420–435 | 20–35 | HEATER_SP | u16 | 16 | 0.1 | degC | 0–650 | 超過該組 HEATER_LIMIT 就不加熱 |
| HR 440–455 | 40–55 | HEATER_RAMP | u16 | 16 | 0.1 | degC/min | 0–50 |  |
| HR 460–475 | 60–75 | HOLD_SP | u16 | 16 | 0.1 | degC | 0–650 | 安全狀態時的保溫溫度（避免前驅物在管路冷凝），0 = 安全狀態關閉該組。TODO 依製程 |
| HR 480–495 | 80–95 | HEATER_LIMIT | u16 | 16 | 0.1 | degC | 0–650 | 各組溫度上限（config 寫入）：前驅物 200、main valve 250、其他 300~350、Chuck 600；0 = 該組不准加熱 |
| HR 500–515 | 100–115 | PID_KP | u16 | 16 | 0.01 |  |  |  |
| HR 520–535 | 120–135 | PID_KI | u16 | 16 | 0.001 |  |  |  |
| HR 540–555 | 140–155 | PID_KD | u16 | 16 | 0.01 |  |  |  |

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
| IR 630–637 | 30–37 | AI_RAW | u16 | 8 |  | mV |  | 0 CDA, 1 冷卻水流量, 2 Ion gauge（WRG200）, 3 Chamber convectron（APGX-H）, 4 Foreline convectron, 5 預留 LL 壓力計擴充, 6-7 Spare |

### Holding Register (FC03/FC06/FC16)

| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HR 600 | 0 | HEARTBEAT | u16 | 1 |  |  |  | 上層每次輪詢 +1（IPC→主控板、主控板→執行板各一層） |
| HR 601 | 1 | COMMAND | u16 | 1 |  |  |  | 值: 0=NONE, 1=START, 2=STOP, 3=RESET_ALARM, 4=ENTER_SAFE |
| HR 602 | 2 | WATCHDOG_MS | u16 | 1 |  | ms | 200–10000 | 預設 1000，TODO 確認 |
| HR 610 | 10 | DO_CMD | u16 | 1 |  |  |  | bits: 0=TURBO_GV, 1=TURBO_ROUGH, 2=CHAMBER_ROUGH, 3=CHAMBER_VENT, 4=DRYPUMP_ON, 5=TURBO_ON |

## 槽 4：motion

PM 內升降（軸 0 Heater module、軸 1 預留；Wafer lift 為氣缸）。Loadlock 手臂 / Indexer 由 SLK 模組控制

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
| IR 810 | 10 | DI | u16 | 1 |  |  |  | bits: 0=HEATER_DOWN, 1=HEATER_PROCESS, 2=WAFER_LIFT_DOWN, 3=WAFER_LIFT_UP, 4=WAFER_PRESENT |
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
