/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include <stdint.h>

/*
 * EtherCAT PDO ↔ 暫存器表對應（主控板從站與 IPC 主站共用）
 *   RxPDO 0x1600（主站 → 主控板）：所有 HR，entry = 0x7000:n，每個 16 bit
 *   TxPDO 0x1A00（主控板 → 主站）：所有 IR，entry = 0x6000:n，每個 16 bit
 * 每一段 { 槽號, 區內起始位址, 長度(word), PDO 內 word 偏移 }；PDO 內順序 = 槽號、位址遞增
 */
#define ALD_ECAT_VENDOR_ID     0x00000000u  /* TODO ETG 申請 */
#define ALD_ECAT_PRODUCT_CODE  0x00A1D001u
#define ALD_ECAT_REVISION      3u   /* = map_version */
#define ALD_PDO_RX_WORDS       245u
#define ALD_PDO_TX_WORDS       152u
#define ALD_PDO_RX_INDEX       0x1600u
#define ALD_PDO_TX_INDEX       0x1A00u
#define ALD_PDO_RX_OBJ         0x7000u
#define ALD_PDO_TX_OBJ         0x6000u

typedef struct { uint8_t slot; uint16_t addr; uint16_t len; uint16_t pdo_off; } ald_pdo_seg_t;

#define ALD_PDO_RX_SEGS { \
    { 0u, 0u, 3u, 0u }, /* main */ \
    { 0u, 10u, 1u, 3u }, /* main */ \
    { 0u, 20u, 3u, 4u }, /* main */ \
    { 0u, 30u, 3u, 7u }, /* main */ \
    { 0u, 100u, 4u, 10u }, /* main */ \
    { 0u, 150u, 8u, 14u }, /* main */ \
    { 0u, 160u, 4u, 22u }, /* main */ \
    { 0u, 165u, 4u, 26u }, /* main */ \
    { 1u, 0u, 3u, 30u }, /* process */ \
    { 1u, 10u, 1u, 33u }, /* process */ \
    { 1u, 20u, 8u, 34u }, /* process */ \
    { 1u, 100u, 3u, 42u }, /* process */ \
    { 1u, 110u, 16u, 45u }, /* process */ \
    { 1u, 130u, 16u, 61u }, /* process */ \
    { 1u, 150u, 4u, 77u }, /* process */ \
    { 1u, 160u, 32u, 81u }, /* process */ \
    { 2u, 0u, 3u, 113u }, /* heater */ \
    { 2u, 10u, 1u, 116u }, /* heater */ \
    { 2u, 20u, 16u, 117u }, /* heater */ \
    { 2u, 40u, 16u, 133u }, /* heater */ \
    { 2u, 60u, 16u, 149u }, /* heater */ \
    { 2u, 80u, 16u, 165u }, /* heater */ \
    { 2u, 100u, 16u, 181u }, /* heater */ \
    { 2u, 120u, 16u, 197u }, /* heater */ \
    { 2u, 140u, 16u, 213u }, /* heater */ \
    { 3u, 0u, 3u, 229u }, /* vacuum */ \
    { 3u, 10u, 1u, 232u }, /* vacuum */ \
    { 4u, 0u, 3u, 233u }, /* motion */ \
    { 4u, 10u, 1u, 236u }, /* motion */ \
    { 4u, 20u, 4u, 237u }, /* motion */ \
    { 4u, 30u, 2u, 241u }, /* motion */ \
    { 4u, 40u, 2u, 243u }, /* motion */ \
}
#define ALD_PDO_RX_SEG_COUNT 32u

#define ALD_PDO_TX_SEGS { \
    { 0u, 0u, 9u, 0u }, /* main */ \
    { 0u, 10u, 7u, 9u }, /* main */ \
    { 0u, 30u, 3u, 16u }, /* main */ \
    { 0u, 40u, 6u, 19u }, /* main */ \
    { 0u, 100u, 18u, 25u }, /* main */ \
    { 0u, 120u, 3u, 43u }, /* main */ \
    { 0u, 124u, 3u, 46u }, /* main */ \
    { 1u, 0u, 9u, 49u }, /* process */ \
    { 1u, 10u, 1u, 58u }, /* process */ \
    { 1u, 20u, 1u, 59u }, /* process */ \
    { 1u, 30u, 9u, 60u }, /* process */ \
    { 1u, 40u, 2u, 69u }, /* process */ \
    { 1u, 50u, 1u, 71u }, /* process */ \
    { 2u, 0u, 9u, 72u }, /* heater */ \
    { 2u, 10u, 1u, 81u }, /* heater */ \
    { 2u, 20u, 1u, 82u }, /* heater */ \
    { 2u, 30u, 16u, 83u }, /* heater */ \
    { 2u, 50u, 16u, 99u }, /* heater */ \
    { 3u, 0u, 9u, 115u }, /* vacuum */ \
    { 3u, 10u, 1u, 124u }, /* vacuum */ \
    { 3u, 20u, 2u, 125u }, /* vacuum */ \
    { 3u, 30u, 8u, 127u }, /* vacuum */ \
    { 4u, 0u, 9u, 135u }, /* motion */ \
    { 4u, 10u, 1u, 144u }, /* motion */ \
    { 4u, 20u, 1u, 145u }, /* motion */ \
    { 4u, 30u, 4u, 146u }, /* motion */ \
    { 4u, 40u, 2u, 150u }, /* motion */ \
}
#define ALD_PDO_TX_SEG_COUNT 27u

