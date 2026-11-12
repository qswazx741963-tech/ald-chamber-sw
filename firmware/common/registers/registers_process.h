/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include "registers_common.h"

/* process: 製程控制（ALD 閥 ×7、MFC 上游閥 ×8、MFC / 壓力控制器 ×8、腔體壓力計、RF 線檢知） */
#define PROC_DEFAULT_SLOT 1u
#define PROC_BOARD_TYPE   1u

/* ---- Input Register (FC04)（區內位址；共用表頭見 COM_IR_*） */
#define PROC_IR_SIZE 51u
#define PROC_IR_DI                                   10u  /* u16 */
#define PROC_IR_DI_BIT_RF1_POWER_CABLE_OK            0u
#define PROC_IR_DI_BIT_RF1_MATCH_CABLE_OK            1u
#define PROC_IR_DO_FB                                20u  /* u16 */
#define PROC_IR_DO_FB_BIT_ALD_V1                     0u
#define PROC_IR_DO_FB_BIT_ALD_V2                     1u
#define PROC_IR_DO_FB_BIT_ALD_V3                     2u
#define PROC_IR_DO_FB_BIT_ALD_V4                     3u
#define PROC_IR_DO_FB_BIT_ALD_V5                     4u
#define PROC_IR_DO_FB_BIT_ALD_V6                     5u
#define PROC_IR_DO_FB_BIT_ALD_V7                     6u
#define PROC_IR_DO_FB_BIT_MFC_UP_V1                  7u
#define PROC_IR_DO_FB_BIT_MFC_UP_V2                  8u
#define PROC_IR_DO_FB_BIT_MFC_UP_V3                  9u
#define PROC_IR_DO_FB_BIT_MFC_UP_V4                  10u
#define PROC_IR_DO_FB_BIT_MFC_UP_V5                  11u
#define PROC_IR_DO_FB_BIT_MFC_UP_V6                  12u
#define PROC_IR_DO_FB_BIT_MFC_UP_V7                  13u
#define PROC_IR_DO_FB_BIT_MFC_UP_V8                  14u
#define PROC_IR_MFC_PV                               30u  /* u16 x8 [%FS] scale 0.01 */
#define PROC_IR_MFC_PV_COUNT                         8u
#define PROC_IR_MANOMETER_RAW                        38u  /* u16 [mV] */
#define PROC_IR_ALD_CYCLE_NOW                        40u  /* u16 */
#define PROC_IR_ALD_STEP_NOW                         41u  /* u16 */
#define PROC_IR_WAVE_SEQ                             50u  /* u16 */

/* ---- Holding Register (FC03/FC06/FC16)（區內位址；共用表頭見 COM_HR_*） */
#define PROC_HR_SIZE 192u
#define PROC_HR_DO_CMD                               10u  /* u16 */
#define PROC_HR_DO_CMD_BIT_ALD_V1                    0u
#define PROC_HR_DO_CMD_BIT_ALD_V2                    1u
#define PROC_HR_DO_CMD_BIT_ALD_V3                    2u
#define PROC_HR_DO_CMD_BIT_ALD_V4                    3u
#define PROC_HR_DO_CMD_BIT_ALD_V5                    4u
#define PROC_HR_DO_CMD_BIT_ALD_V6                    5u
#define PROC_HR_DO_CMD_BIT_ALD_V7                    6u
#define PROC_HR_DO_CMD_BIT_MFC_UP_V1                 7u
#define PROC_HR_DO_CMD_BIT_MFC_UP_V2                 8u
#define PROC_HR_DO_CMD_BIT_MFC_UP_V3                 9u
#define PROC_HR_DO_CMD_BIT_MFC_UP_V4                 10u
#define PROC_HR_DO_CMD_BIT_MFC_UP_V5                 11u
#define PROC_HR_DO_CMD_BIT_MFC_UP_V6                 12u
#define PROC_HR_DO_CMD_BIT_MFC_UP_V7                 13u
#define PROC_HR_DO_CMD_BIT_MFC_UP_V8                 14u
#define PROC_HR_MFC_SP                               20u  /* u16 x8 [%FS] scale 0.01 */
#define PROC_HR_MFC_SP_COUNT                         8u
#define PROC_HR_MFC_SP_MIN_RAW                       0
#define PROC_HR_MFC_SP_MAX_RAW                       10000
#define PROC_HR_ALD_CYCLES                           100u  /* u16 */
#define PROC_HR_ALD_CYCLES_MIN_RAW                   1
#define PROC_HR_ALD_CYCLES_MAX_RAW                   60000
#define PROC_HR_ALD_STEP_COUNT                       101u  /* u16 */
#define PROC_HR_ALD_STEP_COUNT_MIN_RAW               1
#define PROC_HR_ALD_STEP_COUNT_MAX_RAW               16
#define PROC_HR_PURGE_VALVES                         102u  /* u16 */
#define PROC_HR_ALD_STEP_VALVES                      110u  /* u16 x16 */
#define PROC_HR_ALD_STEP_VALVES_COUNT                16u
#define PROC_HR_ALD_STEP_MS                          130u  /* u16 x16 [ms] */
#define PROC_HR_ALD_STEP_MS_COUNT                    16u
#define PROC_HR_ALD_STEP_MS_MIN_RAW                  1
#define PROC_HR_ALD_STEP_MS_MAX_RAW                  60000
#define PROC_HR_ALD_STEP_MFC_SET                     150u  /* u16 x4 */
#define PROC_HR_ALD_STEP_MFC_SET_COUNT               4u
#define PROC_HR_ALD_MFC_PRESET                       160u  /* u16 x32 [%FS] scale 0.01 */
#define PROC_HR_ALD_MFC_PRESET_COUNT                 32u
