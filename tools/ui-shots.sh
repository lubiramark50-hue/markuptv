#!/usr/bin/env bash
# Boots on an emulator, opens every Shell route through the debug deep link and screenshots it.
set -u
PKG=com.markup.markuptv
OUT="${1:-shots}"
APK="${APK_PATH:?APK_PATH not set}"
mkdir -p "$OUT"

adb wait-for-device
adb shell settings put global window_animation_scale 0 || true
adb shell settings put global transition_animation_scale 0 || true
adb shell settings put global animator_duration_scale 0 || true
adb install -r -g "$APK" || { echo "INSTALL FAILED" > "$OUT/_install_failed.txt"; exit 0; }

shot() { adb exec-out screencap -p > "$OUT/$1.png"; adb shell uiautomator dump /sdcard/u.xml >/dev/null 2>&1; adb pull /sdcard/u.xml "$OUT/ui_$1.xml" >/dev/null 2>&1; }
alive() { [ -n "$(adb shell pidof $PKG | tr -d '\r')" ]; }

adb logcat -c
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
sleep 20; shot 00_launch_20s
sleep 40; shot 01_launch_60s

ROUTES="MainPage SportsPage NewsPage MoviesPage MusicPage FootballPage WorldChannelsPage CommunityHubPage SocialPage RecentlyWatchedPage DonationPage CartoonPage DiscoveryPage FashionPage GospelPage Lifestyle ReligiousTvPage LocalPage EuropeanSportsPage WildlifePage AdultsPage MovieCatalogPage SearchPage MatchThreadPage"
i=2
for r in $ROUTES; do
  if ! alive; then echo "CRASHED before $r" >> "$OUT/_events.txt"; adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1; sleep 25; fi
  adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/$r" "$PKG" >/dev/null 2>&1
  sleep 9
  n=$(printf "%02d" $i); shot "${n}_$r"; i=$((i+1))
  alive || echo "CRASHED on $r" >> "$OUT/_events.txt"
done

# Real bottom-tab taps (not deep links) plus the flyout drawer
adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/MainPage" "$PKG" >/dev/null 2>&1
sleep 9
for pair in "tab_Sports:324" "tab_News:540" "tab_Movies:756" "tab_Music:972" "tab_Home:108"; do
  name=${pair%%:*}; x=${pair##*:}
  adb shell input tap "$x" 2236
  sleep 6; shot "$name"
done
adb shell input swipe 4 1200 760 1200 300
sleep 3; shot "flyout_open"
adb shell input keyevent 4
sleep 2
adb shell input keyevent 4
sleep 4
shot "after_back_at_root"
if alive; then echo "BACK_AT_ROOT: process survived" >> "$OUT/_events.txt"; else echo "BACK_AT_ROOT: process died" >> "$OUT/_events.txt"; fi

# Television-sized viewport pass (landscape 1080p)
adb shell wm size 1920x1080
adb shell wm density 240
sleep 4
for r in MainPage SportsPage NewsPage MoviesPage CommunityHubPage; do
  adb shell am start -a android.intent.action.VIEW -d "markuptv://goto/$r" "$PKG" >/dev/null 2>&1
  sleep 9; shot "tv_$r"
done
adb shell wm size reset
adb shell wm density reset

adb logcat -d -v time > "$OUT/_logcat_full.txt" 2>&1
grep -E "AndroidRuntime|FATAL|ANR|MarkUpTV|mono-rt|Unhandled" "$OUT/_logcat_full.txt" | tail -300 > "$OUT/_logcat_filtered.txt"
python3 tools/ui_audit.py "$OUT" || true
echo done > "$OUT/_done.txt"
