#!/bin/bash
# Robust UI dump with retries. Usage: dumpui.sh <name> [tries]
ADB="C:\\Users\\hp\\AppData\\Local\\Android\\Sdk\\platform-tools\\adb.exe"
SHOT="/c/Users/hp/source/repos/MarkUptv/screenshots"
NAME="${1:-dump}"
TRIES="${2:-6}"
"$ADB" shell rm -f //sdcard/ui.xml >/dev/null 2>&1
for i in $(seq 1 "$TRIES"); do
  "$ADB" shell uiautomator dump //sdcard/ui.xml >/dev/null 2>&1
  SIZE=$("$ADB" shell stat -c %s //sdcard/ui.xml 2>/dev/null | tr -d '\r')
  if [ -n "$SIZE" ] && [ "$SIZE" -gt 5000 ] 2>/dev/null; then
    "$ADB" exec-out cat //sdcard/ui.xml > "$SHOT/ui_$NAME.xml" 2>/dev/null
    echo "dump $NAME ok (try $i, $SIZE bytes)"
    exit 0
  fi
  sleep 1
done
echo "dump $NAME FAILED after $TRIES tries"
exit 1
