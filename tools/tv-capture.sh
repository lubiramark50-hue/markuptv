#!/usr/bin/env bash
# Genuine Android TV emulator screenshots and D-pad smoke test.
set -euo pipefail
PKG=com.markup.markuptv
APK="${APK_PATH:?APK_PATH not set}"
OUT="${1:-shots}"
mkdir -p "$OUT"
adb wait-for-device
adb install -r -g "$APK"
echo "TV DEVICE: $(adb shell getprop ro.build.characteristics | tr -d '\r')"
adb shell settings put global window_animation_scale 0 || true
adb shell settings put global transition_animation_scale 0 || true
adb shell settings put global animator_duration_scale 0 || true
adb logcat -c
adb shell monkey -p "$PKG" -c android.intent.category.LEANBACK_LAUNCHER 1 >/dev/null || true
sleep 24
shot() {
  local name="$1"
  adb exec-out screencap -p > "$OUT/$name.png"
  adb shell uiautomator dump /sdcard/tv-ui.xml >/dev/null 2>&1 || true
  adb pull /sdcard/tv-ui.xml "$OUT/ui_$name.xml" >/dev/null 2>&1 || true
  echo "CAPTURED Android TV $name"
}
for route in MainPage SportsPage NewsPage MoviesPage MusicPage FootballPage WorldChannelsPage CommunityHubPage MorePage; do
  adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/$route" "$PKG" >/dev/null
  sleep 7
  shot "tv_$route"
done
adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/MainPage" "$PKG" >/dev/null
sleep 4
for key in KEYCODE_DPAD_DOWN KEYCODE_DPAD_RIGHT KEYCODE_DPAD_CENTER; do
  adb shell input keyevent "$key"
  sleep 2
done
shot tv_dpad_selection
adb logcat -d -v time > "$OUT/_logcat_tv.txt" 2>&1
python3 tools/ui_audit.py "$OUT" > "$OUT/_audit_report.txt" 2>&1 || true
echo "TV screenshots: $(find "$OUT" -name 'tv_*.png' | wc -l)"
cat "$OUT/_audit_report.txt" | tail -25
FATAL=$(grep -c 'FATAL EXCEPTION' "$OUT/_logcat_tv.txt" || true)
test "$FATAL" -eq 0 || { echo "Android TV app crash count=$FATAL" >&2; exit 1; }
