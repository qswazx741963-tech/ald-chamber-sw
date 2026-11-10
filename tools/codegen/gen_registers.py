#!/usr/bin/env python3
"""
ALD Chamber 暫存器產生器（map_version 2：主控板一張表、依槽位分區）

讀 shared/modbus/registers.yaml，產生：
  firmware/common/registers/registers_common.h     各槽共用表頭（C）
  firmware/common/registers/registers_<board>.h    每種板的區內位址（C，巨集前綴 = prefix）
  firmware/common/registers/slot_table.h           槽位 / 板型 / 大小總表（主控板用）
  ipc/src/Ald.Comm/Generated/Registers.g.cs        C#（絕對位址 = slot × slot_stride + 區內位址）
  shared/modbus/register-map.md                    給人看 / Modbus Poll 對照

用法：
  python tools/codegen/gen_registers.py           產生檔案
  python tools/codegen/gen_registers.py --check   只檢查產生檔是否最新（CI 用）
"""
import argparse
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "shared/modbus/registers.yaml"
OUT_H_DIR = ROOT / "firmware/common/registers"
OUT_CS = ROOT / "ipc/src/Ald.Comm/Generated/Registers.g.cs"
OUT_MD = ROOT / "shared/modbus/register-map.md"

BANNER = "自動產生，請勿手改。來源：shared/modbus/registers.yaml"
WIDTH = {"u16": 1, "i16": 1, "i32": 2}
AREAS = (("input", "IR", "Input Register (FC04)"), ("holding", "HR", "Holding Register (FC03/FC06/FC16)"))


# ---------------------------------------------------------------- 讀取與檢查
def _normalize(r):
    r.setdefault("type", "u16")
    r.setdefault("count", 1)
    r.setdefault("scale", 1)
    r["span"] = WIDTH.get(r["type"], 1) * r["count"]


def load():
    data = yaml.safe_load(SRC.read_text(encoding="utf-8"))
    stride, max_slots = data["slot_stride"], data["max_slots"]
    types = data["board_types"]
    errors = []
    for area, _, _ in AREAS:
        for r in data["common"].get(area, []):
            _normalize(r)
    slots, prefixes = {}, set()
    for bname, board in data["boards"].items():
        s = board.get("slot")
        if not isinstance(s, int) or not 0 <= s < max_slots:
            errors.append(f"{bname}: slot 必須是 0–{max_slots - 1}")
        elif s in slots:
            errors.append(f"{bname}: slot {s} 與 {slots[s]} 重複")
        slots[s] = bname
        if board.get("board_type") not in types:
            errors.append(f"{bname}: board_type {board.get('board_type')} 不在 board_types")
        if board.get("prefix") in prefixes or not board.get("prefix"):
            errors.append(f"{bname}: prefix 缺少或重複")
        prefixes.add(board.get("prefix"))
        for area, _, _ in AREAS:
            used, names = {}, set()
            for r in data["common"].get(area, []) + board.get(area, []):
                _normalize(r)
                if r["type"] not in WIDTH:
                    errors.append(f"{bname}.{r['name']}: 未知 type {r['type']}")
                if r["name"] in names:
                    errors.append(f"{bname}.{area}: 名稱重複 {r['name']}")
                names.add(r["name"])
                if len(r.get("bits", [])) > 16:
                    errors.append(f"{bname}.{r['name']}: bits 超過 16 個")
                if r["addr"] + r["span"] > stride:
                    errors.append(f"{bname}.{r['name']}: 超出槽位範圍（區內位址需 < {stride}）")
                for a in range(r["addr"], r["addr"] + r["span"]):
                    if a in used:
                        errors.append(f"{bname}.{area}: 位址 {a} 重疊（{used[a]} 與 {r['name']}）")
                    used[a] = r["name"]
    if errors:
        sys.exit("registers.yaml 有錯誤：\n  " + "\n  ".join(errors))
    return data


def regs_of(data, bname, area, with_common=True):
    own = data["boards"][bname].get(area, [])
    return (data["common"].get(area, []) + own) if with_common else own


def area_size(data, bname, area):
    regs = regs_of(data, bname, area)
    return max(r["addr"] + r["span"] for r in regs) if regs else 0


def pascal(name):
    return "".join(p.capitalize() for p in name.lower().split("_"))


def fmt_num(v):
    return repr(float(v)) if isinstance(v, float) or v != int(v) else str(int(v))


