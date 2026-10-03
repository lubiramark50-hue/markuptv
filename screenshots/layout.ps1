param(
    [Parameter(Mandatory = $true)][string[]]$Path,
    [int]$MinBandPx = 70
)
# Layout balance analyzer for MarkUpTV captures (PS 5.1 safe).
#
# For every row of the image we measure the spread of luminance across the
# width. A row that is pure background varies almost not at all; a row that
# contains text, cards or artwork varies a lot. Contiguous runs of "flat" rows
# that are taller than $MinBandPx are reported as dead space, which is the
# measurable form of "this page is not balanced".
Add-Type -AssemblyName System.Drawing

foreach ($file in $Path) {
    if (-not (Test-Path $file)) { Write-Host "== $file -> missing"; continue }

    $bmp = $null
    try {
        $bmp = [System.Drawing.Bitmap]::FromFile($file)
        $w = $bmp.Width
        $h = $bmp.Height

        $stepY = 6
        $stepX = 6
        $flat = New-Object System.Collections.Generic.List[bool]

        for ($y = 0; $y -lt $h; $y += $stepY) {
            $min = 255.0
            $max = 0.0
            for ($x = 4; $x -lt ($w - 4); $x += $stepX) {
                $c = $bmp.GetPixel($x, $y)
                $lum = 0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B
                if ($lum -lt $min) { $min = $lum }
                if ($lum -gt $max) { $max = $lum }
            }
            $flat.Add((($max - $min) -lt 18))
        }

        # Merge contiguous flat rows into bands.
        $bands = @()
        $runStart = -1
        for ($i = 0; $i -lt $flat.Count; $i++) {
            if ($flat[$i]) {
                if ($runStart -lt 0) { $runStart = $i }
            }
            elseif ($runStart -ge 0) {
                $startY = $runStart * $stepY
                $lenPx = ($i - $runStart) * $stepY
                if ($lenPx -ge $MinBandPx) { $bands += ,@($startY, $lenPx) }
                $runStart = -1
            }
        }
        if ($runStart -ge 0) {
            $startY = $runStart * $stepY
            $lenPx = ($flat.Count - $runStart) * $stepY
            if ($lenPx -ge $MinBandPx) { $bands += ,@($startY, $lenPx) }
        }

        $flatCount = 0
        foreach ($f in $flat) { if ($f) { $flatCount++ } }
        $emptyPct = [math]::Round(100.0 * $flatCount / $flat.Count, 1)

        Write-Host ("== " + [System.IO.Path]::GetFileNameWithoutExtension($file) + "  ${w}x${h}  dead=${emptyPct}%")
        if ($bands.Count -eq 0) {
            Write-Host "   no dead bands >= ${MinBandPx}px"
        }
        else {
            foreach ($b in $bands) {
                Write-Host ("   dead band y={0,4}..{1,-4} ({2}px)" -f $b[0], ($b[0] + $b[1]), $b[1])
            }
        }
    }
    catch {
        Write-Host ("== " + [System.IO.Path]::GetFileName($file) + " -> unreadable: " + $_.Exception.Message)
    }
    finally {
        if ($bmp) { $bmp.Dispose() }
    }
}
