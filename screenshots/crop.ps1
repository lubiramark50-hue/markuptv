param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [Parameter(Mandatory = $true)][int]$W,
    [Parameter(Mandatory = $true)][int]$H,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$Scale = 4
)
# Crop a region of a screenshot and upscale it, so OCR can be re-run on the
# glyphs at a size where character loss is a rendering problem rather than a
# resolution problem.
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile($Path)
try {
    $rect = New-Object System.Drawing.Rectangle $X, $Y, $W, $H
    $crop = $src.Clone($rect, $src.PixelFormat)

    $dst = New-Object System.Drawing.Bitmap ($W * $Scale), ($H * $Scale)
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($crop, 0, 0, $W * $Scale, $H * $Scale)
    $g.Dispose()

    $dst.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $dst.Dispose()
    $crop.Dispose()
    Write-Host "wrote $Out ($($W * $Scale)x$($H * $Scale))"
}
finally {
    $src.Dispose()
}
