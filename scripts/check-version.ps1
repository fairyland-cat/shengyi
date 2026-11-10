# Guards a release: the version in shengyi.csproj must be consistent everywhere,
# recorded at the top of CHANGELOG.md, and strictly newer than the previous release.
# build-release.ps1 calls this automatically before publishing anything.
param([string]$ExpectedVersion = '')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Find-Version([string]$text, [string]$pattern, [string]$where) {
    $m = [regex]::Match($text, $pattern)
    if (-not $m.Success) { throw "找不到版本号：$where" }
    return $m.Groups[1].Value
}

$current = Find-Version (Get-Content -Raw (Join-Path $root 'shengyi.csproj')) `
    '<Version>\s*(\d+\.\d+\.\d+)\s*</Version>' 'shengyi.csproj 的 <Version>'

$iss = Find-Version (Get-Content -Raw (Join-Path $root 'installer\shengyi.iss')) `
    '#define\s+AppVersion\s+"(\d+\.\d+\.\d+)"' 'installer/shengyi.iss 的 AppVersion'
if ($iss -ne $current) {
    throw "版本号不一致：shengyi.csproj=$current，installer/shengyi.iss=$iss"
}

if ($ExpectedVersion -and $ExpectedVersion -ne $current) {
    throw "传入的版本 $ExpectedVersion 与 shengyi.csproj 的 $current 不一致"
}

$log = Get-Content -Raw (Join-Path $root 'CHANGELOG.md')
$entries = @([regex]::Matches($log, '(?m)^##\s+\[?v?(\d+\.\d+\.\d+)\]?') | ForEach-Object { $_.Groups[1].Value })
if ($entries.Count -eq 0) { throw 'CHANGELOG.md 里没有版本条目（应有 ## [x.y.z] - 日期）' }
if ($entries[0] -ne $current) {
    throw "CHANGELOG.md 顶部是 $($entries[0])，但 csproj 是 $current；发版前请新增 CHANGELOG 条目"
}
if ($entries.Count -gt 1 -and ([version]$current) -le ([version]$entries[1])) {
    throw "版本必须递增：$current 没有高于上一版 $($entries[1])；新增功能请升次版本"
}

$prev = if ($entries.Count -gt 1) { "上一版 $($entries[1])" } else { '首个记录版本' }
Write-Host "版本检查通过：绳忆 $current（$prev）"