/* HR 開機預設值 { 絕對位址, 原始值 }（registers.yaml 的 default）：主控板 reg_image_init 與 IPC 主站程式 bridge_init 都載入 */
typedef struct { uint16_t addr; uint16_t value; } ald_hr_default_t;
#define ALD_HR_DEFAULTS { \
    { 2u, 1000u }, /* main.WATCHDOG_MS */ \
    { 150u, 50u }, /* main.IL_SV_DP_MAX_MTORR */ \
    { 151u, 1u }, /* main.IL_LL_Z_ENABLE */ \
    { 152u, 500u }, /* main.IL_LL_STALE_MS */ \
    { 154u, 5894u }, /* main.IL_ARM_SAFE_POS */ \
    { 156u, 1000u }, /* main.IL_SV_BELOW_MTORR */ \
    { 157u, 10000u }, /* main.IL_LL_JUMP_MTORR */ \
    { 160u, 2u }, /* main.PM_GAUGE_MODEL */ \
    { 161u, 633u }, /* main.PM_GAUGE_AI_ADDR */ \
    { 162u, 15u }, /* main.PM_GAUGE_FS_MTORR */ \
    { 163u, 16960u }, /* main.PM_GAUGE_FS_MTORR */ \
    { 202u, 1000u }, /* process.WATCHDOG_MS */ \
    { 402u, 1000u }, /* heater.WATCHDOG_MS */ \
    { 500u, 800u }, /* heater.PID_KP */ \
    { 501u, 800u }, /* heater.PID_KP */ \
    { 502u, 800u }, /* heater.PID_KP */ \
    { 503u, 800u }, /* heater.PID_KP */ \
    { 504u, 800u }, /* heater.PID_KP */ \
    { 505u, 800u }, /* heater.PID_KP */ \
    { 506u, 800u }, /* heater.PID_KP */ \
    { 507u, 800u }, /* heater.PID_KP */ \
    { 508u, 800u }, /* heater.PID_KP */ \
    { 509u, 800u }, /* heater.PID_KP */ \
    { 510u, 800u }, /* heater.PID_KP */ \
    { 511u, 800u }, /* heater.PID_KP */ \
    { 512u, 800u }, /* heater.PID_KP */ \
    { 513u, 800u }, /* heater.PID_KP */ \
    { 514u, 800u }, /* heater.PID_KP */ \
    { 515u, 800u }, /* heater.PID_KP */ \
    { 520u, 50u }, /* heater.PID_KI */ \
    { 521u, 50u }, /* heater.PID_KI */ \
    { 522u, 50u }, /* heater.PID_KI */ \
    { 523u, 50u }, /* heater.PID_KI */ \
    { 524u, 50u }, /* heater.PID_KI */ \
    { 525u, 50u }, /* heater.PID_KI */ \
    { 526u, 50u }, /* heater.PID_KI */ \
    { 527u, 50u }, /* heater.PID_KI */ \
    { 528u, 50u }, /* heater.PID_KI */ \
    { 529u, 50u }, /* heater.PID_KI */ \
    { 530u, 50u }, /* heater.PID_KI */ \
    { 531u, 50u }, /* heater.PID_KI */ \
    { 532u, 50u }, /* heater.PID_KI */ \
    { 533u, 50u }, /* heater.PID_KI */ \
    { 534u, 50u }, /* heater.PID_KI */ \
    { 535u, 50u }, /* heater.PID_KI */ \
    { 602u, 1000u }, /* vacuum.WATCHDOG_MS */ \
    { 802u, 1000u }, /* motion.WATCHDOG_MS */ \
    { 830u, 1000u }, /* motion.AXIS_SPEED */ \
    { 831u, 1000u }, /* motion.AXIS_SPEED */ \
}
#define ALD_HR_DEFAULT_COUNT 49u
