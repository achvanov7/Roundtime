param([switch]$Test, [switch]$Run)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path $compiler)) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    $compiler = Join-Path $frameworkRoot 'csc.exe'
}
if (-not (Test-Path $compiler)) { throw '.NET Framework 4.x compiler not found. Enable .NET Framework 4.8 on Windows.' }
$outputDir = Join-Path $projectRoot 'artifacts\Roundtime'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
function Compile([string[]]$CompilerArguments) {
    & $compiler /nologo /warn:4 /warnaserror+ /optimize+ /langversion:5 @CompilerArguments
    if ($LASTEXITCODE -ne 0) { throw "C# compilation failed ($LASTEXITCODE)." }
}
$coreSources = @(Get-ChildItem (Join-Path $projectRoot 'src\Roundtime.Core') -Filter '*.cs' | ForEach-Object FullName)
Compile (@('/target:library', "/out:$outputDir\Roundtime.Core.dll", '/reference:System.Web.Extensions.dll') + $coreSources)
if ($Test) {
    $testSources = @(Get-ChildItem (Join-Path $projectRoot 'tests\Roundtime.Tests') -Filter '*.cs' | Where-Object Name -ne 'LayoutTests.cs' | ForEach-Object FullName)
    Compile (@('/target:exe', "/out:$outputDir\Roundtime.Tests.exe", "/reference:$outputDir\Roundtime.Core.dll", '/reference:System.Web.Extensions.dll') + $testSources)
    & (Join-Path $outputDir 'Roundtime.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Behavioral tests failed.' }
}
$appSources = @(Get-ChildItem (Join-Path $projectRoot 'src\Roundtime.App') -Filter '*.cs' -ErrorAction SilentlyContinue | ForEach-Object FullName)
if ($appSources.Count -gt 0) {
    $wpf = Join-Path $frameworkRoot 'WPF'
    Compile (@('/target:winexe', "/out:$outputDir\Roundtime.exe", "/reference:$outputDir\Roundtime.Core.dll", "/reference:$wpf\PresentationFramework.dll", "/reference:$wpf\PresentationCore.dll", "/reference:$wpf\WindowsBase.dll", '/reference:System.Xaml.dll', "/resource:$projectRoot\src\Roundtime.App\MainWindow.xaml,Roundtime.MainWindow.xaml", "/resource:$projectRoot\src\Roundtime.App\Assets\Roundtime.ico,Roundtime.Icon.ico", "/resource:$projectRoot\src\Roundtime.App\Assets\Roundtime.Mark.xaml,Roundtime.Mark.xaml", "/win32icon:$projectRoot\src\Roundtime.App\Assets\Roundtime.ico", "/win32manifest:$projectRoot\src\Roundtime.App\app.manifest") + $appSources)
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Roundtime.App\App.config') -Destination (Join-Path $outputDir 'Roundtime.exe.config') -Force
    if ($Test) {
        Compile @('/target:exe', "/out:$outputDir\Roundtime.LayoutTests.exe", "/reference:$outputDir\Roundtime.exe", "/reference:$outputDir\Roundtime.Core.dll", "/reference:$wpf\PresentationFramework.dll", "/reference:$wpf\PresentationCore.dll", "/reference:$wpf\WindowsBase.dll", '/reference:System.Xaml.dll', (Join-Path $projectRoot 'tests\Roundtime.Tests\LayoutTests.cs'))
        & (Join-Path $outputDir 'Roundtime.LayoutTests.exe') (Join-Path $projectRoot 'src\Roundtime.App\MainWindow.xaml')
        if ($LASTEXITCODE -ne 0) { throw 'Window layout tests failed.' }
    }
    Write-Output "Built: $outputDir\Roundtime.exe"
    if ($Run) { Start-Process -FilePath (Join-Path $outputDir 'Roundtime.exe') }
}
