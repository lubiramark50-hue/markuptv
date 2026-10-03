param([string]$Path)
# OCR a screenshot PNG via Windows.Media.Ocr (PowerShell 5.1, WinRT projections).
# Prints "x,y : line text" per detected line; x,y is the line's left-center in image pixels.
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.InteropServices.WindowsRuntime
Add-Type -AssemblyName System.Runtime.WindowsRuntime   # AsTask + WindowsRuntimeStreamExtensions

# Force-load the WinRT projection namespaces
$null = [Windows.Media.Ocr.OcrEngine, Windows.Media.Ocr, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]

# Resolve the generic AsTask(IAsyncOperation<T>) extension once
$asTaskMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
                   $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
    Select-Object -First 1

function AwaitOp($op, [Type]$typeArg) {
    $task = $asTaskMethod.MakeGenericMethod($typeArg).Invoke($null, @($op))
    return $task.GetAwaiter().GetResult()
}

$img = [System.Drawing.Image]::FromFile($Path)
$stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
$netStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForWrite($stream)
$img.Save($netStream, [System.Drawing.Imaging.ImageFormat]::Png)
$img.Dispose()

$decoder = AwaitOp ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
$bitmap  = AwaitOp $decoder.GetSoftwareBitmapAsync() ([Windows.Graphics.Imaging.SoftwareBitmap])

$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if ($null -eq $engine) { Write-Error "OCR engine unavailable"; exit 1 }

$recognize = AwaitOp $engine.RecognizeAsync($bitmap) ([Windows.Media.Ocr.OcrResult])

foreach ($line in $recognize.Lines) {
    $words = @($line.Words)
    $w0 = $words[0]
    if ($w0 -is [System.Array]) { $w0 = @($w0)[0] }
    try {
        $x = [int][double]$w0.BoundingRect.X
        $y = [int][double]($w0.BoundingRect.Y + $w0.BoundingRect.Height / 2)
    } catch { $x = 0; $y = 0 }
    $text = ($words | ForEach-Object { $w = $_; if ($w -is [System.Array]) { $w = @($w)[0] }; $w.Text }) -join ' '
    Write-Host ("{0},{1} : {2}" -f $x, $y, $text)
}
