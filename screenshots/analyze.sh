#!/bin/bash
# Analyze a uiautomator XML dump for layout balance issues.
# Usage: analyze.sh <name>   (reads ui_<name>.xml in screenshots dir)
SHOT="/c/Users/hp/source/repos/MarkUptv/screenshots"
F="$SHOT/ui_${1}.xml"
[ -f "$F" ] || { echo "missing $F"; exit 1; }

TMP=$(mktemp)
tr '>' '>\n' < "$F" > "$TMP"

echo "--- clickable nodes with height < 40px (cramped tap targets):"
grep 'clickable="true"' "$TMP" |
grep -oE 'bounds="\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]"' |
sed -E 's/bounds="\[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\]"/\1 \2 \3 \4/' |
awk '{ h=$4-$2; w=$3-$1; if (h < 40 && h > 0) printf "  small tap target %dx%d at (%d,%d)\n", w, h, $1, $2 }' |
head -12

echo "--- text nodes clipped/overflowing screen edge:"
grep 'text="[^"]' "$TMP" |
grep -oE 'text="[^"]*"[^>]*bounds="\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]"' |
sed -E 's/text="([^"]*)".*bounds="\[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\]"/\1|\2|\3|\4|\5/' |
awk -F'|' '{ if ($4+0 >= 1075 || $5+0 >= 2395) printf "  overflow: %s right=%s bottom=%s\n", substr($1,1,40), $4, $5 }' |
head -12

echo "--- node counts:"
TOTAL=$(grep -c "<node" "$TMP"); CLICKABLE=$(grep -c 'clickable="true"' "$TMP")
echo "  nodes=$TOTAL clickable=$CLICKABLE"
rm -f "$TMP"
