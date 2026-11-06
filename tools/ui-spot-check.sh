#!/usr/bin/env bash
# Fast, deterministic UI spot check for recently polished screens.
set -euo pipefail
PKG=com.markup.markuptv
OUT="${1:-shots}"
APK="${APK_PATH:?APK_PATH not set}"
mkdir -p "$OUT"
adb wait-for-device
adb install -r -g "$APK"
adb shell settings put global window_animation_scale 0 || true
adb shell settings put global transition_animation_scale 0 || true
adb shell settings put global animator_duration_scale 0 || true
adb logcat -c
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null
sleep 22
snap() {
  local name="$1"
  adb exec-out screencap -p > "$OUT/$name.png"
  adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1 || true
  adb pull /sdcard/ui.xml "$OUT/ui_$name.xml" >/dev/null 2>&1 || true
  echo "CAPTURED $name"
}
for route in MainPage CommunityHubPage MorePage PaymentRequiredPage SearchPage; do
  adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/$route" "$PKG" >/dev/null
  sleep 8
  snap "$route"
  if [ "$route" = "CommunityHubPage" ]; then
    adb shell input swipe 520 1880 520 650 350
    sleep 2; snap "CommunityHubPage_scroll"
  fi
  if [ "$route" = "SearchPage" ]; then
    adb shell input keyevent KEYCODE_BACK
    sleep 2; snap "SearchPage_no_keyboard"
  fi
done
adb logcat -d -v time > "$OUT/_logcat.txt" 2>&1
python3 tools/ui_audit.py "$OUT" > "$OUT/_audit_report.txt" 2>&1 || true
cat "$OUT/_audit_report.txt" | tail -15
FATAL=$(grep -c 'FATAL EXCEPTION' "$OUT/_logcat.txt" || true)
test "$FATAL" -eq 0 || { echo "UI SPOT CHECK FAILED: fatal crashes=$FATAL" >&2; exit 1; }
test "$(find "$OUT" -name '*.png' | wc -l)" -ge 5 || { echo "UI SPOT CHECK FAILED: missing screenshots" >&2; exit 1; }
echo "UI SPOT CHECK PASSED"
