# One-shot verification of a locally built release.
# build-release.ps1 runs this at the end; it can also be run on its own after a partial rebuild:
#   powershell -File scripts\check-release.ps1
# It checks that versions agree, that the source archive contains the CURRENT docs shots and
# source files (the old pack -> re-shoot -> re-pack loop used to leave these stale), and prints
# size + sha256 for the three release assets.
param([string]$ExpectedVersion = '')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dist = Join-Path $root 'dist'
$script:fail = New-Object System.Collections.Generic.List[string]

function Note([bool]$ok, [string]$label, [string]$detail) {
    $mark = 'OK  '
    if (-not $ok) { $mark = 'FAIL' }
    Write-Host ('{0}  {1} {2}' -f $mark, $label.PadRight(30), $detail)
    if (-not $ok) { $script:fail.Add($label) }
}
function Get-Sha([System.IO.Stream]$stream) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Get-FileSha([string]$path) {
    $fs = [System.IO.File]::OpenRead($path)
    try { return Get-Sha $fs } finally { $fs.Dispose() }
}
function Get-EntrySha([System.IO.Compression.ZipArchiveEntry]$e) {
    $s = $e.Open()
    try { return Get-Sha $s } finally { $s.Dispose() }
}
function Get-EntryText([System.IO.Compression.ZipArchiveEntry]$e) {
    $s = $e.Open()
    $r = New-Object System.IO.StreamReader($s, [System.Text.Encoding]::UTF8, $true)
    try { return $r.ReadToEnd() } finally { $r.Dispose() }
}
function Find-Entry([string]$relative) {
    $needle = '/' + $relative.Replace('\', '/')
    foreach ($k in $script:zipEntries.Keys) {
        if ($k.EndsWith($needle, [System.StringComparison]::OrdinalIgnoreCase)) { return $script:zipEntries[$k] }
    }
    return $null
}
function Check-Group([string]$label, [string]$folder, [string]$pattern) {
    $files = @(Get-ChildItem (Join-Path $root $folder) -File | Where-Object { $_.Name -like $pattern })
    $bad = @()
    foreach ($f in $files) {
        $e = Find-Entry ($folder + '/' + $f.Name)
        if ($null -eq $e) { $bad += ($f.Name + ' (missing)'); continue }
        if ((Get-FileSha $f.FullName) -ne (Get-EntrySha $e)) { $bad += $f.Name }
    }
    $matched = $files.Count - $bad.Count
    $detail = "$matched/$($files.Count) 一致"
    if ($bad.Count -gt 0) { $detail += " · 差异: $($bad -join ', ')" }
    Note (($bad.Count -eq 0) -and ($files.Count -gt 0)) $label $detail
}

$csproj = [System.IO.File]::ReadAllText((Join-Path $root 'shengyi.csproj'), [System.Text.Encoding]::UTF8)
$m = [regex]::Match($csproj, '<Version>\s*(\d+\.\d+\.\d+)\s*</Version>')
if (-not $m.Success) { throw 'Cannot read <Version> from shengyi.csproj' }
$v = $m.Groups[1].Value
if ($ExpectedVersion -and $ExpectedVersion -ne $v) {
    throw "传入的版本 $ExpectedVersion 与 shengyi.csproj 的 $v 不一致"
}

$portable = Join-Path $dist "shengyi-$v-portable.exe"
$setup = Join-Path $dist "shengyi-Setup-$v.exe"
$zipPath = Join-Path $dist "shengyi-$v-src.zip"
$assets = @(
    @{ Name = 'portable'; Path = $portable },
    @{ Name = 'setup';    Path = $setup },
    @{ Name = 'src.zip';  Path = $zipPath }
)
foreach ($a in $assets) { Note (Test-Path $a.Path) ('产物存在 · ' + $a.Name) (Split-Path $a.Path -Leaf) }
if ($script:fail.Count -gt 0) { throw "缺少发布产物：$($script:fail -join ', ')" }

Write-Host ' '
Write-Host "== 版本 $v 一致性"
foreach ($a in $assets[0..1]) {
    $fv = (Get-Item $a.Path).VersionInfo.FileVersion
    Note ($fv -and $fv.StartsWith($v)) ('版本信息 · ' + $a.Name) $fv
}
& (Join-Path $PSScriptRoot 'check-version.ps1') -ExpectedVersion $v | Out-Host

$z = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $script:zipEntries = @{}
    foreach ($e in $z.Entries) { $script:zipEntries[($e.FullName -replace '\\', '/')] = $e }
    Write-Host ("   源码包内共 {0} 个条目" -f $z.Entries.Count)

    $inProj = Find-Entry 'shengyi.csproj'
    $projVer = ''
    if ($null -ne $inProj) {
        $pm = [regex]::Match((Get-EntryText $inProj), '<Version>\s*(\d+\.\d+\.\d+)\s*</Version>')
        if ($pm.Success) { $projVer = $pm.Groups[1].Value }
    }
    Note ($projVer -eq $v) '源码包内 csproj' $(if ($projVer) { $projVer } else { '缺失' })

    $inIss = Find-Entry 'installer/shengyi.iss'
    $issVer = ''
    if ($null -ne $inIss) {
        $im = [regex]::Match((Get-EntryText $inIss), '#define\s+AppVersion\s+"(\d+\.\d+\.\d+)"')
        if ($im.Success) { $issVer = $im.Groups[1].Value }
    }
    Note ($issVer -eq $v) '源码包内 iss' $(if ($issVer) { $issVer } else { '缺失' })

    $inLog = Find-Entry 'CHANGELOG.md'
    $logVer = ''
    if ($null -ne $inLog) {
        $lm = [regex]::Match((Get-EntryText $inLog), '(?m)^##\s+\[?v?(\d+\.\d+\.\d+)\]?')
        if ($lm.Success) { $logVer = $lm.Groups[1].Value }
    }
    Note ($logVer -eq $v) '源码包内 CHANGELOG' $(if ($logVer) { $logVer } else { '缺失' })

    Write-Host ' '
    Write-Host '== 截图与源码是否与仓库一致'
    Check-Group 'docs 截图' 'docs' '*.png'
    Check-Group 'src 源码' 'src' '*.cs'
    Check-Group 'scripts 脚本' 'scripts' '*.ps1'
}
finally { $z.Dispose() }

Write-Host ' '
Write-Host '== 产物摘要'
foreach ($a in $assets) {
    $len = (Get-Item $a.Path).Length
    Write-Host ('   {0,-34} {1,12:N0} B  sha256 {2}' -f (Split-Path $a.Path -Leaf), $len, (Get-FileSha $a.Path).Substring(0, 16))
}
Write-Host ' '
if ($script:fail.Count -gt 0) { throw "发布校验失败：$($script:fail -join ', ')" }
Write-Host "发布校验通过：绳忆 $v"
