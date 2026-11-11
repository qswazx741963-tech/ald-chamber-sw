#include "bp_proto.h"
#include "hal_port.h"
#include <string.h>

static void put16(uint8_t *p, uint16_t v) { p[0] = (uint8_t)v; p[1] = (uint8_t)(v >> 8); }
static uint16_t get16(const uint8_t *p) { return (uint16_t)(p[0] | (p[1] << 8)); }

uint16_t bp_make_id(bp_type_t type, uint8_t slot, uint8_t chunk)
{
    return (uint16_t)((((unsigned)type & 0x7u) << 8) | ((slot & 0xFu) << 4) | (chunk & 0xFu));
}

void bp_parse_id(uint16_t id, bp_type_t *type, uint8_t *slot, uint8_t *chunk)
{
    *type = (bp_type_t)((id >> 8) & 0x7u);
    *slot = (uint8_t)((id >> 4) & 0xFu);
    *chunk = (uint8_t)(id & 0xFu);
}

uint8_t bp_fd_len(uint8_t len)
{
    static const uint8_t ok[] = {0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 20, 24, 32, 48, 64};
    for (unsigned i = 0; i < sizeof ok; i++)
        if (ok[i] >= len) return ok[i];
    return 0;
}

uint8_t bp_pack_regs(uint8_t *buf, uint16_t offset, const uint16_t *regs, uint8_t n)
{
    if (n == 0 || n > BP_REGS_PER_FRAME) return 0;
    uint8_t len = (uint8_t)(BP_HDR_LEN + 2u * n);
    uint8_t fd = bp_fd_len(len);
    memset(buf, 0, fd);
    put16(buf, offset);
    buf[2] = n;
    buf[3] = 0;
    for (uint8_t i = 0; i < n; i++) put16(buf + BP_HDR_LEN + 2u * i, regs[i]);
    return fd;
}

int bp_unpack_regs(const uint8_t *buf, uint8_t len, uint16_t *image, uint16_t image_size)
{
    if (len < BP_HDR_LEN) return -1;
    uint16_t off = get16(buf);
    uint8_t n = buf[2];
    if (n == 0 || n > BP_REGS_PER_FRAME) return -1;
    if (len < BP_HDR_LEN + 2u * n) return -1;
    if ((uint32_t)off + n > image_size) return -1;
    for (uint8_t i = 0; i < n; i++) image[off + i] = get16(buf + BP_HDR_LEN + 2u * i);
    return n;
}

uint8_t bp_pack_sync(uint8_t *buf, const bp_sync_t *s)
{
    memset(buf, 0, 8);
    put16(buf, s->heartbeat);
    put16(buf + 2, (uint16_t)s->time_ms);
    put16(buf + 4, (uint16_t)(s->time_ms >> 16));
    buf[6] = s->flags;
    return 8;
}

bool bp_unpack_sync(const uint8_t *buf, uint8_t len, bp_sync_t *s)
{
    if (len < 7) return false;
    s->heartbeat = get16(buf);
    s->time_ms = (uint32_t)get16(buf + 2) | ((uint32_t)get16(buf + 4) << 16);
    s->flags = buf[6];
    return true;
}

uint8_t bp_pack_hello(uint8_t *buf, const bp_hello_t *h)
{
    memset(buf, 0, 8);
    buf[0] = h->board_type;
    put16(buf + 1, h->fw_version);
    put16(buf + 3, h->map_version);
    return 8;
}

bool bp_unpack_hello(const uint8_t *buf, uint8_t len, bp_hello_t *h)
{
    if (len < 5) return false;
    h->board_type = buf[0];
    h->fw_version = get16(buf + 1);
    h->map_version = get16(buf + 3);
    return true;
}

int bp_send_image(bp_type_t type, uint8_t slot, const uint16_t *image, uint16_t size)
{
    uint8_t buf[BP_MAX_FRAME];
    int frames = 0;
    for (uint16_t off = 0, chunk = 0; off < size; off += BP_REGS_PER_FRAME, chunk++) {
        uint16_t n = (uint16_t)(size - off);
        if (n > BP_REGS_PER_FRAME) n = BP_REGS_PER_FRAME;
        uint8_t len = bp_pack_regs(buf, off, image + off, (uint8_t)n);
        if (hal_can_send(bp_make_id(type, slot, (uint8_t)chunk), buf, len) == 0) frames++;
    }
    return frames;
}
