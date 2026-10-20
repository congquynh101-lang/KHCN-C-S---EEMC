#!/bin/sh
# EEMC Work Manager Web trên NAS Synology không có Docker (ví dụ DS215j, ARM 32-bit).
# Chạy MỘT LẦN (Task Scheduler → User-defined script, tài khoản quản trị DSM): ghi thông tin hệ thống,
# ghép các phần app.partNN trong /volume1/eemc/upload, giải nén vào /volume1/eemc/app.
# Kết quả ghi vào /volume1/eemc/install.txt. Dữ liệu (/volume1/eemc/data) không bị động tới.
B=/volume1/eemc
O=$B/install.txt
{
  echo "== $(date)"
  uname -a
  echo "-- glibc";    ldd --version 2>&1 | head -1; ls /lib/libc-* 2>/dev/null
  echo "-- icu";      ls /usr/lib/libicuuc.so* /lib/libicuuc.so* 2>/dev/null
  echo "-- openssl";  ls /usr/lib/libssl.so* /lib/libssl.so* 2>/dev/null
  echo "-- glibcxx";  for f in /usr/lib/libstdc++.so.6 /lib/libstdc++.so.6; do [ -f "$f" ] && grep -ao 'GLIBCXX_3\.4\.[0-9]*' "$f" | sort -t. -k3 -n -u | tail -1; done
  echo "-- zoneinfo"; ls /usr/share/zoneinfo/Asia/Ho_Chi_Minh 2>&1
  echo "-- cpu";      grep -m3 -E 'model name|Features|CPU architecture' /proc/cpuinfo
  echo "-- mem";      grep -E 'MemTotal|MemFree|MemAvailable' /proc/meminfo
  echo "-- ghep goi"
  cat $B/upload/app.part* > $B/upload/app.tar.gz && echo "sha256: $(sha256sum $B/upload/app.tar.gz | cut -d' ' -f1)"
  if [ -f $B/upload/app.sha256 ] && [ "$(cut -d' ' -f1 $B/upload/app.sha256)" != "$(sha256sum $B/upload/app.tar.gz | cut -d' ' -f1)" ]; then
    echo "cai dat: LOI - goi khong khop app.sha256 (thieu hoac thua tep app.part*)"; echo "== xong $(date)"; exit 1
  fi
  rm -rf $B/app.new && mkdir -p $B/app.new \
    && tar -xzf $B/upload/app.tar.gz -C $B/app.new && echo "giai nen: OK, $(ls $B/app.new | wc -l) muc"
  if [ -f $B/app.new/EEMC.WM.Web ]; then
    [ -d $B/app ] && { rm -rf $B/app.old; mv $B/app $B/app.old; }
    mv $B/app.new $B/app && chmod +x $B/app/EEMC.WM.Web $B/start.sh && echo "cai dat: OK"
  else
    echo "cai dat: LOI - khong thay EEMC.WM.Web"
  fi
  mkdir -p $B/data
  echo "== xong $(date)"
} > $O 2>&1
