#!/bin/bash
# IPC 安裝 / 更新（在 ARK-1222 上用 root 執行，解開安裝包後）：
#   sudo ./install.sh            正式：ald-ecat 連 EtherCAT 主控板
#   sudo ./install.sh --sim      展示 / 教育訓練：ald-ecat 跑虛擬機台（不需要主控板）
#   ROOT=/tmp/x ./install.sh     裝到暫存根目錄（測試安裝腳本用，不碰 systemd / 帳號）
#
# 放置：/opt/ald（程式，每次更新覆蓋）、/etc/ald/machine.json（機台設定，已存在就不覆蓋，新版另存 .new）、
#       /var/lib/ald（資料庫、配方、晶圓位置、記錄、匯出，更新不動）
set -euo pipefail
SRC="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-}"
SIM=0
[ "${1:-}" = "--sim" ] && SIM=1
if [ -z "$ROOT" ] && [ "$(id -u)" != 0 ]; then echo "請用 sudo 執行"; exit 1; fi

echo "== 程式 → $ROOT/opt/ald（$(cat "$SRC/VERSION")）"
mkdir -p "$ROOT/opt/ald"
rm -rf "$ROOT/opt/ald/bin" "$ROOT/opt/ald/hmi"
cp -a "$SRC/bin" "$SRC/hmi" "$ROOT/opt/ald/"
install -m 755 "$SRC/files/ald-hmi.sh" "$ROOT/opt/ald/bin/"
cp "$SRC/VERSION" "$ROOT/opt/ald/"

echo "== 機台設定 → $ROOT/etc/ald/machine.json"
mkdir -p "$ROOT/etc/ald"
NEWCFG="$ROOT/etc/ald/machine.json.new"
# 安裝位置的資料路徑改成絕對路徑（/var/lib/ald）
sed -e 's#"path": "data/ald.db"#"path": "/var/lib/ald/ald.db"#' \
    -e 's#"waferMapPath": "data/wafer.json"#"waferMapPath": "/var/lib/ald/wafer.json"#' \
    "$SRC/config/machine.json" > "$NEWCFG"
if [ -f "$ROOT/etc/ald/machine.json" ]; then
    echo "   已有 machine.json，保留不動；新版預設值放在 machine.json.new，請比對"
else
    mv "$NEWCFG" "$ROOT/etc/ald/machine.json"
fi

echo "== 資料 → $ROOT/var/lib/ald"
mkdir -p "$ROOT/var/lib/ald/recipes" "$ROOT/var/lib/ald/log" "$ROOT/var/lib/ald/export"
for f in "$SRC"/recipes/*.json; do
    [ -f "$ROOT/var/lib/ald/recipes/$(basename "$f")" ] || install -m 644 "$f" "$ROOT/var/lib/ald/recipes/"
done

echo "== 開機自動啟動"
ARGS=""
[ $SIM = 1 ] && ARGS="--sim"
mkdir -p "$ROOT/etc/systemd/system" "$ROOT/etc/xdg/autostart"
sed "s#@ECAT_ARGS@#$ARGS#" "$SRC/files/ald-ecat.service" > "$ROOT/etc/systemd/system/ald-ecat.service"
install -m 644 "$SRC/files/ald-hmi.desktop" "$ROOT/etc/xdg/autostart/"

if [ -z "$ROOT" ]; then
    # 人機用桌面登入的帳號執行，要能寫 /var/lib/ald
    OPER="${SUDO_USER:-}"
    if [ -n "$OPER" ] && [ "$OPER" != root ]; then chown -R "$OPER": /var/lib/ald; fi
    systemctl daemon-reload
    systemctl enable ald-ecat.service
    systemctl restart ald-ecat.service
    echo "== ald-ecat：$(systemctl is-active ald-ecat.service)"
fi
echo "== 完成。$( [ $SIM = 1 ] && echo '（模擬模式：ald-ecat --sim）')"
echo "   人機在桌面登入後自動開啟；記錄在 /var/lib/ald/log/hmi.log；ald-ecat 記錄：journalctl -u ald-ecat"
