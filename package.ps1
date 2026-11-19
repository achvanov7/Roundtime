$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$applicationDir = Join-Path $projectRoot 'artifacts\Roundtime'
$files = @('Roundtime.exe', 'DailyTimer.Core.dll', 'Roundtime.exe.config') | ForEach-Object { Join-Path $applicationDir $_ }
foreach ($file in $files) { if (-not (Test-Path -LiteralPath $file)) { throw "Missing build output: $file. Run .\build.ps1 -Test first." } }
foreach ($file in @('README.md', 'roundtime-screenshot.jpg', 'roundtime-rankings.jpg')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $applicationDir -Force
    $files += Join-Path $applicationDir $file
}
$archive = Join-Path $projectRoot 'artifacts\Roundtime-Windows.zip'
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Write-Output "Packaged: $archive"
