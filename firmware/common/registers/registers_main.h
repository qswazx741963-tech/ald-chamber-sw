/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include "registers_common.h"

/* main: 主控板（系統狀態、跨板互鎖、Throttle valve / RF 閘道、Loadlock 狀態鏡像） */
#define MAIN_DEFAULT_SLOT 0u
#define MAIN_BOARD_TYPE   0u

/* ---- Input Register (FC04)（區內位址；共用表頭見 COM_IR_*） */
#define MAIN_IR_SIZE 46u
#define MAIN_IR_DI                                   10u  /* u16 */
#define MAIN_IR_DI_BIT_SAFE_LINE_OK                  0u
#define MAIN_IR_DI_BIT_EMO_OK                        1u
#define MAIN_IR_DI_BIT_CDA_OK                        2u
#define MAIN_IR_DI_BIT_TRANSFER_PERMIT_OUT           3u
#define MAIN_IR_SLOT_ONLINE                          11u  /* u16 */
#define MAIN_IR_SYS_STATE                            12u  /* u16 */
#define MAIN_IR_SYS_STATE_BOOT                       0u
#define MAIN_IR_SYS_STATE_SAFE                       1u
#define MAIN_IR_SYS_STATE_IDLE                       2u
#define MAIN_IR_SYS_STATE_PROCESS                    3u
#define MAIN_IR_SYS_STATE_TRANSFER                   4u
#define MAIN_IR_SYS_STATE_ALARM                      5u
#define MAIN_IR_IL_DENY                              13u  /* u16 */
#define MAIN_IR_IL_DENY_BIT_SAFE_ACTIVE              0u
#define MAIN_IR_IL_DENY_BIT_SLOT_OFFLINE             1u
#define MAIN_IR_IL_DENY_BIT_LL_UNKNOWN               2u
#define MAIN_IR_IL_DENY_BIT_LL_ALARM                 3u
#define MAIN_IR_IL_DENY_BIT_SV_NOT_CLOSED            4u
#define MAIN_IR_IL_DENY_BIT_SV_NOT_OPEN              5u
#define MAIN_IR_IL_DENY_BIT_DP_HIGH                  6u
#define MAIN_IR_IL_DENY_BIT_PRESSURE_UNKNOWN         7u
#define MAIN_IR_IL_DENY_BIT_PM_BUSY                  8u
#define MAIN_IR_IL_DENY_BIT_PM_VENTING               9u
#define MAIN_IR_IL_DENY_BIT_ARM_IN_SV_PATH           10u
#define MAIN_IR_IL_DENY_BIT_Z_NOT_DOWN               11u
#define MAIN_IR_IL_DENY_BIT_LL_LID_OPEN              12u
#define MAIN_IR_IL_DENY_BIT_LL_VALVE_CONFLICT        13u
#define MAIN_IR_IL_DENY_BIT_PM_NOT_AT_TRANSFER       14u
#define MAIN_IR_IL_DENY_BIT_LL_BUSY                  15u
#define MAIN_IR_IL_DENY_ACTION                       14u  /* u16 */
#define MAIN_IR_IL_DENY2                             15u  /* u16 */
#define MAIN_IR_IL_DENY2_BIT_TURBO_GV_OPEN           0u
#define MAIN_IR_IL_DENY2_BIT_ARM_MOVING              1u
#define MAIN_IR_IL_DENY2_BIT_HEATER_NOT_DOWN         2u
#define MAIN_IR_TV_PRESSURE                          30u  /* u16 [Torr] scale 0.001 */
#define MAIN_IR_TV_POSITION                          31u  /* u16 [%] scale 0.1 */
#define MAIN_IR_TV_STATUS                            32u  /* u16 */
#define MAIN_IR_RF_FWD_W                             40u  /* u16 x2 [W] */
#define MAIN_IR_RF_FWD_W_COUNT                       2u
#define MAIN_IR_RF_REF_W                             42u  /* u16 x2 [W] */
#define MAIN_IR_RF_REF_W_COUNT                       2u
#define MAIN_IR_RF_STATUS                            44u  /* u16 x2 */
#define MAIN_IR_RF_STATUS_COUNT                      2u

