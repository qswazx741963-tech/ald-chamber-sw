#!/bin/sh
# 人機啟動器：登入桌面後自動執行（/etc/xdg/autostart/ald-hmi.desktop）。
# 人機被關掉或當掉 3 秒後重開；畫面關掉期間主控板看門狗會讓全機進安全狀態。
LOG=/var/lib/ald/log/hmi.log
mkdir -p /var/lib/ald/log 2>/dev/null
cd /var/lib/ald || exit 1
while true; do
    echo "=== $(date '+%F %T') 啟動人機" >> "$LOG"
    /opt/ald/hmi/AldChamber --config /etc/ald/machine.json --recipes /var/lib/ald/recipes >> "$LOG" 2>&1
    echo "=== $(date '+%F %T') 人機結束（代碼 $?），3 秒後重開" >> "$LOG"
    sleep 3
done
