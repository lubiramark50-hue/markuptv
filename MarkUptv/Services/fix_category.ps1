$content = Get-Content C:\Users\hp\source\repos\MarkUptv\MarkUptv\Services\AiTvService.cs -Raw
$content = [regex]::Replace($content, '(c\.Category\?\.Contains\([^)]+\))(?!\s*==\s*true)', '$1 == true')
Set-Content -Path C:\Users\hp\source\repos\MarkUptv\MarkUptv\Services\AiTvService.cs -Value $content
Write-Host "Done"
