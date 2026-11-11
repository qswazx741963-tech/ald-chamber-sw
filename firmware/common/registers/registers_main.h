/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include "registers_common.h"

/* main: 主控板（系統狀態、跨板互鎖、Loadlock / Throttle valve / RF 閘道） */
#define MAIN_DEFAULT_SLOT 0u
#define MAIN_BOARD_TYPE   0u

/* ---- Input Register (FC04)（區內位址；共用表頭見 COM_IR_*） */
#define MAIN_IR_SIZE 118u
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
#define MAIN_IR_LL_LINK                              100u  /* u16 */
#define MAIN_IR_LL_LINK_BIT_ONLINE                   0u
#define MAIN_IR_LL_LINK_BIT_TLM_FRESH                1u
#define MAIN_IR_LL_LINK_BIT_ONLINE_MODE              2u
#define MAIN_IR_LL_LINK_BIT_IL_BYPASS                3u
#define MAIN_IR_LL_LINK_BIT_PORT_LOCKED              4u
#define MAIN_IR_LL_CHM                               101u  /* u16 */
#define MAIN_IR_LL_ARM                               102u  /* u16 */
#define MAIN_IR_LL_SV                                103u  /* u16 */
#define MAIN_IR_LL_Z                                 104u  /* u16 */
#define MAIN_IR_LL_WAF                               105u  /* u16 */
#define MAIN_IR_LL_SEQ                               106u  /* u16 */
#define MAIN_IR_LL_FLAGS                             107u  /* u16 */
#define MAIN_IR_LL_FLAGS_BIT_WAFER_PRESENT           0u
#define MAIN_IR_LL_FLAGS_BIT_ROUGH_OPEN              1u
#define MAIN_IR_LL_FLAGS_BIT_VENT_OPEN               2u
#define MAIN_IR_LL_FLAGS_BIT_LID_OPEN                3u
#define MAIN_IR_LL_FLAGS_BIT_PRES_UNDER_RANGE        4u
#define MAIN_IR_LL_PRES                              108u  /* i32 [mTorr] */
#define MAIN_IR_LL_ALM                               110u  /* u16 */
#define MAIN_IR_LL_POS                               111u  /* i32 [step] */
#define MAIN_IR_LL_SPD                               113u  /* u16 [step/s] */
#define MAIN_IR_LL_TLM_AGE_MS                        114u  /* u16 [ms] */
#define MAIN_IR_LL_CMD_RESULT                        115u  /* u16 */
#define MAIN_IR_LL_CMD_RESULT_NONE                   0u
#define MAIN_IR_LL_CMD_RESULT_SENT                   1u
#define MAIN_IR_LL_CMD_RESULT_DENIED                 2u
#define MAIN_IR_LL_CMD_RESULT_DONE                   3u
#define MAIN_IR_LL_CMD_RESULT_ERROR                  4u
#define MAIN_IR_LL_CMD_RESULT_BUSY                   5u
#define MAIN_IR_LL_CMD_ACK                           116u  /* u16 */
#define MAIN_IR_LL_LAST_EVT                          117u  /* u16 */

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
#define MAIN_HR_LL_CMD                               100u  /* u16 */
#define MAIN_HR_LL_CMD_NONE                          0u
#define MAIN_HR_LL_CMD_STATUS                        1u
#define MAIN_HR_LL_CMD_ESTOP                         2u
#define MAIN_HR_LL_CMD_RESET                         3u
#define MAIN_HR_LL_CMD_RECOVER                       4u
#define MAIN_HR_LL_CMD_PUMP                          10u
#define MAIN_HR_LL_CMD_VENT                          11u
#define MAIN_HR_LL_CMD_LOAD                          12u
#define MAIN_HR_LL_CMD_UNLOAD                        13u
#define MAIN_HR_LL_CMD_ABORT                         14u
#define MAIN_HR_LL_CMD_XFER_DONE                     15u
#define MAIN_HR_LL_CMD_SV_OPEN                       20u
#define MAIN_HR_LL_CMD_SV_CLOSE                      21u
#define MAIN_HR_LL_CMD_ROUGH_ON                      22u
#define MAIN_HR_LL_CMD_ROUGH_OFF                     23u
#define MAIN_HR_LL_CMD_VENT_ON                       24u
#define MAIN_HR_LL_CMD_VENT_OFF                      25u
#define MAIN_HR_LL_CMD_ARM_HOME                      30u
#define MAIN_HR_LL_CMD_ARM_AHOME                     31u
#define MAIN_HR_LL_CMD_ARM_LL                        32u
#define MAIN_HR_LL_CMD_ARM_PM                        33u
#define MAIN_HR_LL_CMD_ARM_GOTO                      34u
#define MAIN_HR_LL_CMD_Z_UP                          35u
#define MAIN_HR_LL_CMD_Z_DOWN                        36u
#define MAIN_HR_LL_CMD_ARG                           101u  /* i32 */
#define MAIN_HR_LL_CMD_SEQ                           103u  /* u16 */
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
