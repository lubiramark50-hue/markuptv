#!/usr/bin/env bash
# Android TV emulator check: leanback launch, safe-area margins, D-pad focus and OK-to-select.
set -u
PKG=com.markup.markuptv
APK="${APK_PATH:?APK_PATH not set}"
adb wait-for-device
adb shell settings put global window_animation_scale 0 || true
adb shell settings put global transition_animation_scale 0 || true
adb shell settings put global animator_duration_scale 0 || true
echo "TV: characteristics=$(adb shell getprop ro.build.characteristics | tr -d '\r') sdk=$(adb shell getprop ro.build.version.sdk | tr -d '\r') abi=$(adb shell getprop ro.product.cpu.abi | tr -d '\r')"
DENS=$(adb shell wm density | grep -oE '[0-9]+' | tail -1)
DP=$(python3 -c "print($DENS/160)")
echo "TV: density=$DENS size=$(adb shell wm size | tr -d '\r' | tr '\n' ' ')"
adb install -r -g "$APK" || { echo "TV: INSTALL FAILED"; exit 0; }
adb logcat -c
echo "TV: leanback launch result: $(adb shell monkey -p $PKG -c android.intent.category.LEANBACK_LAUNCHER 1 2>&1 | tr -d '\r' | tail -2 | tr '\n' ' ')"
sleep 50
fdump() { adb shell uiautomator dump /sdcard/f.xml >/dev/null 2>&1; adb shell cat /sdcard/f.xml | python3 tools/tv_focus.py "$DP" "$1"; }
fdump launch
adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/MainPage" $PKG >/dev/null 2>&1
sleep 12
fdump main_initial
for k in 1 2 3 4; do
  adb shell input keyevent KEYCODE_DPAD_DOWN; sleep 2; fdump "down_$k"
done
adb shell input keyevent KEYCODE_DPAD_RIGHT; sleep 2; fdump right_1
echo "TV: before select: $(adb shell dumpsys activity activities | grep -E 'topResumedActivity' | head -1 | tr -d '\r')"
adb shell input keyevent KEYCODE_DPAD_CENTER
sleep 8
fdump after_select
echo "TV: text visible after select:"
adb shell uiautomator dump /sdcard/f.xml >/dev/null 2>&1
adb shell cat /sdcard/f.xml | grep -oE 'text="[^"]+"' | head -12
echo "TV: error and warning lines:"
adb logcat -d -v time | grep -E "FATAL EXCEPTION|TV select failed|Unhandled|MarkUpTV.*(W|E)/" | head -12
echo "TV: done"
