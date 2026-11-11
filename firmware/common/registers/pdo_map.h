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
#define ALD_ECAT_REVISION      2u   /* = map_version */
#define ALD_PDO_RX_WORDS       209u
#define ALD_PDO_TX_WORDS       151u
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
    { 2u, 0u, 3u, 77u }, /* heater */ \
    { 2u, 10u, 1u, 80u }, /* heater */ \
    { 2u, 20u, 16u, 81u }, /* heater */ \
    { 2u, 40u, 16u, 97u }, /* heater */ \
    { 2u, 60u, 16u, 113u }, /* heater */ \
    { 2u, 80u, 16u, 129u }, /* heater */ \
    { 2u, 100u, 16u, 145u }, /* heater */ \
    { 2u, 120u, 16u, 161u }, /* heater */ \
    { 2u, 140u, 16u, 177u }, /* heater */ \
    { 3u, 0u, 3u, 193u }, /* vacuum */ \
    { 3u, 10u, 1u, 196u }, /* vacuum */ \
    { 4u, 0u, 3u, 197u }, /* motion */ \
    { 4u, 10u, 1u, 200u }, /* motion */ \
    { 4u, 20u, 4u, 201u }, /* motion */ \
    { 4u, 30u, 2u, 205u }, /* motion */ \
    { 4u, 40u, 2u, 207u }, /* motion */ \
}
#define ALD_PDO_RX_SEG_COUNT 30u

#define ALD_PDO_TX_SEGS { \
    { 0u, 0u, 9u, 0u }, /* main */ \
    { 0u, 10u, 6u, 9u }, /* main */ \
    { 0u, 30u, 3u, 15u }, /* main */ \
    { 0u, 40u, 6u, 18u }, /* main */ \
    { 0u, 100u, 18u, 24u }, /* main */ \
    { 0u, 120u, 3u, 42u }, /* main */ \
    { 0u, 124u, 3u, 45u }, /* main */ \
    { 1u, 0u, 9u, 48u }, /* process */ \
    { 1u, 10u, 1u, 57u }, /* process */ \
    { 1u, 20u, 1u, 58u }, /* process */ \
    { 1u, 30u, 9u, 59u }, /* process */ \
    { 1u, 40u, 2u, 68u }, /* process */ \
    { 1u, 50u, 1u, 70u }, /* process */ \
    { 2u, 0u, 9u, 71u }, /* heater */ \
    { 2u, 10u, 1u, 80u }, /* heater */ \
    { 2u, 20u, 1u, 81u }, /* heater */ \
    { 2u, 30u, 16u, 82u }, /* heater */ \
    { 2u, 50u, 16u, 98u }, /* heater */ \
    { 3u, 0u, 9u, 114u }, /* vacuum */ \
    { 3u, 10u, 1u, 123u }, /* vacuum */ \
    { 3u, 20u, 2u, 124u }, /* vacuum */ \
    { 3u, 30u, 8u, 126u }, /* vacuum */ \
    { 4u, 0u, 9u, 134u }, /* motion */ \
    { 4u, 10u, 1u, 143u }, /* motion */ \
    { 4u, 20u, 1u, 144u }, /* motion */ \
    { 4u, 30u, 4u, 145u }, /* motion */ \
    { 4u, 40u, 2u, 149u }, /* motion */ \
}
#define ALD_PDO_TX_SEG_COUNT 27u
