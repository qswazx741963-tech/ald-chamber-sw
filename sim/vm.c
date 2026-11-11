/*
 * vm.c — 整機模擬器核心：5 塊虛擬板 + 虛擬背板（CAN FD）+ SAFE 線 + 主控板 UART
 *
 * 各板韌體在連結時被加上前綴（n0_ = 主控板、n1_~n4_ = 槽 1~4），見 Makefile。
 */
#include "vm.h"
#include "fieldbus_if.h"
#include "hal_port.h"
#include "slot_table.h"
#include "world.h"
#include <string.h>

/* ---- 加了前綴的韌體進入點 ---- */
extern void n0_main_app_init(const fieldbus_ops_t *bus);
extern void n0_main_app_loop(void);
extern const fieldbus_ops_t n0_fieldbus_ethercat;
extern void n0_ecat_rx_to_image(const uint16_t *rx);
extern void n0_ecat_image_to_tx(uint16_t *tx);
#define EXEC_DECL(k) extern void n##k##_exec_app_init(void); extern void n##k##_exec_app_loop(void); \
                     extern void n##k##_exec_app_tick_1ms(void);
EXEC_DECL(1) EXEC_DECL(2) EXEC_DECL(3) EXEC_DECL(4)

typedef struct { void (*init)(void), (*loop)(void), (*tick)(void); } exec_fw_t;
static const exec_fw_t s_exec[VM_NODES] = {
    { 0 },
    { n1_exec_app_init, n1_exec_app_loop, n1_exec_app_tick_1ms },
    { n2_exec_app_init, n2_exec_app_loop, n2_exec_app_tick_1ms },
    { n3_exec_app_init, n3_exec_app_loop, n3_exec_app_tick_1ms },
    { n4_exec_app_init, n4_exec_app_loop, n4_exec_app_tick_1ms },
};

static vm_node_t s_nodes[VM_NODES];
static uint32_t  s_now;
static bool      s_ecat_op;
world_t g_world;

vm_node_t *vm_node(int n) { return &s_nodes[n]; }
bool vm_ecat_op(void) { return s_ecat_op; }
uint32_t vm_now_ms(void) { return s_now; }
vm_knobs_t *vm_knobs(void) { return &g_world.knobs; }

/* 背板 CAN FD：廣播給其他所有節點（跟實體匯流排一樣，各板自己依 ID 過濾） */
void vm_can_tx(int from, uint16_t id, const uint8_t *d, uint8_t len)
{
    if (len > 64) len = 64;
    for (int i = 0; i < VM_NODES; i++) {
        if (i == from) continue;
        vm_node_t *n = &s_nodes[i];
        int next = (n->can_tail + 1) % VM_CANQ;
        if (next == n->can_head) { n->can_drop++; continue; }   /* 佇列滿 = 掉包（實機也可能發生） */
        n->canq[n->can_tail].id = id;
        n->canq[n->can_tail].len = len;
        memcpy(n->canq[n->can_tail].d, d, len);
        n->can_tail = next;
    }
}

/* SAFE 線：開集極線，任何一塊板拉低或 EMO 按下 = 低 */
bool vm_safe_line(void)
{
    if (!g_world.knobs.emo_ok) return false;
    for (int i = 0; i < VM_NODES; i++) if (s_nodes[i].safe_pull) return false;
    return true;
}

void vm_uart_from_board(uint8_t port, const char *s, uint16_t len)
{
    if (port == HAL_UART_LOADLOCK) slk_emu_rx(s, len);
    /* RF：TODO 鎧泰 RF 協議確認後加模擬 */
}

void vm_uart_to_board(uint8_t port, const char *s, uint16_t len)
{
    vm_node_t *n = &s_nodes[0];
    if (port >= VM_UART_PORTS) return;
    for (uint16_t i = 0; i < len; i++) {
        int next = (n->urx_tail[port] + 1) % VM_UART_BUF;
        if (next == n->urx_head[port]) return;   /* 溢位丟掉 */
        n->urx[port][n->urx_tail[port]] = s[i];
        n->urx_tail[port] = next;
    }
}

void vm_init(void)
{
    memset(s_nodes, 0, sizeof s_nodes);
    memset(&g_world, 0, sizeof g_world);
    vm_knobs_t *k = &g_world.knobs;
    k->emo_ok = k->cda_ok = k->pm_lid_closed = k->ll_lid_closed = true;
    k->pm_leak_torr_l_s = 1e-5f;
    s_now = 0;
    s_ecat_op = false;

    static const ald_slot_def_t slots[] = ALD_SLOT_TABLE;
    for (unsigned i = 0; i < ALD_SLOT_TABLE_LEN && slots[i].slot < VM_NODES; i++) {
        s_nodes[slots[i].slot].slot = slots[i].slot;
        s_nodes[slots[i].slot].board_type = slots[i].board_type;
    }
    plant_init();
    slk_emu_init();
    plant_step(0.0f);                    /* 先把感測器值填好再開機 */
    n0_main_app_init(&n0_fieldbus_ethercat);
    for (int i = 1; i < VM_NODES; i++) s_exec[i].init();
}

void vm_step(const uint16_t *rx, uint16_t *tx)
{
    s_now++;
    for (int i = 0; i < VM_NODES; i++) s_nodes[i].now = s_now;

    s_ecat_op = rx != NULL;
    if (rx) n0_ecat_rx_to_image(rx);     /* SSC APPL_OutputMapping */

    plant_step(0.001f);
    slk_emu_step(s_now, 0.001f);

    for (int i = 1; i < VM_NODES; i++) s_exec[i].tick();
    n0_main_app_loop();
    for (int i = 1; i < VM_NODES; i++) s_exec[i].loop();

    if (tx) n0_ecat_image_to_tx(tx);     /* SSC APPL_InputMapping */
}

void vm_view(vm_view_t *v)
{
    memset(v, 0, sizeof *v);
    v->pm_torr = g_world.pm_torr;
    v->ll_torr = g_world.ll_torr;
    v->foreline_torr = g_world.foreline_torr;
    v->turbo_pct = g_world.turbo_pct;
    v->gv_pos = g_world.gv_pos;
    v->lift_pos = g_world.lift_pos;
    memcpy(v->temp_c, g_world.temp_c, sizeof v->temp_c);
    for (int i = 0; i < 8; i++) v->mfc_sccm[i] = plant_mfc_sccm(i);
    v->drypump_running = g_world.drypump_t >= 2.0f;
    v->sv_state = slk_emu_sv_state();
    v->arm_pos = slk_emu_arm_pos();
    v->ll_rough = slk_emu_rough();
    v->ll_vent = slk_emu_vent();
}
