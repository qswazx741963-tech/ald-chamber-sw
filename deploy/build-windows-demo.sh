#!/usr/bin/env bash
# 打包「Windows 試操作版」：虛擬機台（ald-ecat.exe，Windows 原生）+ 人機（AldChamber.exe，自帶 .NET）
# 給沒有 Linux 的電腦看畫面、試操作用；正式機台請用 deploy/build-package.sh（Linux）。
#
# 需要：mingw-w64（apt install gcc-mingw-w64-x86-64）、dotnet 8 SDK
# 用法：deploy/build-windows-demo.sh [輸出資料夾，預設 dist/]
# 產出：<輸出>/ALD-Demo-Windows.zip，解壓後雙擊 start-demo.bat
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT=$(mkdir -p "${1:-$ROOT/dist}" && cd "${1:-$ROOT/dist}" && pwd)
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
CC=x86_64-w64-mingw32-gcc-posix
PKG="$TMP/ALD-Demo"
mkdir -p "$PKG/sim" "$PKG/hmi"

echo "== 虛擬機台（Windows）"
# 另外複製一份編譯，不弄髒 sim/build（Linux 版）。-mcmodel=small：避免 mingw 的 .refptr 符號被 objcopy 改名後對不上
cp -r "$ROOT/sim" "$ROOT/firmware" "$TMP/"
rm -rf "$TMP/sim/build"
make -s -C "$TMP/sim" build/libaldvm.a CC=$CC LD=x86_64-w64-mingw32-ld OBJCOPY=x86_64-w64-mingw32-objcopy \
    NM=x86_64-w64-mingw32-nm CFLAGS="-std=c11 -O2 -Wall -Wextra -Werror -Wno-unused-parameter -mcmodel=small"
E="$ROOT/ipc/ecat-master"
$CC -std=c11 -O2 -mcmodel=small -Wall -Wextra -Werror -I"$E/src" -I"$ROOT/firmware/common/registers" -I"$TMP/sim" \
    -o "$PKG/sim/ald-ecat.exe" "$E/src/main_win.c" "$E/src/bridge.c" "$E/src/ecat_sim.c" "$TMP/sim/build/libaldvm.a" \
    -lws2_32 -lwinmm -lm -static

echo "== 人機（win-x64，自帶 .NET）"
dotnet publish "$ROOT/ipc/src/Ald.Hmi" -c Release -r win-x64 --self-contained true -o "$PKG/hmi" -v q -nologo

cat > "$PKG/start-demo.bat" <<'EOF'
@echo off
rem ALD Chamber demo: start the simulator (10x speed) and the HMI
cd /d "%~dp0"
rem close an older simulator / HMI that is still running (otherwise the HMI may talk to the old version)
taskkill /f /im ald-ecat.exe >nul 2>&1
taskkill /f /im AldChamber.exe >nul 2>&1
start "ALD simulator - close this window to stop" sim\ald-ecat.exe --sim --speed 10 --port 1502
timeout /t 2 /nobreak >nul
cd hmi
start "" AldChamber.exe --port 1502
EOF
sed -i 's/$/\r/' "$PKG/start-demo.bat"

printf '\xEF\xBB\xBF' > "$PKG/說明.txt"
cat >> "$PKG/說明.txt" <<'EOF'
ALD Chamber 人機 試操作版（Windows）

1. 雙擊 start-demo.bat：會開兩個視窗
   - 黑色視窗 = 虛擬機台（正式韌體 + 物理模型，10 倍速：抽真空、升溫比實機快）
   - 人機畫面 = AldChamber
   第一次開 Windows 可能跳「Windows 已保護您的電腦」：按「其他資訊」→「仍要執行」。
2. 右上角選單 → 登入（admin / 出廠密碼）。
3. 按 Reset（剛開機會在安全狀態），出現黃條就按「恢復常駐加熱設定」。
4. 主頁 Go Online → Sequence ▾ → 自動跑片 → 選 sim_quick_test.json → 執行。
5. 結束：關人機，再關黑色視窗。

注意：
- 這是模擬，沒有接任何硬體。資料（事件記錄、履歷、晶圓位置）存在 hmi\data\，刪掉就回到初始狀態。
- 關掉黑色視窗但人機還開著 → 人機會顯示斷線；重開 start-demo.bat 即可（它會先關掉還開著的舊版虛擬機台 / 人機）。
- 換新版時請解壓到新的資料夾，並確認舊版的黑色視窗已經關掉；版本不同時人機警報會顯示「暫存器表版本不同」。
EOF
sed -i 's/$/\r/' "$PKG/說明.txt"

rm -f "$OUT/ALD-Demo-Windows.zip"
(cd "$TMP" && zip -qr "$OUT/ALD-Demo-Windows.zip" ALD-Demo)
ls -la "$OUT/ALD-Demo-Windows.zip"
