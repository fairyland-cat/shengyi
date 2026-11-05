# Builds everything that goes into a release, into dist/:
#   shengyi-Setup-<version>.exe     installer: the window in installer/shell with the Inno Setup engine inside
#   shengyi-<version>-portable.exe  the app as a single self-contained exe
#   shengyi-<version>-src.zip       the source tree
# <version> comes from shengyi.csproj; check-version.ps1 enforces a version bump + CHANGELOG entry.
# Needs the .NET 8 SDK and Inno Setup 6. The installer window is compiled with the .NET Framework
# compiler that comes with Windows.
param([string]$Version = '')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'shengyi.csproj'))).Project.PropertyGroup.Version | Select-Object -First 1
}
# Every feature release must bump <Version> and add a CHANGELOG entry. Refuse to package otherwise.
& (Join-Path $PSScriptRoot 'check-version.ps1') -ExpectedVersion $Version
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'
$engineDir = Join-Path $dist 'engine'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

Write-Host "== Publishing shengyi $Version"
dotnet publish (Join-Path $root 'shengyi.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $publish 'shengyi.exe') (Join-Path $dist "shengyi-$Version-portable.exe")

Write-Host "== Building install engine"
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }
& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $root 'installer\shengyi.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }

Write-Host "== Building installer window"
$shell = Join-Path $root 'installer\shell'
$info = Join-Path $engineDir 'AssemblyInfo.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("绳忆 安装程序")]
[assembly: AssemblyProduct("绳忆")]
[assembly: AssemblyCompany("shengyi")]
[assembly: AssemblyCopyright("Copyright (c) 2026 shengyi")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
"@ | Set-Content -LiteralPath $info -Encoding UTF8
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$icon = Join-Path $root 'assets\shengyi.ico'
& (Join-Path $fx 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ `
    "/out:$(Join-Path $dist "shengyi-Setup-$Version.exe")" "/win32icon:$icon" "/win32manifest:$(Join-Path $shell 'app.manifest')" `
    "/resource:$(Join-Path $shell 'SetupWindow.xaml'),Shengyi.Setup.Window" "/resource:$icon,Shengyi.Setup.Icon" `
    "/resource:$(Join-Path $engineDir 'shengyi-Engine.exe'),Shengyi.Setup.Engine" `
    "/reference:$fx\WPF\WindowsBase.dll" "/reference:$fx\WPF\PresentationCore.dll" "/reference:$fx\WPF\PresentationFramework.dll" `
    /reference:System.Xaml.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll `
    (Join-Path $shell 'Setup.cs') $info
if ($LASTEXITCODE) { throw 'Installer window failed to compile' }
Remove-Item $engineDir, $publish -Recurse -Force

Write-Host "== Packing source"
$src = Join-Path $dist "shengyi-$Version-src"
New-Item -ItemType Directory $src | Out-Null
foreach ($item in 'src', 'assets', 'docs', 'scripts', 'installer', 'shengyi.csproj', 'README.md', 'CHANGELOG.md', 'LICENSE', '.gitignore') {
    Copy-Item (Join-Path $root $item) $src -Recurse
}
Compress-Archive -Path $src -DestinationPath (Join-Path $dist "shengyi-$Version-src.zip")
Remove-Item $src -Recurse -Force

Write-Host "== Done"
Get-ChildItem $dist -File | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
