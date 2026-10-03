param(
    [string]$TourDir = "C:\Users\hp\source\repos\MarkUptv\screenshots\tour",
    [string]$OutFile = "C:\Users\hp\source\repos\MarkUptv\screenshots\gallery.html",
    [int]$MinBandPx = 70
)
# Build a contact sheet of every captured page: the frame itself, the measured
# dead space, and the OCR text, so the whole UI can be reviewed in one place
# instead of one screenshot at a time.
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Web -ErrorAction SilentlyContinue

function Get-DeadSpaceStats {
    param([string]$File, [int]$MinBandPx)

    $bmp = $null
    try {
        $bmp = [System.Drawing.Bitmap]::FromFile($File)
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

        $bands = @()
        $runStart = -1
        for ($i = 0; $i -lt $flat.Count; $i++) {
            if ($flat[$i]) {
                if ($runStart -lt 0) { $runStart = $i }
            }
            elseif ($runStart -ge 0) {
                $startY = $runStart * $stepY
                $lenPx = ($i - $runStart) * $stepY
                if ($lenPx -ge $MinBandPx) { $bands += , @($startY, $lenPx) }
                $runStart = -1
            }
        }

        $flatCount = 0
        foreach ($f in $flat) { if ($f) { $flatCount++ } }

        return [pscustomobject]@{
            Width  = $w
            Height = $h
            Dead   = [math]::Round(100.0 * $flatCount / $flat.Count, 1)
            Bands  = $bands
        }
    }
    finally {
        if ($bmp) { $bmp.Dispose() }
    }
}

$rows = @()
Get-ChildItem -Path $TourDir -Filter *.png |
    Where-Object { $_.Name -notlike '_*' } |
    Sort-Object Name |
    ForEach-Object {
        $stats = Get-DeadSpaceStats -File $_.FullName -MinBandPx $MinBandPx

        $txt = Join-Path $TourDir ($_.BaseName + ".txt")
        $lines = @()
        if (Test-Path $txt) {
            $lines = Get-Content $txt |
                Where-Object { $_ -match '^[0-9]+,[0-9]+ : ' } |
                ForEach-Object { ($_ -split ': ', 2)[1].Trim() } |
                Where-Object { $_ -ne '' } |
                Select-Object -First 14
        }

        $bandText = if ($stats.Bands.Count -eq 0) { "none" }
                    else { ($stats.Bands | ForEach-Object { "y=$($_[0]) ($($_[1])px)" }) -join ", " }

        $rows += [pscustomobject]@{
            Name   = $_.BaseName
            Png    = $_.Name
            Bytes  = $_.Length
            W      = $stats.Width
            H      = $stats.Height
            Dead   = $stats.Dead
            Bands  = $bandText
            Lines  = $lines
        }
    }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<!DOCTYPE html>')
[void]$sb.AppendLine('<html lang="en"><head><meta charset="utf-8">')
[void]$sb.AppendLine('<meta name="viewport" content="width=device-width, initial-scale=1">')
[void]$sb.AppendLine('<title>MarkUpTV page gallery</title>')
[void]$sb.AppendLine(@'
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body { margin: 0; padding: 26px; background: #070912; color: #EEF3FB;
         font: 14px/1.5 "Segoe UI", system-ui, sans-serif; }
  h1 { margin: 0 0 4px; font-size: 24px; letter-spacing: .4px; }
  .sub { color: #93A0BF; margin-bottom: 22px; }
  .grid { display: grid; gap: 18px; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); }
  .card { background: #0F1322; border: 1px solid #232A42; border-radius: 16px; overflow: hidden;
          display: flex; flex-direction: column; }
  .shot { background: #05060F; text-align: center; }
  .shot img { width: 100%; max-height: 460px; object-fit: contain; display: block; }
  .meta { padding: 12px 14px 14px; }
  .title { display: flex; justify-content: space-between; align-items: baseline; gap: 8px; }
  .title h2 { margin: 0; font-size: 15px; }
  .dead { font-size: 12px; color: #E8B54A; white-space: nowrap; }
  .dead.bad { color: #FF4D8D; }
  .bands { margin: 6px 0 0; font-size: 11.5px; color: #93A0BF; }
  .ocr { margin: 10px 0 0; padding: 9px 10px; background: #0A0E1A; border: 1px solid #1D2438;
         border-radius: 10px; font: 11.5px/1.45 Consolas, ui-monospace, monospace; color: #B9C4DE;
         max-height: 150px; overflow: auto; white-space: pre-wrap; }
</style>
'@)
[void]$sb.AppendLine('</head><body>')
[void]$sb.AppendLine("<h1>MarkUpTV &mdash; every page</h1>")
[void]$sb.AppendLine("<div class='sub'>$($rows.Count) captures from the Pixel 7 emulator. " +
                     "“Dead” is the share of the screen height that is a flat, empty band - the measurable form of an unbalanced page.</div>")
[void]$sb.AppendLine("<div class='grid'>")

foreach ($r in $rows) {
    $deadClass = if ($r.Dead -ge 32) { 'dead bad' } else { 'dead' }
    $ocr = ($r.Lines -join "`n")
    $ocrHtml = [System.Web.HttpUtility]::HtmlEncode($ocr)
    [void]$sb.AppendLine('<div class="card">')
    [void]$sb.AppendLine("  <div class='shot'><img src='tour/$($r.Png)' alt='$($r.Name)' loading='lazy'></div>")
    [void]$sb.AppendLine('  <div class="meta">')
    [void]$sb.AppendLine("    <div class='title'><h2>$($r.Name)</h2><span class='$deadClass'>dead $($r.Dead)%</span></div>")
    [void]$sb.AppendLine("    <div class='bands'>$($r.W)x$($r.H) &middot; dead bands: $([System.Web.HttpUtility]::HtmlEncode($r.Bands))</div>")
    [void]$sb.AppendLine("    <pre class='ocr'>$ocrHtml</pre>")
    [void]$sb.AppendLine('  </div>')
    [void]$sb.AppendLine('</div>')
}

[void]$sb.AppendLine('</div></body></html>')

Set-Content -Path $OutFile -Value $sb.ToString() -Encoding UTF8
Write-Host "gallery: $($rows.Count) pages -> $OutFile"
