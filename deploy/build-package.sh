#!/bin/bash
# 產生 IPC 安裝包：dist/ald-ipc-<版本>.tar.gz
#   deploy/build-package.sh                     # 只有模擬（沒裝 IgH 的開發機）
#   IGH=/opt/etherlab deploy/build-package.sh   # 正式（連結 IgH libethercat）
# 內容：ald-ecat、人機（.NET 自帶執行環境，IPC 不用另裝 .NET）、預設 config、配方、systemd / 自動啟動檔、install.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VER="${VER:-$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || echo dev)}"
OUT="$ROOT/dist/ald-ipc-$VER"
rm -rf "$OUT"
mkdir -p "$OUT/bin" "$OUT/hmi" "$OUT/config" "$OUT/recipes" "$OUT/files"

echo "== ald-ecat ${IGH:+(IgH: $IGH)}"
make -C "$ROOT/ipc/ecat-master" clean >/dev/null
make -C "$ROOT/ipc/ecat-master" ${IGH:+IGH="$IGH"}
install -m 755 "$ROOT/ipc/ecat-master/ald-ecat" "$OUT/bin/"

echo "== 人機（linux-x64，self-contained）"
dotnet publish "$ROOT/ipc/src/Ald.Hmi/Ald.Hmi.csproj" -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=false -o "$OUT/hmi" -v q --nologo
rm -rf "$OUT/hmi/config" "$OUT/hmi/recipes"          # config / 配方放 /etc/ald、/var/lib/ald

install -m 644 "$ROOT/ipc/config/machine.json" "$OUT/config/"
install -m 644 "$ROOT"/recipes/*.json "$OUT/recipes/"
install -m 644 "$ROOT"/deploy/files/* "$OUT/files/"
install -m 755 "$ROOT/deploy/install.sh" "$OUT/"
echo "$VER ${IGH:+igh}${IGH:-sim-only} $(date '+%F %T')" > "$OUT/VERSION"

tar -C "$ROOT/dist" -czf "$ROOT/dist/ald-ipc-$VER.tar.gz" "ald-ipc-$VER"
echo "== 完成：dist/ald-ipc-$VER.tar.gz"