# ---------------------------------------------------------------- C 標頭
def _h_regs(L, prefix, regs):
    for r in regs:
        n = f"{prefix}_{r['name']}"
        L.append(f"#define {n:<44} {r['addr']}u  /* {r['type']}"
                 + (f" x{r['count']}" if r["count"] > 1 else "")
                 + (f" [{r['unit']}]" if "unit" in r else "")
                 + (f" scale {r['scale']}" if r["scale"] != 1 else "") + " */")
        if r["count"] > 1:
            L.append(f"#define {n + '_COUNT':<44} {r['count']}u")
        if "min" in r:
            L.append(f"#define {n + '_MIN_RAW':<44} {round(r['min'] / r['scale'])}")
        if "max" in r:
            L.append(f"#define {n + '_MAX_RAW':<44} {round(r['max'] / r['scale'])}")
        for i, bit in enumerate(r.get("bits", [])):
            L.append(f"#define {n + '_BIT_' + bit:<44} {i}u")
        for k, v in r.get("values", {}).items():
            L.append(f"#define {n + '_' + k:<44} {v}u")


def gen_h_common(data):
    L = [f"/* {BANNER} */", "#pragma once", "/* 各槽共用表頭（區內位址，每塊板都一樣） */", ""]
    for area, prefix, title in AREAS:
        L.append(f"/* ---- {title} */")
        _h_regs(L, f"COM_{prefix}", data["common"].get(area, []))
        L.append("")
    return "\n".join(L)


def gen_h_board(data, bname):
    b = data["boards"][bname]
    p = b["prefix"]
    L = [f"/* {BANNER} */", "#pragma once", '#include "registers_common.h"', "",
         f"/* {bname}: {b.get('desc', '')} */",
         f"#define {p}_DEFAULT_SLOT {b['slot']}u",
         f"#define {p}_BOARD_TYPE   {data['board_types'][b['board_type']]}u", ""]
    for area, prefix, title in AREAS:
        L.append(f"/* ---- {title}（區內位址；共用表頭見 COM_{prefix}_*） */")
        L.append(f"#define {p}_{prefix}_SIZE {area_size(data, bname, area)}u")
        _h_regs(L, f"{p}_{prefix}", regs_of(data, bname, area, with_common=False))
        L.append("")
    return "\n".join(L)


def gen_h_slot_table(data):
    L = [f"/* {BANNER} */", "#pragma once", "#include <stdint.h>", "",
         f"#define ALD_MAP_VERSION  {data['map_version']}u",
         f"#define ALD_SLOT_STRIDE  {data['slot_stride']}u",
         f"#define ALD_MAX_SLOTS    {data['max_slots']}u",
         "#define ALD_SLOT_NONE    0xFFu", ""]
    for k, v in data["board_types"].items():
        L.append(f"#define BOARD_TYPE_{k:<10} {v}u")
    L += ["", "/* 預設槽位配置：{ 槽號, 板型, IR 大小, HR 大小 } */",
          "typedef struct { uint8_t slot; uint8_t board_type; uint16_t ir_size; uint16_t hr_size; } ald_slot_def_t;",
          "#define ALD_SLOT_TABLE { \\"]
    for bname, b in sorted(data["boards"].items(), key=lambda kv: kv[1]["slot"]):
        L.append(f"    {{ {b['slot']}u, BOARD_TYPE_{b['board_type']}, "
                 f"{area_size(data, bname, 'input')}u, {area_size(data, bname, 'holding')}u }}, /* {bname} */ \\")
    L += ["}", f"#define ALD_SLOT_TABLE_LEN {len(data['boards'])}u", ""]
    return "\n".join(L)


# ---------------------------------------------------------------- C#
def gen_cs(data):
    stride = data["slot_stride"]
    L = [f"// <auto-generated> {BANNER} </auto-generated>",
         "namespace Ald.Comm.Generated;", "",
         "public static class RegisterMap", "{",
         f"    public const ushort MapVersion = {data['map_version']};",
         f"    public const ushort SlotStride = {stride};",
         f"    public const int MaxSlots = {data['max_slots']};", "}", "",
         "public enum BoardType : ushort", "{"]
    for k, v in data["board_types"].items():
        L.append(f"    {pascal(k)} = {v},")
    L += ["}", ""]
    for bname, b in data["boards"].items():
        cls = pascal(bname) + "Board"
        base = b["slot"] * stride
        L += [f"/// <summary>{b.get('desc', '')}（槽 {b['slot']}，位址 = {base} + 區內位址）</summary>",
              f"public static class {cls}", "{",
              f"    public const int Slot = {b['slot']};",
              f"    public const BoardType Type = BoardType.{pascal(b['board_type'])};",
              f"    public const ushort Base = {base};", ""]
        for area, prefix, title in AREAS:
            regs = regs_of(data, bname, area)
            cname = "Input" if prefix == "IR" else "Holding"
            L += [f"    /// <summary>{title}，以下皆為絕對位址</summary>", f"    public static class {cname}", "    {",
                  f"        public const ushort Size = {area_size(data, bname, area)};"]
            for r in regs:
                p = pascal(r["name"])
                doc = r.get("desc", "") + (f" [{r['unit']}]" if "unit" in r else "")
                if doc.strip():
                    L.append(f"        /// <summary>{doc.strip()}</summary>")
                L.append(f"        public const ushort {p} = {base + r['addr']};")
                if r["count"] > 1:
                    L.append(f"        public const ushort {p}Count = {r['count']};")
                if r["scale"] != 1:
                    L.append(f"        public const double {p}Scale = {fmt_num(r['scale'])};")
                if "min" in r:
                    L.append(f"        public const double {p}Min = {fmt_num(r['min'])};")
                if "max" in r:
                    L.append(f"        public const double {p}Max = {fmt_num(r['max'])};")
                if r.get("bits"):
                    L.append(f"        [System.Flags] public enum {p}Bits : ushort")
                    L.append("        {")
                    for i, bit in enumerate(r["bits"]):
                        L.append(f"            {pascal(bit)} = 1 << {i},")
                    L.append("        }")
                if r.get("values"):
                    L.append(f"        public enum {p}Values : ushort")
                    L.append("        {")
                    for k, v in r["values"].items():
                        L.append(f"            {pascal(k)} = {v},")
                    L.append("        }")
            L += ["    }", ""]
        L += ["}", ""]
    return "\n".join(L)


