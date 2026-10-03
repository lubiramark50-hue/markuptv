param(
    [Parameter(Mandatory = $true)][string]$Png,
    [Parameter(Mandatory = $true)][string]$Xml,
    [Parameter(Mandatory = $true)][string[]]$Text
)

# Measure the real rendered contrast of text on a screenshot.
#
#   contrast.ps1 -Png before_flyout.png -Xml before_flyout.xml -Text Cartoons,Gospel
#
# Bounds come from a uiautomator dump (device pixels, which is exactly what
# `screencap` writes), so the sampled box is the label's own box and nothing
# else. The dominant colour in that box is the background; the pixel furthest
# from it in luminance is the text a reader actually sees against it. The
# reported ratio is the WCAG contrast ratio between those two.
#
# Pixels are read straight out of locked bitmap memory: GetPixel per pixel is
# roughly a thousand times slower and this box is a quarter of a million pixels.

Add-Type -AssemblyName System.Drawing

function Convert-Lum([int]$r, [int]$g, [int]$b) {
    $rs = $r / 255.0; $gs = $g / 255.0; $bs = $b / 255.0
    $rl = if ($rs -le 0.03928) { $rs / 12.92 } else { [Math]::Pow((($rs + 0.055) / 1.055), 2.4) }
    $gl = if ($gs -le 0.03928) { $gs / 12.92 } else { [Math]::Pow((($gs + 0.055) / 1.055), 2.4) }
    $bl = if ($bs -le 0.03928) { $bs / 12.92 } else { [Math]::Pow((($bs + 0.055) / 1.055), 2.4) }
    return (0.2126 * $rl) + (0.7152 * $gl) + (0.0722 * $bl)
}

function Get-Ratio([double]$a, [double]$b) {
    $hi = [Math]::Max($a, $b); $lo = [Math]::Min($a, $b)
    return [Math]::Round(($hi + 0.05) / ($lo + 0.05), 2)
}

$doc = Get-Content -Raw $Xml
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Png).Path)

# Normalise to 32bpp so every pixel is 4 bytes and the stride is predictable.
$bmp = New-Object System.Drawing.Bitmap $src.Width, $src.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g0 = [System.Drawing.Graphics]::FromImage($bmp)
$g0.DrawImage($src, 0, 0, $src.Width, $src.Height)
$g0.Dispose()
$src.Dispose()

try {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $bytes = New-Object byte[] ($stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)

    foreach ($t in $Text) {
        $rx = '<node[^>]*text="' + [regex]::Escape($t) + '"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"'
        $m = [regex]::Match($doc, $rx)
        if (-not $m.Success) { Write-Host ("{0,-18}: no node in dump" -f $t); continue }

        # Keep clear of the box edges, where the glyph strokes are cut off.
        $x1 = [int]$m.Groups[1].Value + 2; $y1 = [int]$m.Groups[2].Value + 4
        $x2 = [int]$m.Groups[3].Value - 2; $y2 = [int]$m.Groups[4].Value - 4

        $hist = @{}
        $maxL = -1.0; $maxR = 0; $maxG = 0; $maxB = 0
        $minL = 2.0; $minR = 0; $minG = 0; $minB = 0

        for ($y = $y1; $y -lt $y2; $y++) {
            $row = $y * $stride
            for ($x = $x1; $x -lt $x2; $x++) {
                $i = $row + ($x * 4)
                $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]
                # Integer luminance is enough to find the two extremes.
                $l = (2 * $r) + (5 * $g) + $b
                if ($l -gt $maxL) { $maxL = $l; $maxR = $r; $maxG = $g; $maxB = $b }
                if ($l -lt $minL) { $minL = $l; $minR = $r; $minG = $g; $minB = $b }
                $key = ($r -shr 3) -shl 10 -bor ($g -shr 3) -shl 5 -bor ($b -shr 3)
                if ($hist.ContainsKey($key)) { $hist[$key]++ } else { $hist[$key] = 1 }
            }
        }

        # Dominant quantised colour: step back up to the centre of its bucket so
        # the reported background is a real pixel value, not a bucket floor.
        $top = $hist.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 1
        $k = [int]$top.Key
        $bgB = ((($k) -band 31) -shl 3) + 4
        $bgG = ((($k -shr 5) -band 31) -shl 3) + 4
        $bgR = ((($k -shr 10) -band 31) -shl 3) + 4

        $bgL = Convert-Lum $bgR $bgG $bgB
        $maxLs = Convert-Lum $maxR $maxG $maxB
        $minLs = Convert-Lum $minR $minG $minB

        if ([Math]::Abs($maxLs - $bgL) -ge [Math]::Abs($minLs - $bgL)) {
            $fgR = $maxR; $fgG = $maxG; $fgB = $maxB; $fgL = $maxLs
        }
        else {
            $fgR = $minR; $fgG = $minG; $fgB = $minB; $fgL = $minLs
        }

        $ratio = Get-Ratio $bgL $fgL
        $verdict = if ($ratio -ge 4.5) { 'PASS' } elseif ($ratio -ge 3.0) { 'large-text only' } else { 'FAIL' }

        Write-Host ("{0,-18} box=[{1},{2}][{3},{4}]  bg=rgb({5},{6},{7})  text=rgb({8},{9},{10})  ratio={11}:1  {12}" -f `
                $t, $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value, $m.Groups[4].Value, `
                $bgR, $bgG, $bgB, $fgR, $fgG, $fgB, $ratio, $verdict)
    }
}
finally {
    if ($bmp) { $bmp.Dispose() }
}
