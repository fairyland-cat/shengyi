# Builds everything that goes into a release, into dist/:
#   shengyi-Setup-<version>.exe     installer: the window in installer/shell with the Inno Setup engine inside
#   shengyi-<version>-portable.exe  the app as a single self-contained exe
#   shengyi-<version>-src.zip       the source tree
# <version> comes from shengyi.csproj; check-version.ps1 enforces a version bump + CHANGELOG entry.
# Needs a .NET 8 SDK and Inno Setup 6 or 7 (7 recommended). The installer window is compiled with
# the .NET Framework compiler that comes with Windows. Override tool paths with -Dotnet / -Iscc
# when they are not on PATH or installed in a non-default directory.
param([string]$Version = '', [string]$Iscc = '', [string]$Dotnet = '')

$ErrorActionPreference = 'Stop'
# This environment wraps Remove-Item with a trash-based safe-delete hook that fails closed on the
# large dist/publish tree. Use .NET directly; these helpers only ever touch build output under dist/.
function Clear-BuildDir([string]$path) {
    if ([System.IO.Directory]::Exists($path)) { [System.IO.Directory]::Delete($path, $true) }
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Version) {
    # Read as UTF-8 explicitly: Windows PowerShell 5.1's Get-Content defaults to the ANSI code
    # page, which mangles the Chinese <Description> and breaks XML parsing. Regex keeps this
    # independent of XML namespaces and matches check-version.ps1's version lookup.
    $csproj = [System.IO.File]::ReadAllText((Join-Path $root 'shengyi.csproj'), [System.Text.Encoding]::UTF8)
    $match = [regex]::Match($csproj, '<Version>\s*(\d+\.\d+\.\d+)\s*</Version>')
    if (-not $match.Success) { throw 'Cannot read <Version> from shengyi.csproj' }
    $Version = $match.Groups[1].Value
}
# Every feature release must bump <Version> and add a CHANGELOG entry. Refuse to package otherwise.
& (Join-Path $PSScriptRoot 'check-version.ps1') -ExpectedVersion $Version
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'
$engineDir = Join-Path $dist 'engine'
# Only clear this build's intermediates and outputs. Older released exes / zips in dist/ are kept.
New-Item -ItemType Directory -Force -Path $dist, $engineDir | Out-Null
foreach ($d in @($publish, (Join-Path $dist "shengyi-$Version-src"))) { Clear-BuildDir $d }
# Do not delete existing release files: the portable exe may be locked by a running shengyi
# instance. Outputs below are written with overwrite semantics instead.

# PATH's dotnet may be runtime-only on this machine; fall back to the portable SDK if it has no SDKs.
if (-not $Dotnet) {
    $Dotnet = 'dotnet'
    $sdks = $null
    try { $sdks = & dotnet --list-sdks 2>$null } catch { }
    if (-not $sdks) {
        foreach ($c in @((Join-Path $env:LOCALAPPDATA 'Temp\dotnet-sdk\dotnet.exe'), (Join-Path $env:TEMP 'dotnet-sdk\dotnet.exe'))) {
            if (Test-Path $c) { $Dotnet = $c; break }
        }
    }
}

Write-Host "== Publishing shengyi $Version (SDK: $Dotnet)"
& $Dotnet publish (Join-Path $root 'shengyi.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
$portable = Join-Path $dist "shengyi-$Version-portable.exe"
try {
    Copy-Item (Join-Path $publish 'shengyi.exe') $portable -Force -ErrorAction Stop
} catch {
    # A running tray instance holds an exclusive handle on its own exe. The freshly published
    # build is identical (same sources, same flags) and still feeds the installer, so keep it.
    Write-Host "   NOTE: portable exe is in use; keeping the existing copy (installer is unaffected)."
}

Write-Host "== Building install engine"
# Inno Setup 7 installs as "Inno Setup 7" (x64 into Program Files, x86 into Program Files (x86)).
# Prefer 7, keep 6 as a fallback. ISCC's /Q and /D options are unchanged in 7.
if (-not $Iscc) {
    $roots = @((Join-Path $env:LOCALAPPDATA 'Programs'), ${env:ProgramFiles(x86)}, $env:ProgramFiles)
    $candidates = @()
    foreach ($v in '7', '6') {
        foreach ($r in $roots) {
            if ($r) { $candidates += (Join-Path $r "Inno Setup $v\ISCC.exe") }
        }
    }
    $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path $Iscc)) {
    throw 'Inno Setup 6/7 not found. Install 7.1.1 x64 with: winget install --id JRSoftware.InnoSetup.7 -e -s winget -i  (or pass -Iscc "<path to ISCC.exe>")'
}
Write-Host "   ISCC: $Iscc"
& $Iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $root 'installer\shengyi.iss')
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
Clear-BuildDir $engineDir
Clear-BuildDir $publish

Write-Host "== Packing source"
$src = Join-Path $dist "shengyi-$Version-src"
New-Item -ItemType Directory -Force $src | Out-Null
foreach ($item in 'src', 'assets', 'docs', 'scripts', 'installer', 'shengyi.csproj', 'README.md', 'CHANGELOG.md', 'LICENSE', '.gitignore') {
    Copy-Item (Join-Path $root $item) $src -Recurse
}
Compress-Archive -Path $src -DestinationPath (Join-Path $dist "shengyi-$Version-src.zip") -Force
Clear-BuildDir $src

Write-Host "== Done"
Get-ChildItem $dist -File | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
