param([string]$Dir = "C:\Users\hp\source\repos\MarkUptv\screenshots")
# Compare all *_g.png screenshots on a 4x4 brightness grid (PS 5.1-safe).
Add-Type -AssemblyName System.Drawing

Get-ChildItem -Path $Dir -Filter "*_g.png" | ForEach-Object {
    $path = $_.FullName
    try {
        $bmp = [System.Drawing.Bitmap]::FromFile($path)
        Write-Host ("== " + $_.Name)
        for ($gy = 0; $gy -lt 4; $gy++) {
            $row = ""
            for ($gx = 0; $gx -lt 4; $gx++) {
                $s = 0; $n = 0
                for ($i = $gx*270+10; $i -lt ($gx+1)*270; $i += 45) {
                    for ($j = $gy*600+10; $j -lt ($gy+1)*600; $j += 45) {
                        $c = $bmp.GetPixel($i, $j)
                        $s += ($c.R + $c.G + $c.B); $n++
                    }
                }
                if ($n -gt 0) { $row += ("{0,4}" -f [math]::Round($s/$n,0)) }
            }
            Write-Host $row
        }
        $bmp.Dispose()
    } catch {
        Write-Host ("== " + $_.Name + " -> unreadable: " + $_.Exception.Message)
    }
}
