$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$applicationDir = Join-Path $projectRoot 'artifacts\Roundtime'
$files = @('Roundtime.exe', 'Roundtime.Core.dll', 'Roundtime.exe.config') | ForEach-Object { Join-Path $applicationDir $_ }
foreach ($file in $files) { if (-not (Test-Path -LiteralPath $file)) { throw "Missing build output: $file. Run .\build.ps1 -Test first." } }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $applicationDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $applicationDir -Recurse -Force
$files += Join-Path $applicationDir 'README.md'
$files += Join-Path $applicationDir 'assets'
$archive = Join-Path $projectRoot 'artifacts\Roundtime-Windows.zip'
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Write-Output "Packaged: $archive"