# ---------------------------------------------------------------- Markdown
def gen_md(data):
    stride = data["slot_stride"]
    L = [f"# 暫存器表（map_version {data['map_version']}）", "", f"> {BANNER}", "",
         "IPC 透過 Modbus TCP 連主控板（Unit ID 1）。**絕對位址 = 槽號 × "
         f"{stride} + 區內位址**。執行板韌體只用區內位址。", "",
         "| 槽 | 板 | 板型 | IR 範圍 | HR 範圍 |", "| --- | --- | --- | --- | --- |"]
    for bname, b in sorted(data["boards"].items(), key=lambda kv: kv[1]["slot"]):
        base = b["slot"] * stride
        L.append(f"| {b['slot']} | {bname} | {b['board_type']} | {base}–{base + area_size(data, bname, 'input') - 1} | "
                 f"{base}–{base + area_size(data, bname, 'holding') - 1} |")
    L.append("")
    for bname, b in sorted(data["boards"].items(), key=lambda kv: kv[1]["slot"]):
        base = b["slot"] * stride
        L += [f"## 槽 {b['slot']}：{bname}", "", b.get("desc", ""), ""]
        for area, prefix, title in AREAS:
            L += [f"### {title}", "", "| 絕對位址 | 區內 | 名稱 | 型別 | 數量 | 倍率 | 單位 | 範圍 | 說明 |",
                  "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
            for r in regs_of(data, bname, area):
                end = r["addr"] + r["span"] - 1

                def rng(off):
                    return f"{off + r['addr']}" if end == r["addr"] else f"{off + r['addr']}–{off + end}"
                lim = f"{r.get('min', '')}–{r.get('max', '')}" if "min" in r or "max" in r else ""
                note = r.get("desc", "")
                if r.get("bits"):
                    note += " bits: " + ", ".join(f"{i}={x}" for i, x in enumerate(r["bits"]))
                if r.get("values"):
                    note += " 值: " + ", ".join(f"{v}={k}" for k, v in r["values"].items())
                L.append(f"| {prefix} {rng(base)} | {rng(0)} | {r['name']} | {r['type']} | {r['count']} | "
                         f"{r['scale'] if r['scale'] != 1 else ''} | {r.get('unit', '')} | {lim} | {note.strip()} |")
            L.append("")
    return "\n".join(L)


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只檢查，不寫檔")
    args = ap.parse_args()

    data = load()
    outputs = {
        OUT_CS: gen_cs(data),
        OUT_MD: gen_md(data),
        OUT_H_DIR / "registers_common.h": gen_h_common(data),
        OUT_H_DIR / "slot_table.h": gen_h_slot_table(data),
    }
    for bname in data["boards"]:
        outputs[OUT_H_DIR / f"registers_{bname}.h"] = gen_h_board(data, bname)

    stale = []
    for path, text in outputs.items():
        old = path.read_text(encoding="utf-8") if path.exists() else None
        if old != text:
            stale.append(path.relative_to(ROOT))
            if not args.check:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8", newline="\n")

    if args.check:
        if stale:
            sys.exit("以下產生檔不是最新，請執行 gen_registers.py：\n  " + "\n  ".join(map(str, stale)))
        print("產生檔皆為最新")
    else:
        print(f"已更新 {len(stale)} 個檔案" if stale else "沒有變更")
        for p in stale:
            print("  ", p)


if __name__ == "__main__":
    main()
