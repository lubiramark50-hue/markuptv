#!/usr/bin/env bash
# Smoke test of the real Release build: install, launch, stay alive, report what is on screen.
set -u
PKG=com.markup.markuptv
APK="${APK_PATH:?APK_PATH not set}"
adb wait-for-device
adb shell settings put global window_animation_scale 0 || true
adb shell settings put global transition_animation_scale 0 || true
adb shell settings put global animator_duration_scale 0 || true
adb install -r -g "$APK" || { echo "SMOKE: INSTALL FAILED" >&2; exit 1; }
adb logcat -c
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1 || { echo "SMOKE: LAUNCH FAILED" >&2; exit 1; }
sleep 30
SMOKE_DIED=0
for i in 1 2 3 4 5; do
  if [ -n "$(adb shell pidof "$PKG" | tr -d '\r')" ]; then
    echo "SMOKE: alive at $((30+i*15))s"
  else
    echo "SMOKE: DIED by $((30+i*15))s" >&2
    SMOKE_DIED=1
  fi
  sleep 15
done
echo "SMOKE: resumed activity:"
adb shell dumpsys activity activities | grep -E "topResumedActivity|mResumedActivity" | head -2
echo "SMOKE: visible text on screen:"
adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1
adb shell cat /sdcard/ui.xml | grep -oE 'text="[^"]+"' | head -30
echo "SMOKE: crash and error lines:"
adb logcat -d -v time | grep -E "FATAL EXCEPTION|AndroidRuntime|Unable to resolve|MissingMethod|TypeLoad|NotSupported|JavaProxyThrowable|Unhandled" | head -30
FATAL_COUNT=$(adb logcat -d -v time | grep -c "FATAL EXCEPTION" || true)
if [ "$SMOKE_DIED" -ne 0 ] || [ "$FATAL_COUNT" -gt 0 ]; then
  echo "SMOKE: FAILED (died=$SMOKE_DIED, fatal=$FATAL_COUNT)" >&2
  exit 1
fi
echo "SMOKE: passed"
