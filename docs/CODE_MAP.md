# 绳忆代码地图：改动前先读这一页

[README.md](../README.md) 的「从源码构建」章节有逐文件的模块职责表。本文件补的是 README 不记录、但每次改动都会踩到的三类知识：**一个改动会牵动哪些文件**、**哪些设计是刻意为之**、**发布流水线为什么必须一次跑完**。目标是在动手之前一次拿到完整影响面，避免边改边找、漏改返工。

---

## 1. 改动影响面矩阵

几乎不存在「只改一个文件」的功能改动。按层列出必查点：

| 层 | 文件 | 什么时候必须动 |
|---|---|---|
| 版本 | `shengyi.csproj` `<Version>`（唯一版本源）、`installer/shengyi.iss` `#define AppVersion`、`CHANGELOG.md` 顶部条目 | 每次产品功能/缺陷改动，按下方版本规则 |
| 设置 | `Settings.cs`（字段、默认值、`Load()` 里的旧配置迁移）、`Theme.cs`（快照属性）、`Dialogs.cs`（控件与 `Refresh()`）、`Ui.cs`（控件工厂）、`Strings.cs`（中英文案） | 新增、重命名、改单位、改取值范围 |
| 纯逻辑 | `src/` 下零依赖模块 + `_verify/Program.cs` 断言 + `_verify/verify.csproj` 的 `<Compile Include>` | 新增算法、解析规则、换算、识别逻辑 |
| 用户可见层 | `CardView.cs`（外观、尺寸、悬浮提示）、`LineWindow.cs`（布局、绳子渐变）、`SearchPanel.cs`（筛选抽屉）、`Shots.cs`（截图场景） | 外观、尺寸、排序、渐变、截图 |
| 搜索 | `ClipText.cs`（可索引文本）、`SearchIndex.cs`、`SearchQuery.cs`、`SearchPanel.cs`、`Strings.cs` | 索引字段、查询语法、筛选条件 |
| 文档 | `README.md`（功能说明 + 源码清单表）、`docs/*.png` | 用户可见变化、升版本 |
| 全局快捷键 | `Watchers.cs`（`MessageWindow` 注册 / 注销 / `ProbeChord`）、`Dialogs.cs`（`ChordFieldBox`、`LocalChordCheck`、保存拦截）、`Strings.cs`、`KeyChord.cs`、`HotkeyCheck.cs`、`AppController.cs`（注入回调）、`Shots.cs`（离屏渲染） | 新增组合键、改探测或注册状态、改冲突判定 |
| 发布 | `scripts/build-release.ps1`、`check-version.ps1`、`check-release.ps1` | 发布链路本身；日常改动无需动 |

**实例（0.8.0，把统计窗口从天改成分钟）** 实际联动 12 处：新增 `FreqWindow.cs`；改 `Clips.cs`（截止与计数）、`Settings.cs`（字段 + 旧 `FreqWindowDays` ×1440 迁移）、`Theme.cs`（快照）、`Dialogs.cs`（可输入数字步进器）、`Ui.cs`（步进器实现）、`Strings.cs`（单位与提示）、`CardView.cs`（悬浮提示易读单位）；改 `_verify/Program.cs` + `verify.csproj`；改 `README.md`、`CHANGELOG.md`、`shengyi.csproj`、`installer/shengyi.iss`；最后重拍全部截图。只搜 `FreqWindowDays` 会漏掉 README 里的「近 N 天」、CHANGELOG、截图版本号和旧配置迁移。

**实例（0.9.0，组合键占用提示）** 联动 9 处：新增 `HotkeyCheck.cs`；改 `Watchers.cs`（`MessageWindow` 注册状态镜像 + `ProbeChord`）、`Dialogs.cs`（`ChordFieldBox`、保存拦截、`LocalChordCheck`）、`Strings.cs`、`AppController.cs`（注入 `messages.ProbeChord`）、`_verify/Program.cs` + `verify.csproj`、`README.md`、`CHANGELOG.md`、版本两处。`Shots.cs` 无需改：构造函数第 5 个参数可空，离屏渲染时 `probeChord = null` 自动降级为「保存时检查是否可用」，不会在截图里留下「检测中」中间态。关键约束：**探测必须先注销自身 4 个热键 ID**（否则自己的键 100% 误报为被占用），恢复时尊重录制主热键期间的挂起状态，且保存必须同步复验、不能只依赖防抖定时器。

