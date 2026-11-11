# IPC（研華 ARK-1222）Linux 安裝說明（方案 B）

> 草案。實際步驟等拿到 ARK-1222 網卡晶片型號、實機驗證後再補齊。

## 一、作業系統

- Debian 12（bookworm）64-bit，安裝 PREEMPT_RT 核心：`apt install linux-image-rt-amd64`，重開機選 `-rt` 核心。
- 中文字型（人機畫面需要）：`apt install fonts-noto-cjk`。
- 開機參數（`/etc/default/grub` 的 `GRUB_CMDLINE_LINUX`）：保留一顆 CPU 給 EtherCAT 即時執行緒
  `isolcpus=3 nohz_full=3 rcu_nocbs=3`（ARK-1222 核心數確認後調整）。
- BIOS：關閉 C-state / SpeedStep / 省電，避免延遲抖動。

## 二、IgH EtherLAB 主站

1. 下載原始碼：`https://gitlab.com/etherlab.org/ethercat`（stable-1.6 分支），GPLv2 / LGPL。
2. 依網卡選驅動：
   | 網卡 | 驅動 | 即時性 |
   | --- | --- | --- |
   | Intel i210 / i211 | `--enable-igb` | 最好（原生） |
   | Intel 82574 / i217 / i219 | `--enable-e1000e` | 好（原生） |
   | 其他 | `--enable-generic` | 堪用（走一般網路堆疊） |
3. 編譯安裝：`./bootstrap && ./configure --prefix=/opt/etherlab --enable-igb --disable-8139too && make && make modules && sudo make install modules_install`
4. `/etc/ethercat.conf`：`MASTER0_DEVICE="<LAN2 的 MAC>"`、`DEVICE_MODULES="igb"`，`systemctl enable --now ethercat`。
5. 確認：`ethercat slaves` 應看到主控板（ALD-MAIN）。

## 三、安裝包（ald-ecat + 人機）

在開發機（Linux，裝好 .NET 8 SDK；正式版另需 IgH）產生安裝包：

```
deploy/build-package.sh                      # 只有模擬（展示 / 教育訓練用）
IGH=/opt/etherlab deploy/build-package.sh    # 正式（連結 IgH libethercat）
```

Windows 試操作版（沒有 Linux 的電腦看畫面用，不接硬體）：`deploy/build-windows-demo.sh`（需 mingw-w64），
產出 `dist/ALD-Demo-Windows.zip`，解壓後雙擊 `start-demo.bat`；虛擬機台是 `ipc/ecat-master/src/main_win.c` 編的 Windows 原生程式。

產出 `dist/ald-ipc-<版本>.tar.gz`（約 40 MB；人機是 self-contained，**IPC 不用另裝 .NET**）。
複製到 IPC 後：

```
tar xzf ald-ipc-<版本>.tar.gz && cd ald-ipc-<版本>
sudo ./install.sh          # 正式
sudo ./install.sh --sim    # 沒有主控板時：ald-ecat 跑虛擬機台
```

| 位置 | 內容 | 更新時 |
| --- | --- | --- |
| `/opt/ald/bin`、`/opt/ald/hmi` | ald-ecat、人機、啟動器 | 覆蓋 |
| `/etc/ald/machine.json` | 機台設定（資料路徑已改成 `/var/lib/ald`） | **保留**，新版預設值另存 `machine.json.new` |
| `/var/lib/ald` | `ald.db`（警報 / 趨勢 / 帳號）、`wafer.json`、`recipes/`、`log/hmi.log`、`export/` | 不動（配方只補新檔） |

開機自動啟動：
- `ald-ecat.service`（systemd，root、鎖記憶體、SCHED_FIFO、CPU 3），當掉 1 秒後重啟；記錄 `journalctl -u ald-ecat`。
- 人機：`/etc/xdg/autostart/ald-hmi.desktop` → 桌面登入後執行 `/opt/ald/bin/ald-hmi.sh`，被關掉 / 當掉 3 秒後重開。
  建議操作帳號設**自動登入**（Debian GNOME：設定 → 使用者 → 自動登入），開機直接進人機。
- 用 `sudo ./install.sh` 的那個帳號會取得 `/var/lib/ald` 的寫入權（人機用桌面登入帳號執行）。

測試安裝腳本（不碰 systemd）：`ROOT=/tmp/x ./install.sh`。

## 四、人機設定

- `config/machine.json`：`mainBoard.host = 127.0.0.1`。Loadlock 的 RS-485 接在主控板，IPC 不需要序列埠設定。
- 出廠 admin 密碼：`hmi.defaultAdminPasswordHash`（`/opt/ald/hmi/AldChamber --hash-password 新密碼` 產生）。

## 五、出貨注意

- IgH 是 GPL：出貨時附 IgH 原始碼（或書面提供索取方式）；ald-ecat 與人機動態連結 libethercat（LGPL），可不開源。
- EtherCAT Vendor ID 向 ETG 申請後，改 `tools/codegen/gen_registers.py` 的 `ECAT_VENDOR_ID` 重新產生。
