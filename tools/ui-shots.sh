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

shot() { adb exec-out screencap -p > "$OUT/$1.png"; }
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
echo done > "$OUT/_done.txt"
