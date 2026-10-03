#!/bin/bash
# Tap the first node whose text matches $1 (exact or substring). v2
ADB="C:\\Users\\hp\\AppData\\Local\\Android\\Sdk\\platform-tools\\adb.exe"
TXT="$1"
TMP=$(mktemp)
"$ADB" shell uiautomator dump //sdcard/ui.xml >/dev/null 2>&1
"$ADB" exec-out cat //sdcard/ui.xml > "$TMP" 2>/dev/null
# strip CR, then pull the first full node tag whose text attribute contains TXT
NODE=$(tr -d '\r' < "$TMP" | grep -oE "<node[^>]*text=\"[^\"]*$TXT[^\"]*\"[^>]*>" | head -1)
rm -f "$TMP"
if [ -z "$NODE" ]; then
  echo "NOT FOUND: $TXT"
  exit 1
fi
B=$(echo "$NODE" | grep -oE 'bounds="\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]"')
X=$(( $(echo "$B" | sed -E 's/bounds="\[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\]"/\1 \3/' | awk '{print int(($1+$2)/2)}') ))
Y=$(( $(echo "$B" | sed -E 's/bounds="\[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\]"/\2 \4/' | awk '{print int(($1+$2)/2)}') ))
"$ADB" shell input tap "$X" "$Y"
echo "tapped '$TXT' at ($X,$Y)"
