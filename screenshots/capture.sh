#!/bin/bash
# Capture screenshot + UI dump of the current emulator screen.
# Usage: capture.sh <name> [wait_seconds]
ADB="C:\\Users\\hp\\AppData\\Local\\Android\\Sdk\\platform-tools\\adb.exe"
SHOT="/c/Users/hp/source/repos/MarkUptv/screenshots"
NAME="${1:-shot}"
WAIT="${2:-3}"
sleep "$WAIT"
"$ADB" exec-out screencap -p > "$SHOT/$NAME.png" 2>/dev/null
"$ADB" shell uiautomator dump //sdcard/ui.xml >/dev/null 2>&1
"$ADB" exec-out cat //sdcard/ui.xml > "$SHOT/ui_$NAME.xml" 2>/dev/null
echo "captured $NAME ($(wc -c < "$SHOT/$NAME.png") bytes png, $(wc -c < "$SHOT/ui_$NAME.xml") bytes ui)"
