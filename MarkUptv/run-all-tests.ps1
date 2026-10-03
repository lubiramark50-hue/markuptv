$tests = @("soak", "community", "assistant", "soaknav")
$reportFile = "C:\Users\hp\AppData\Local\User Name\com.markup.markuptv\Data\selfcheck-report.txt"
$overallReport = ""

foreach ($test in $tests) {
    Write-Host "Running test: $test"
    $env:MARKUPTV_SELFCHECK = $test
    
    # Run the test
    dotnet run --framework net10.0-windows10.0.19041.0
    
    # Wait for the report file to be updated (we wait a few seconds)
    Start-Sleep -Seconds 2
    
    if (Test-Path $reportFile) {
        $reportContent = Get-Content $reportFile -Raw
        $overallReport += "`n`n=== TEST: $test ===`n$reportContent"
        Remove-Item $reportFile
    } else {
        $overallReport += "`n`n=== TEST: $test ===`nNO REPORT GENERATED"
    }
}

Set-Content -Path "C:\Users\hp\source\repos\MarkUptv\MarkUptv\all-tests-report.txt" -Value $overallReport
Write-Host "All tests completed."
