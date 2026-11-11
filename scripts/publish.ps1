$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputPath = Join-Path $projectRoot 'dist/PopBar-win-x64-lite'
$executablePath = Join-Path $outputPath 'PopBar.exe'
$running = Get-Process -Name PopBar -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executablePath }
if ($running) { throw "Please exit PopBar from the system tray before publishing: $executablePath (PID: $($running.Id -join ', ')). The output directory will not be changed." }
# The native GUI apphost checks for the Desktop Runtime before running managed code.
# Keep a single EXE, but do not bundle the .NET runtime or framework assemblies.
dotnet publish (Join-Path $projectRoot 'src/PopBar') -c Release -r win-x64 --self-contained false -p:UseAppHost=true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputPath
Compress-Archive -Path (Join-Path $outputPath 'PopBar.exe'), (Join-Path $outputPath 'README.md'), (Join-Path $outputPath 'Assets') -DestinationPath (Join-Path $projectRoot 'dist/PopBar-win-x64-lite.zip') -Force
Write-Output $executablePath
Write-Output (Join-Path $projectRoot 'dist/PopBar-win-x64-lite.zip')
