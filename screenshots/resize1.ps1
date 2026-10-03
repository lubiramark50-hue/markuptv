param([string]$In, [string]$Out, [int]$Width = 540)
# Downscale a PNG. Paths must be Windows-style (C:\...).
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($In)
$w = $Width
$h = [int]($img.Height * $Width / $img.Width)
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($img, 0, 0, $w, $h)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $img.Dispose()
Write-Host "resized -> $Out ($w x $h)"
