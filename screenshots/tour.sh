#!/bin/bash
# Android page-tour helper for MarkUptv.
# Usage:
#   tour.sh home            -> force-stop, relaunch, wait for dashboard
#   tour.sh route <name> [postwait]
#                           -> from dashboard: open flyout, tap the row whose
#                              label matches <name> (case-insensitive substring),
#                              screenshot the result page.
#   tour.sh back            -> press BACK once and screenshot
#   tour.sh tap <x> <y> <name> [postwait]
#                           -> tap coordinates, screenshot
#   tour.sh scroll [px]     -> scroll down by px (default 900), screenshot
#   tour.sh ui [name]       -> dump the accessibility tree (uiautomator)
ADB="/c/Users/hp/AppData/Local/Android/Sdk/platform-tools/adb.exe"
S=/c/Users/hp/source/repos/MarkUptv/screenshots
CMD="${1:-home}"
NAME="${2:-shot}"
WAIT="${3:-4}"
shot() { "$ADB" exec-out screencap -p > "$S/${1}.png" 2>/dev/null; echo "shot: ${1}.png"; }
dump() { "$ADB" shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1; "$ADB" exec-out cat /sdcard/ui.xml > "$S/ui_${1:-$NAME}.xml" 2>/dev/null; }
rowbounds() {
  # $1 = label substring; prints "x,y" of the matching row center.
  dump row
  tr '>' '\n' < "$S/ui_row.xml" | grep -ai "$1" | grep -aoE 'bounds="\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]"' | head -1 | \
  sed -E 's/bounds="\[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\]"/\1 \2 \3 \4/' | \
  awk '{ printf "%d,%d\n", ($1+$3)/2, ($2+$4)/2 }'
}
case "$CMD" in
  home)
    "$ADB" shell am force-stop com.markup.markuptv 2>/dev/null
    sleep 1
    "$ADB" shell am start -W -n com.markup.markuptv/crc6499e194cbb6dee856.MainActivity 2>/dev/null | grep -a TotalTime
    sleep 8
    shot home
    ;;
  route)
    # open flyout via the dashboard's menu button (verified bounds centre)
    "$ADB" shell input tap 636 887; sleep 2
    P=$(rowbounds "$NAME")
    if [ -z "$P" ]; then echo "row '$NAME' not found"; shot route_miss; exit 1; fi
    "$ADB" shell input tap "${P%%,*}" "${P##*,}"; sleep "$WAIT"
    shot "p_${NAME//[!a-z0-9]/_}"
    dump "p_${NAME//[!a-z0-9]/_}"
    ;;
  back)
    "$ADB" shell input keyevent 4; sleep 2; shot "b_$NAME"
    ;;
  tap)
    X="${2:-0}"; Y="${3:-0}"; NAME="${4:-tap}"; WAIT="${5:-4}"
    "$ADB" shell input tap "$X" "$Y"; sleep "$WAIT"; shot "t_$NAME"
    ;;
  scroll)
    PX="${2:-900}"
    "$ADB" shell input swipe 540 1800 540 $((1800 - PX)) 400; sleep 2; shot "s_$NAME"
    ;;
  ui)
    dump "$NAME"; echo "dump: ui_${NAME}.xml"
    ;;
  *)
    echo "unknown command: $CMD"; exit 1 ;;
esac