/* ---- Holding Register (FC03/FC06/FC16)（區內位址；共用表頭見 COM_HR_*） */
#define MAIN_HR_SIZE 155u
#define MAIN_HR_DO_CMD                               10u  /* u16 */
#define MAIN_HR_DO_CMD_BIT_BUZZER                    0u
#define MAIN_HR_DO_CMD_BIT_TOWER_RED                 1u
#define MAIN_HR_DO_CMD_BIT_TOWER_YELLOW              2u
#define MAIN_HR_DO_CMD_BIT_TOWER_GREEN               3u
#define MAIN_HR_TV_MODE                              20u  /* u16 */
#define MAIN_HR_TV_MODE_OPEN                         0u
#define MAIN_HR_TV_MODE_CLOSE                        1u
#define MAIN_HR_TV_MODE_POSITION                     2u
#define MAIN_HR_TV_MODE_PRESSURE                     3u
#define MAIN_HR_TV_SP_POSITION                       21u  /* u16 [%] scale 0.1 */
#define MAIN_HR_TV_SP_POSITION_MIN_RAW               0
#define MAIN_HR_TV_SP_POSITION_MAX_RAW               1000
#define MAIN_HR_TV_SP_PRESSURE                       22u  /* u16 [Torr] scale 0.001 */
#define MAIN_HR_TV_SP_PRESSURE_MIN_RAW               0
#define MAIN_HR_TV_SP_PRESSURE_MAX_RAW               10000
#define MAIN_HR_RF_ON                                30u  /* u16 */
#define MAIN_HR_RF_ON_BIT_RF1                        0u
#define MAIN_HR_RF_ON_BIT_RF2                        1u
#define MAIN_HR_RF_SP_W                              31u  /* u16 x2 [W] */
#define MAIN_HR_RF_SP_W_COUNT                        2u
#define MAIN_HR_RF_SP_W_MIN_RAW                      0
#define MAIN_HR_RF_SP_W_MAX_RAW                      600
#define MAIN_HR_LL_M_SEQ                             100u  /* u16 */
#define MAIN_HR_LL_M_SV                              101u  /* u16 */
#define MAIN_HR_LL_M_Z                               102u  /* u16 */
#define MAIN_HR_LL_M_ARM                             103u  /* u16 */
#define MAIN_HR_LL_M_POS                             104u  /* i32 [step] */
#define MAIN_HR_LL_M_ALM                             106u  /* u16 */
#define MAIN_HR_LL_M_FLAGS                           107u  /* u16 */
#define MAIN_HR_LL_M_FLAGS_BIT_ROUGH_OPEN            0u
#define MAIN_HR_LL_M_FLAGS_BIT_VENT_OPEN             1u
#define MAIN_HR_LL_M_FLAGS_BIT_LID_OPEN              2u
#define MAIN_HR_LL_M_PRES                            108u  /* i32 [mTorr] */
#define MAIN_HR_IL_SV_DP_MAX_MTORR                   150u  /* u16 [mTorr] */
#define MAIN_HR_IL_SV_DP_MAX_MTORR_MIN_RAW           1
#define MAIN_HR_IL_SV_DP_MAX_MTORR_MAX_RAW           10000
#define MAIN_HR_IL_LL_Z_ENABLE                       151u  /* u16 */
#define MAIN_HR_IL_LL_Z_ENABLE_DISABLED              0u
#define MAIN_HR_IL_LL_Z_ENABLE_ENABLED               1u
#define MAIN_HR_IL_LL_STALE_MS                       152u  /* u16 [ms] */
#define MAIN_HR_IL_LL_STALE_MS_MIN_RAW               100
#define MAIN_HR_IL_LL_STALE_MS_MAX_RAW               5000
#define MAIN_HR_IL_ARM_SAFE_POS                      153u  /* i32 [step] */
