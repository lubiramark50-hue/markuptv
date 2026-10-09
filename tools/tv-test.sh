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
mkdir -p tvshots
shot() { adb exec-out screencap -p > "tvshots/$1.png"; }
fdump() { shot "$1"; adb shell uiautomator dump /sdcard/f.xml >/dev/null 2>&1; adb shell cat /sdcard/f.xml | python3 tools/tv_focus.py "$DP" "$1"; }
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
for r in SportsPage NewsPage MoviesPage CommunityHubPage SocialPage FootballPage WorldChannelsPage DonationPage SearchPage MatchThreadPage; do
  adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/$r" $PKG >/dev/null 2>&1
  sleep 8
  shot "page_$r"
  adb shell input keyevent KEYCODE_DPAD_DOWN; sleep 2
  shot "page_${r}_focus"
done
echo "TV: BACK test at root"
adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/MainPage" $PKG >/dev/null 2>&1
sleep 8
adb logcat -c
adb shell input keyevent KEYCODE_BACK
sleep 5
echo "TV: after back 1: pid=$(adb shell pidof $PKG | tr -d '\r') top=$(adb shell dumpsys activity activities | grep topResumedActivity | head -1 | tr -d '\r' | cut -c1-110)"
adb shell input keyevent KEYCODE_BACK
sleep 6
echo "TV: after back 2: pid=$(adb shell pidof $PKG | tr -d '\r') top=$(adb shell dumpsys activity activities | grep topResumedActivity | head -1 | tr -d '\r' | cut -c1-110)"
echo "TV: FATAL count after back presses: $(adb logcat -d -v time | grep -c 'FATAL EXCEPTION')"
adb logcat -d -v time | grep -E "FATAL EXCEPTION|did not call through" | head -3
echo "TV: done"