动手前先跑一次：

```powershell
powershell -File scripts\impact.ps1 -Keyword FreqWindowMinutes
```

它输出关键词命中 + 上面的横切清单，一次拿全影响面（支持 `-Regex`）。

---

## 2. 已确认的设计约束（不要当成 bug 修）

这些是反复被误判过的点，改之前先确认意图：

- **卡片尺寸按内容类型固定，是预期行为**。`CardView.BaseSizeOf()` 按 Kind 写死（文字 150×104、代码 184×104、链接 180×76、文件 170×62、便签 158×112、色卡 88×94），只有图片按宽高比变化。用户 2026-11-07 明确接受现状，不要「修复」成随字数自适应。
- **按类型配色默认关闭**（`CardTintScheme = "off"`），老用户升级外观零变化；需在设置里主动选方案。改成「升级即彩色」属外观破坏性变更，先问用户。
- **色值卡（`ClipKind.Color`）与图片卡（`ClipKind.Image`）完全不染色**，也不贡献绳子渐变 stop；`CardTints.IsUntinted` 保证这一点。
- **文字/链接/代码粘贴时自带的颜色必须做 WCAG 对比度保护**：解析 HTML CSS / RTF 颜色表，保留色相但按实测对比度推到 ≥4.5:1（对卡片上下沿同时成立）。实现在 `CardTints.SafeInk` / `ExtractRichColors`。
- **原有外观设置没有被取代，不能删**。`ThemePalette` 约 50 个颜色字段、产品代码约 135 处 `p.*` 引用仍依赖主题（窗口、设置面板、搜索栏、Tab、夹子、色值卡/图片卡外框、便签默认纸色、绳子基色、按钮与选中环）。按类型配色只是叠在主题上的可选层。
- **智能排序窗口以分钟计**（1–129600，默认 10080 = 7 天），旧 `FreqWindowDays` 在 `Settings.Load()` 中迁移。筛选抽屉的「今天 / 7 天 / 30 天」与 `in:7d` 是**搜索时间过滤**，与统计窗口无关，不要一起改。
- **便签的第二根绳、Markdown、待办、提醒均未实现**，不要在文档里当成已有能力。
- 搜索入口只在键盘模式显示：鼠标顶边呼出走 `Reveal(false)`，`bar` 折叠；真实热键 Alt+V / Win+V。看不到搜索框不是 bug。
- **粘贴方式的「下一条」组合键不是队列启动键**：队列只能由卡片右键「逐条粘贴这 N 条…」创建（手动模式要求该开关已开启），第 1 条在启动时立即贴出。空队列按组合键必须给托盘提示，不能静默返回；`Actions.SequenceNext()` 用 `seqTimer.IsEnabled` 区分「热键空按」与「队列正常收尾」。
- **`QuickInsert()` 里 `PickInsertItem()` 必须早于 `RetractAll()`**：`ActiveKeyboardWindow` 要求 `IsOut && KeyboardMode`，收起后恒为 false，搜索首条命中会丢失。收起后还要 `ForceForeground` + 90 ms 等待并释放仍按住的 Shift/Ctrl/Alt，否则 Unicode 会打进正在关闭的绳子窗口。
- **热键注册失败要报出具体键名并用弹窗告知**：托盘气泡常被通知中心吞掉。`MessageWindow.SetAuxHotkeys()` 返回逐键 `AuxHotkeyResult`，`HotkeyCheck.FailedChords()` 只列「已启用且确实失败」的键；便签热键 `SetNoteHotkey()` 的返回值同样不能忽略。

---

## 3. 版本规则（强制）

- 主.次.修：**新增功能 → 次版本 +1**；只修缺陷 → 修订号 +1；破坏性/数据格式不兼容 → 主版本 +1；混合按最高档升一次。
- 唯一版本源是 `shengyi.csproj` 的 `<Version>`；必须同步 `installer/shengyi.iss` 的 `#define AppVersion`，并在 `CHANGELOG.md` 顶部新增条目。
- `check-version.ps1` 校验一致性、递增与 CHANGELOG；`build-release.ps1` 自动调用，不通过直接拒绝打包。产物名与 Release tag 都从 csproj 派生，不要覆盖旧 Release 资产。
- **只改工程脚本 / 文档不算产品功能，不升版本**；但会改变源码包内容，需要重跑一次 `build-release.ps1`。

