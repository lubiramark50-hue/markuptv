param(
    [Parameter(Mandatory = $true)][string]$Xml,
    [string]$Only = ''
)

# Find text nodes whose bounds partially overlap another text node's bounds.
#
#   overlap.ps1 -Xml hub_5.xml
#
# A label nested inside its container reports the same (or contained) bounds,
# which is normal. Two *different* pieces of text whose boxes overlap without
# one containing the other is a layout collision: that is what paints words on
# top of a neighbouring control.

$doc = Get-Content -Raw $Xml
$rx = '<node[^>]*text="([^"]+)"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"'

$nodes = @()
foreach ($m in [regex]::Matches($doc, $rx)) {
    $t = $m.Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($t) -or $t.Length -lt 2) { continue }
    $t = $t -replace '&#\d+;', '*' -replace '&amp;', '&'
    if ($Only -and ($t -notlike "*$Only*")) { continue }
    $nodes += [pscustomobject]@{
        Text = $t
        X1   = [int]$m.Groups[2].Value
        Y1   = [int]$m.Groups[3].Value
        X2   = [int]$m.Groups[4].Value
        Y2   = [int]$m.Groups[5].Value
    }
}

Write-Host ("text nodes: {0}" -f $nodes.Count)

$hits = 0
for ($i = 0; $i -lt $nodes.Count; $i++) {
    for ($j = $i + 1; $j -lt $nodes.Count; $j++) {
        $a = $nodes[$i]; $b = $nodes[$j]

        $w = [Math]::Min($a.X2, $b.X2) - [Math]::Max($a.X1, $b.X1)
        $h = [Math]::Min($a.Y2, $b.Y2) - [Math]::Max($a.Y1, $b.Y1)
        if ($w -le 4 -or $h -le 4) { continue }

        # Contained is fine: a label inside its own row/card.
        $aInB = ($a.X1 -ge $b.X1) -and ($a.X2 -le $b.X2) -and ($a.Y1 -ge $b.Y1) -and ($a.Y2 -le $b.Y2)
        $bInA = ($b.X1 -ge $a.X1) -and ($b.X2 -le $a.X2) -and ($b.Y1 -ge $a.Y1) -and ($b.Y2 -le $a.Y2)
        if ($aInB -or $bInA) { continue }

        $hits++
        Write-Host ("OVERLAP  '{0}' [{1},{2}][{3},{4}]  x  '{5}' [{6},{7}][{8},{9}]  ({10}x{11} px shared)" -f `
                $a.Text.Substring(0, [Math]::Min(38, $a.Text.Length)), $a.X1, $a.Y1, $a.X2, $a.Y2,
            $b.Text.Substring(0, [Math]::Min(38, $b.Text.Length)), $b.X1, $b.Y1, $b.X2, $b.Y2, $w, $h)
    }
}

if ($hits -eq 0) { Write-Host 'no partial text overlaps found' }
