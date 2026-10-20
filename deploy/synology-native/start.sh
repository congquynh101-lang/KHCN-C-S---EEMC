#!/bin/sh
# Khởi động EEMC Work Manager Web trên NAS Synology không có Docker.
# Task Scheduler → Triggered Task → Boot-up (chạy cùng NAS); chạy lại tác vụ này để khởi động lại phần mềm.
# Mở trên máy tính trong mạng: http://<IP-NAS>:8088
B=/volume1/eemc
APP=$B/app
export EEMC_WM_DATA=$B/data
export ASPNETCORE_URLS=http://0.0.0.0:8088
export TZ=Asia/Ho_Chi_Minh
# NAS ít RAM: dùng bộ thu gom rác chế độ máy trạm và giới hạn bộ nhớ
export DOTNET_gcServer=0
export DOTNET_GCHeapHardLimit=0xA000000
export DOTNET_EnableWriteXorExecute=0
# Nếu DSM thiếu thư viện ICU: chạy ở chế độ không phụ thuộc ICU (DS215j/DSM 7.1 có sẵn ICU 64)
if ! ls /usr/lib/libicuuc.so* /lib/libicuuc.so* >/dev/null 2>&1; then
  export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
  export DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=0
fi
mkdir -p "$EEMC_WM_DATA"
# Thư viện SQLite đi kèm cần glibc 2.28; DSM cũ (glibc 2.26) dùng SQLite có sẵn của hệ thống (cùng giao diện sqlite3_*)
SYS_SQLITE=$(ls /usr/lib/libsqlite3.so.0 /lib/libsqlite3.so.0 2>/dev/null | head -1)
if [ -n "$SYS_SQLITE" ] && [ ! -L "$APP/libe_sqlite3.so" ]; then
  mv "$APP/libe_sqlite3.so" "$APP/libe_sqlite3.so.dikem" 2>/dev/null
  ln -s "$SYS_SQLITE" "$APP/libe_sqlite3.so"
fi
echo "$(date) SQLite he thong: $SYS_SQLITE $(grep -ao '3\.[0-9][0-9]\.[0-9]*' "$SYS_SQLITE" 2>/dev/null | head -1)" >> "$EEMC_WM_DATA/start.log"
pkill -f "$APP/EEMC.WM.Web" 2>/dev/null && sleep 3
cd "$APP" || exit 1
nohup "$APP/EEMC.WM.Web" > "$EEMC_WM_DATA/console.log" 2>&1 &
echo "$(date) khoi dong PID $!" >> "$EEMC_WM_DATA/start.log"
