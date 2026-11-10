$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputPath = Join-Path $projectRoot 'dist/PopBar-win-x64'
dotnet publish (Join-Path $projectRoot 'src/PopBar') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputPath
Compress-Archive -Path (Join-Path $outputPath 'PopBar.exe'), (Join-Path $outputPath 'README.md') -DestinationPath (Join-Path $projectRoot 'dist/PopBar-win-x64.zip') -Force
Write-Output (Join-Path $projectRoot 'dist/PopBar-win-x64.zip')
