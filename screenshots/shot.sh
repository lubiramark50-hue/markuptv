#!/bin/bash
# Capture screenshot + UI dump. Usage: ./shot.sh <name> [pre-sleep]
ADB="/c/Users/hp/AppData/Local/Android/Sdk/platform-tools/adb.exe"
S="/c/Users/hp/source/repos/MarkUptv/screenshots"
NAME="${1:-shot}"
WAIT="${2:-2}"
sleep "$WAIT"
"$ADB" exec-out screencap -p > "$S/$NAME.png" 2>/dev/null
"$ADB" shell uiautomator dump //sdcard/ui.xml >/dev/null 2>&1
"$ADB" exec-out cat //sdcard/ui.xml > "$S/ui_$NAME.xml" 2>/dev/null
# bounds helper: grep text nodes with centre coords
tr '>' '>\n' < "$S/ui_$NAME.xml" | grep -oE 'text="[^"]+"[^>]*bounds="\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]"' | sed -E 's/text="([^"]*)".*bounds="\[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\]"/\1 [\2,\3 \4,\5]/' | grep -v '^ ' | head -40
echo "--- shot: $NAME.png"
