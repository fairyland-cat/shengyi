# Change-impact helper: keyword hits + the cross-cutting files a feature change usually touches.
# Run BEFORE editing, so the full blast radius is known in one call instead of discovered while
# editing. Usage:
#   powershell -File scripts\impact.ps1 -Keyword FreqWindowMinutes
#   powershell -File scripts\impact.ps1 -Keyword "FreqWindow|DaysUnit" -Regex
param([Parameter(Mandatory = $true)][string]$Keyword, [switch]$Regex)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$workspace = (Resolve-Path (Join-Path $root '..')).Path
$verify = Join-Path $workspace '_verify'

$files = @()
foreach ($dir in @((Join-Path $root 'src'), (Join-Path $root 'installer'), (Join-Path $root 'scripts'), (Join-Path $root 'docs'), $verify)) {
    if (Test-Path $dir) {
        $files += @(Get-ChildItem $dir -Recurse -File -Include *.cs, *.iss, *.ps1, *.md, *.csproj | Where-Object {
            $_.FullName -notmatch '\\(dist|bin|obj)\\' -and $_.Name -ne 'CODE_MAP.md' -and $_.FullName -ne $PSCommandPath
        })
    }
}
foreach ($name in @('README.md', 'CHANGELOG.md', 'shengyi.csproj')) {
    $p = Join-Path $root $name
    if (Test-Path $p) { $files += (Get-Item $p) }
}

$params = @{ Path = $files.FullName; Pattern = $Keyword; ErrorAction = 'SilentlyContinue' }
if (-not $Regex) { $params['SimpleMatch'] = $true }
$hits = @(Select-String @params)
Write-Host "== 关键词命中: $Keyword （$($hits.Count) 处 / $(@($hits | Select-Object -ExpandProperty Path -Unique).Count) 个文件）"
$limit = [Math]::Min($hits.Count, 60)
for ($i = 0; $i -lt $limit; $i++) {
    $h = $hits[$i]
    $rel = $h.Path.Replace($workspace, '').TrimStart('\', '/')
    $line = $h.Line.Trim()
    if ($line.Length -gt 110) { $line = $line.Substring(0, 110) + '...' }
    Write-Host ('   {0}:{1}  {2}' -f $rel, $h.LineNumber, $line)
}
if ($hits.Count -gt $limit) { Write-Host "   ... 另有 $($hits.Count - $limit) 处未显示（换更精确的关键词）" }

Write-Host ''
Write-Host '== 每次功能改动必查（横切清单）'
$checks = @(
    '版本 4 处    shengyi.csproj <Version>（唯一源） / installer/shengyi.iss AppVersion / CHANGELOG.md 顶部条目 / README.md 说明',
    '设置 5 处    src/Settings.cs（字段+默认值+旧配置迁移） / src/Theme.cs 快照 / src/Dialogs.cs 控件 / src/Ui.cs 控件工厂 / src/Strings.cs 中英文案',
    '逻辑与断言  对应纯逻辑模块 + _verify/Program.cs 断言 + _verify/verify.csproj 的 <Compile Include>',
    '用户可见层  src/CardView.cs 提示与外观 / src/LineWindow.cs 绳子 / src/SearchPanel.cs 筛选 / src/Shots.cs 截图场景',
    '文档与截图  README.md 功能说明与源码清单 / docs/*.png（升版本必须重拍，build-release.ps1 现在自动完成）',
    '发布校验    scripts/check-version.ps1（版本递增） / scripts/check-release.ps1（产物、截图、源码包一致性）'
)
foreach ($c in $checks) { Write-Host "   - $c" }
Write-Host ''
Write-Host '== 流程纪律'
Write-Host '   1. 代码冻结：所有源码、文案、断言、文档一次性改完，再进入构建。'
Write-Host '   2. dotnet build + _verify + --preview 通过。'
Write-Host '   3. 只跑一次 build-release.ps1（脚本内已含截图生成与 check-release）。'
Write-Host '   4. 打包后若又改了任何文件或脚本，源码包即刻过期，必须重跑；详见 docs/CODE_MAP.md。'
