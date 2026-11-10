/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
/* 各槽共用表頭（區內位址，每塊板都一樣） */

/* ---- Input Register (FC04) */
#define COM_IR_BOARD_TYPE                            0u  /* u16 */
#define COM_IR_FW_VERSION                            1u  /* u16 */
#define COM_IR_MAP_VERSION                           2u  /* u16 */
#define COM_IR_STATUS                                3u  /* u16 */
#define COM_IR_STATUS_BIT_READY                      0u
#define COM_IR_STATUS_BIT_RUNNING                    1u
#define COM_IR_STATUS_BIT_SAFE_STATE                 2u
#define COM_IR_STATUS_BIT_WATCHDOG_TRIPPED           3u
#define COM_IR_STATUS_BIT_PARAM_REJECTED             4u
#define COM_IR_STATUS_BIT_LOCAL_INTERLOCK            5u
#define COM_IR_STATUS_BIT_ONLINE                     6u
#define COM_IR_ALARM_BITS                            4u  /* u16 */
#define COM_IR_HEARTBEAT_ECHO                        5u  /* u16 */
#define COM_IR_BOARD_COUNTER                         6u  /* u16 */
#define COM_IR_LAST_ERROR                            7u  /* u16 */
#define COM_IR_SLOT_ID                               8u  /* u16 */

/* ---- Holding Register (FC03/FC06/FC16) */
#define COM_HR_HEARTBEAT                             0u  /* u16 */
#define COM_HR_COMMAND                               1u  /* u16 */
#define COM_HR_COMMAND_NONE                          0u
#define COM_HR_COMMAND_START                         1u
#define COM_HR_COMMAND_STOP                          2u
#define COM_HR_COMMAND_RESET_ALARM                   3u
#define COM_HR_COMMAND_ENTER_SAFE                    4u
#define COM_HR_WATCHDOG_MS                           2u  /* u16 [ms] */
#define COM_HR_WATCHDOG_MS_MIN_RAW                   200
#define COM_HR_WATCHDOG_MS_MAX_RAW                   10000
