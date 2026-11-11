/*
 * ecat_igh.c — IgH EtherLAB 主站（需要 Linux + ec_master 核心模組 + libethercat）
 *
 * 只接一台從站：主控板（位置 0，Vendor / Product 見 pdo_map.h）。
 * RxPDO 0x1600 = 0x7000:1..N（HR），TxPDO 0x1A00 = 0x6000:1..M（IR），每個 16 bit。
 * SM2 開看門狗：主站停止送資料 → 主控板的 SSC 掉出 OP → 主控板進安全狀態。
 */
#ifdef HAVE_IGH
#include "ecat_master.h"
#include "pdo_map.h"
#include <ecrt.h>
#include <stdio.h>

static ec_master_t *s_master;
static ec_domain_t *s_domain;
static ec_slave_config_t *s_sc;
static uint8_t *s_pd;
static unsigned s_off_rx, s_off_tx;

static ec_pdo_entry_info_t s_rx_entries[ALD_PDO_RX_WORDS];
static ec_pdo_entry_info_t s_tx_entries[ALD_PDO_TX_WORDS];
static ec_pdo_info_t s_pdos[2];
static ec_sync_info_t s_syncs[5];

static bool igh_open(void)
{
    for (unsigned i = 0; i < ALD_PDO_RX_WORDS; i++) s_rx_entries[i] = (ec_pdo_entry_info_t){ ALD_PDO_RX_OBJ, (uint8_t)(i + 1), 16 };
    for (unsigned i = 0; i < ALD_PDO_TX_WORDS; i++) s_tx_entries[i] = (ec_pdo_entry_info_t){ ALD_PDO_TX_OBJ, (uint8_t)(i + 1), 16 };
    s_pdos[0] = (ec_pdo_info_t){ ALD_PDO_RX_INDEX, ALD_PDO_RX_WORDS, s_rx_entries };
    s_pdos[1] = (ec_pdo_info_t){ ALD_PDO_TX_INDEX, ALD_PDO_TX_WORDS, s_tx_entries };
    s_syncs[0] = (ec_sync_info_t){ 0, EC_DIR_OUTPUT, 0, NULL, EC_WD_DISABLE };
    s_syncs[1] = (ec_sync_info_t){ 1, EC_DIR_INPUT, 0, NULL, EC_WD_DISABLE };
    s_syncs[2] = (ec_sync_info_t){ 2, EC_DIR_OUTPUT, 1, &s_pdos[0], EC_WD_ENABLE };
    s_syncs[3] = (ec_sync_info_t){ 3, EC_DIR_INPUT, 1, &s_pdos[1], EC_WD_DISABLE };
    s_syncs[4] = (ec_sync_info_t){ 0xff };

    if (!(s_master = ecrt_request_master(0))) { fprintf(stderr, "ecat: 取不到 master 0（ec_master 模組有載入嗎？）\n"); return false; }
    if (!(s_domain = ecrt_master_create_domain(s_master))) return false;
    if (!(s_sc = ecrt_master_slave_config(s_master, 0, 0, ALD_ECAT_VENDOR_ID, ALD_ECAT_PRODUCT_CODE))) return false;
    if (ecrt_slave_config_pdos(s_sc, EC_END, s_syncs)) { fprintf(stderr, "ecat: PDO 設定失敗\n"); return false; }
    int rx = ecrt_slave_config_reg_pdo_entry(s_sc, ALD_PDO_RX_OBJ, 1, s_domain, NULL);
    int tx = ecrt_slave_config_reg_pdo_entry(s_sc, ALD_PDO_TX_OBJ, 1, s_domain, NULL);
    if (rx < 0 || tx < 0) { fprintf(stderr, "ecat: PDO entry 註冊失敗\n"); return false; }
    s_off_rx = (unsigned)rx; s_off_tx = (unsigned)tx;
    if (ecrt_master_activate(s_master)) return false;
    return (s_pd = ecrt_domain_data(s_domain)) != NULL;
}

static bool igh_cycle(const uint16_t *rx, uint16_t *tx)
{
    ecrt_master_receive(s_master);
    ecrt_domain_process(s_domain);

    ec_domain_state_t ds; ec_slave_config_state_t ss;
    ecrt_domain_state(s_domain, &ds);
    ecrt_slave_config_state(s_sc, &ss);
    bool ok = ds.wc_state == EC_WC_COMPLETE && ss.operational;

    for (unsigned i = 0; i < ALD_PDO_TX_WORDS; i++) tx[i] = EC_READ_U16(s_pd + s_off_tx + 2 * i);
    for (unsigned i = 0; i < ALD_PDO_RX_WORDS; i++) EC_WRITE_U16(s_pd + s_off_rx + 2 * i, rx[i]);

    ecrt_domain_queue(s_domain);
    ecrt_master_send(s_master);
    return ok;
}

static void igh_close(void) { if (s_master) ecrt_release_master(s_master); s_master = NULL; }

const ecat_master_t ecat_igh = { "igh", igh_open, igh_cycle, igh_close };
#endif
