$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testOutput = Join-Path $projectRoot 'artifacts/fixture'
$reportPath = Join-Path $projectRoot 'artifacts/selection-tests.txt'
dotnet build (Join-Path $projectRoot 'tests/PopBar.Fixture') -c Release -o $testOutput
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
# This is an intentionally visible, isolated test window.
$testProcess = Start-Process -FilePath (Join-Path $testOutput 'PopBar.Fixture.exe') -ArgumentList '--verify', ('"' + $reportPath + '"') -PassThru
if (-not $testProcess.WaitForExit(20000)) { $testProcess.Kill(); throw 'Selection tests timed out' }
if ($testProcess.ExitCode -ne 0) { throw 'Fixture crashed' }
$report = Get-Content -LiteralPath $reportPath
$report
if (($report | Where-Object { $_ -like 'FAIL*' }) -or @($report | Where-Object { $_ -like 'PASS*' }).Count -ne 6) { throw 'Selection tests failed' }
