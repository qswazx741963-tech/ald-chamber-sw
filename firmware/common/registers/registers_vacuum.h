/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include "registers_common.h"

/* vacuum: PM 壓力 / 真空、閥件、泵（板內互鎖）。Loadlock 的閥、Slit valve 已移到 SLK 模組 */
#define VAC_DEFAULT_SLOT 3u
#define VAC_BOARD_TYPE   3u

/* ---- Input Register (FC04)（區內位址；共用表頭見 COM_IR_*） */
#define VAC_IR_SIZE 38u
#define VAC_IR_DI                                    10u  /* u16 */
#define VAC_IR_DI_BIT_CHAMBER_LID_CLOSED             0u
#define VAC_IR_DI_BIT_LID_COVER_CLOSED               1u
#define VAC_IR_DI_BIT_TURBO_GV_OPENED                2u
#define VAC_IR_DI_BIT_TURBO_GV_CLOSED                3u
#define VAC_IR_DI_BIT_DRYPUMP_RUNNING                4u
#define VAC_IR_DI_BIT_DRYPUMP_ALARM                  5u
#define VAC_IR_DI_BIT_TURBO_AT_SPEED                 6u
#define VAC_IR_DI_BIT_TURBO_ALARM                    7u
#define VAC_IR_DO_FB                                 20u  /* u16 */
#define VAC_IR_DO_FB_BIT_TURBO_GV                    0u
#define VAC_IR_DO_FB_BIT_TURBO_ROUGH                 1u
#define VAC_IR_DO_FB_BIT_CHAMBER_ROUGH               2u
#define VAC_IR_DO_FB_BIT_CHAMBER_VENT                3u
#define VAC_IR_DO_FB_BIT_DRYPUMP_ON                  4u
#define VAC_IR_DO_FB_BIT_TURBO_ON                    5u
#define VAC_IR_DO_BLOCKED                            21u  /* u16 */
#define VAC_IR_DO_BLOCKED_BIT_TURBO_GV               0u
#define VAC_IR_DO_BLOCKED_BIT_TURBO_ROUGH            1u
#define VAC_IR_DO_BLOCKED_BIT_CHAMBER_ROUGH          2u
#define VAC_IR_DO_BLOCKED_BIT_CHAMBER_VENT           3u
#define VAC_IR_DO_BLOCKED_BIT_DRYPUMP_ON             4u
#define VAC_IR_DO_BLOCKED_BIT_TURBO_ON               5u
#define VAC_IR_AI_RAW                                30u  /* u16 x8 [mV] */
#define VAC_IR_AI_RAW_COUNT                          8u

/* ---- Holding Register (FC03/FC06/FC16)（區內位址；共用表頭見 COM_HR_*） */
#define VAC_HR_SIZE 11u
#define VAC_HR_DO_CMD                                10u  /* u16 */
#define VAC_HR_DO_CMD_BIT_TURBO_GV                   0u
#define VAC_HR_DO_CMD_BIT_TURBO_ROUGH                1u
#define VAC_HR_DO_CMD_BIT_CHAMBER_ROUGH              2u
#define VAC_HR_DO_CMD_BIT_CHAMBER_VENT               3u
#define VAC_HR_DO_CMD_BIT_DRYPUMP_ON                 4u
#define VAC_HR_DO_CMD_BIT_TURBO_ON                   5u