---

## 4. 发布流水线：一次调用完成

旧流程有回环：脚本把 `docs/` 打进源码包，但截图必须用脚本产出的新 exe 渲染，于是「打包 → 重拍 → 包过期 → 再打包」，一次发版曾重复 5 次全量打包。现在截图生成已并入脚本内部：

```
check-version（自动）
  → dotnet publish（便携 exe）
  → ISCC（安装引擎）+ .NET Framework 编译安装窗口
  → 用本次构建的 exe 重拍 docs/*.png 与 setup-*.png   ← 关键：在打 zip 之前
  → 拷贝源码 + docs + scripts → 压缩 src.zip
  → check-release（自动：版本、截图、源码包一致性、产物摘要）
```

```powershell
powershell -File scripts\build-release.ps1 -Iscc "D:\App\Develop\Inno Setup 7\ISCC.exe"
```

- 截图进程必须**显式等待**：`--shots` / `--shot` 都是 GUI 子系统程序，PowerShell 的 `&` 不会等它们退出，脚本可能在渲染写完前就把 `docs/` 打进 zip（`check-release` 之前确实抓到过这种半渲染状态）。脚本用 `Start-Process -Wait -PassThru` 并检查退出码。
- 一次运行即产出三件一致产物，**不要在打包之后再手工重拍或改代码**。
- `-SkipShots` 可跳过截图（例如渲染环境异常），但此时 `docs/*.png` 可能残留旧版本号，`check-release.ps1` 只比对字节一致性、不做图像文字识别，发布前需目视确认。
- `check-release.ps1` 也可单独运行：它比对源码包内 `docs/*.png`、`src/*.cs`、`scripts/*.ps1` 与仓库的 sha256，能直接发现「改了文件但没重打包」。
- 「打包后又改了一行」会让源码包立刻过期——这是本仓库最常见的隐性返工来源。

---

## 5. 建议的改动流程

1. 读本文件 + README 模块表，确认是否触碰第 2 节的约束。
2. `scripts\impact.ps1 -Keyword <关键词>` 拿命中与必查清单。
3. **代码冻结**：源码、文案、断言、README、CHANGELOG、版本四处一次性改完。
4. `dotnet build -c Release` 零警告；`_verify` 全 PASS；`--preview` 冒烟。
5. 只跑**一次** `build-release.ps1`（内含截图 + `check-release`）。
6. 必要时再发布；远端推送与 Release 由仓库外的 `sync_release.py` 处理。

---

## 6. 环境与已知陷阱

- PATH 里的 `dotnet` 只有运行时；可用 SDK 在 `C:\Users\wangrt\AppData\Local\Temp\dotnet-sdk\dotnet.exe`（8.0.427）。脚本会自动回退。
- Inno Setup 7.1.1 装在自定义路径 `D:\App\Develop\Inno Setup 7`，脚本探测不到，需传 `-Iscc`。
- **UTF-8 BOM**：`.iss`、`installer/shell/Setup.cs`、`scripts/*.ps1` 含中文时必须带 BOM，否则 Windows PowerShell 5.1 / ISCC / .NET Framework 编译器按 ANSI 读会乱码。`check-version.ps1` 用 `Get-Content -Raw` 只匹配 ASCII 版本号，不受影响。
- 安全删除钩子会包装 `Remove-Item -Recurse`；脚本清 dist  intermediates 用 .NET `Directory.Delete`。
- 旧便携 exe 可能被正在运行的实例占用；脚本跳过覆盖并提示，不结束用户进程。
- 安装程序未签名，首次运行会触发 SmartScreen。
- GitHub 远端提交用 REST API，本地 git HEAD 常落后于远端，**不要直接 `git push`**；PAT 只在进程内传递、不落盘、用后吊销。源码包上传易被代理 502 打断，`sync_release.py` 按 asset digest 幂等判断，失败直接重跑即可。
