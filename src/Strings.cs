using System.Globalization;

namespace Shengyi;

// UI strings. English and Simplified Chinese, chosen from the Windows display language.
static class S
{
    static readonly bool Zh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
    static string T(string en, string zh) => Zh ? zh : en;

    public const string AppName = "绳忆";

    // Tray
    public static string ToggleLine => T("Show / hide clipboard", "显示 / 隐藏剪贴板");
    public static string Pause => T("Pause recording", "暂停记录");
    public static string PausedUntil => T("Paused until {0:HH:mm}", "已暂停至 {0:HH:mm}");
    public static string PausedForever => T("Paused", "已暂停");
    public static string Pause10 => T("For 10 minutes", "10 分钟");
    public static string Pause60 => T("For 1 hour", "1 小时");
    public static string PauseManual => T("Until I resume", "直到手动恢复");
    public static string Resume => T("Resume recording", "恢复记录");
    public static string SettingsMenu => T("Settings…", "设置…");
    public static string OpenFolder => T("Open data folder", "打开数据文件夹");
    public static string ClearLine => T("Clear history (keep pinned & notes)…", "清空历史（保留固定项与便签）…");
    public static string ClearConfirm => T("Delete {0} unpinned items?", "删除 {0} 条未固定的记录？");
    public static string Quit => T("Quit", "退出");
    public static string Welcome => T("Copy something, then rest the pointer at the top edge of the screen or press {0}.",
                                      "复制点什么，然后把鼠标停在屏幕顶边，或按 {0}。");
    public static string HotkeyFailed => T("The shortcut {0} is already used by another app.", "快捷键 {0} 已被其他程序占用。");

    // Line
    public static string Hint => T("Things you copy will hang here", "复制的内容会挂在这里");
    public static string NoMatches => T("Nothing matches", "没有匹配的内容");
    public static string Copied => T("Copied", "已复制");
    public static string Chars => T("{0} chars", "{0} 字");
    public static string Lines => T("{0} lines", "{0} 行");
    public static string MoreFiles => T("+{0} more", "另 {0} 项");
    public static string SearchPlaceholder => T("Type to search", "输入以搜索");
    public static string FilterAll => T("All", "全部");
    public static string FilterText => T("Text", "文字");
    public static string FilterImages => T("Images", "图片");
    public static string FilterLinks => T("Links", "链接");
    public static string FilterFiles => T("Files", "文件");
    public static string FilterPinned => T("Pinned", "已固定");
    public static string KeysHelpCopy => T("Enter copy · Shift+Enter paste · Ctrl+P pin · Del delete · Esc close",
                                           "Enter 复制 · Shift+Enter 粘贴 · Ctrl+P 固定 · Del 删除 · Esc 关闭");
    public static string KeysHelpPaste => T("Enter paste · Shift+Enter copy · Ctrl+P pin · Del delete · Esc close",
                                            "Enter 粘贴 · Shift+Enter 复制 · Ctrl+P 固定 · Del 删除 · Esc 关闭");

    // Advanced search & filters
    public static string FilterGearTip => T("Advanced filters", "高级筛选");
    public static string FilterSource => T("Source", "来源");
    public static string FilterKind => T("Type", "类型");
    public static string FilterTime => T("When", "时间");
    public static string FilterAny => T("Any", "不限");
    public static string FilterToday => T("Today", "今天");
    public static string FilterDays7 => T("7 days", "7 天内");
    public static string FilterDays30 => T("30 days", "30 天内");
    public static string FilterClear => T("Clear filters", "清除筛选");
    public static string FilterNoApps => T("No source recorded yet", "暂无来源记录");
    public static string FilterResult => T("{0} results", "{0} 条结果");
    public static string FilterHint => T("Pinyin works · try app:chrome, tag:phone / tag:手机号, cat:音视频 / cat:media, kind:image, in:7d",
                                         "支持拼音 · 可用 app:chrome、tag:phone / tag:手机号、cat:音视频 / cat:media、kind:图片、in:7d");
    public static string[] FilterKindNames => new[]
    {
        T("Text", "文字"), T("Code", "代码"), T("Links", "链接"), T("Colours", "颜色"),
        T("Images", "图片"), T("Files", "文件"), T("Notes", "便签"),
    };
    public static string SourceTip => T("From {0}", "来自 {0}");
    public static string SectionSearch => T("Search", "搜索");
    public static string RecordSourceTitle => T("Record source app", "记录来源应用");
    public static string RecordSourceNote => T("Remember which program each copy came from and which clipboard formats it used. Nothing leaves this computer.",
                                               "记住每条内容从哪个程序复制、使用了哪些剪贴板格式；数据只保存在本机。");
    public static string FuzzyTitle => T("Fuzzy matching", "模糊匹配");
    public static string FuzzyNote => T("When nothing matches exactly, try an ordered letter match (gaps allowed).",
                                        "没有精确结果时，尝试按顺序但允许间隔的字母匹配。");

    // Item menu
    public static string Copy => T("Copy", "复制");
    public static string Paste => T("Paste", "粘贴");
    public static string PastePlain => T("Paste as plain text", "粘贴为纯文本");
    public static string Pin => T("Pin", "固定");
    public static string Unpin => T("Unpin", "取消固定");
    public static string Edit => T("Edit…", "编辑…");
    public static string EditImage => T("Edit in Paint", "用画图编辑");
    public static string Open => T("Open", "打开");
    public static string OpenLink => T("Open in browser", "在浏览器中打开");
    public static string QuickLook => T("Quick look", "快速查看");
    public static string ShowInFolder => T("Show in folder", "在文件夹中显示");
    public static string SaveAs => T("Save as…", "另存为…");
    public static string Remove => T("Delete", "删除");

    // Dialogs
    public static string EditTitle => T("Edit text", "编辑文字");
    public static string Save => T("Save", "保存");
    public static string SaveAndCopy => T("Save and copy", "保存并复制");
    public static string Cancel => T("Cancel", "取消");

    // Settings
    public static string SettingsTitle => T("Settings", "设置");
    public static string SettingsHeading => T("Settings", "设置");
    public static string SettingsSubtitle => T("Make 绳忆 work your way. Everything stays on this computer.", "让 绳忆 按你的习惯工作。所有内容都只保存在这台电脑上。");
    public static string SectionShortcut => T("Shortcut", "快捷键");
    public static string OpenClipboard => T("Open 绳忆", "打开 绳忆");
    public static string OpenClipboardNote => T("Search and pick with the keyboard", "用键盘搜索、挑选");
    public static string PressShortcut => T("Press keys…", "请按下快捷键…");
    public static string WinVTitle => T("Use Win+V", "用 Win+V 打开");
    public static string WinVNote => T("Replaces Windows clipboard history", "替代系统自带的剪贴板历史");
    public static string SectionHabits => T("Habits", "使用习惯");
    public static string ClickTitle => T("Click a card", "单击卡片");
    public static string ClickNote => T("Hold Shift for the other one", "按住 Shift 执行另一种");
    public static string ClickCopyShort => T("Copy", "仅复制");
    public static string ClickPasteShort => T("Paste", "直接粘贴");
    public static string EdgeTitle => T("Top-edge reveal", "顶边呼出");
    public static string EdgeNote => T("Show when the pointer rests at the top", "鼠标停在屏幕顶边时显示");
    public static string SectionHistory => T("History", "历史记录");
    public static string MaxItems => T("Keep at most", "最多保留");
    public static string MaxItemsNote => T("Pinned cards don't count", "固定的卡片不计入");
    public static string Retention => T("Delete after", "自动删除");
    public static string Never => T("Never", "永不");
    public static string Days => T("{0} d", "{0} 天");
    public static string SectionPrivacy => T("Privacy", "隐私");
    public static string Excluded => T("Ignore these apps", "忽略这些程序");
    public static string ExcludedNote => T("Copies from them are never recorded. One program name per line.", "从这些程序复制的内容不会被记录，每行一个程序名。");
    public static string StartWithWindows => T("Start with Windows", "开机启动");
    public static string StartupNote => T("Run after you sign in", "登录 Windows 后自动运行");

    // Appearance
    public static string SectionAppearance => T("Appearance", "外观");
    public static string ThemeTitle => T("Theme", "主题");
    public static string ThemeNote => T("Base palette for windows, pegs, colour chips and photos; also the rope when its gradient is off", "窗口、夹子、色值卡与图片卡的底层配色；绳子渐变关闭时也决定绳子基色");
    public static string[] ThemeNames => new[]
    {
        T("Warm", "暖纸"), T("Silver", "冷银"), T("Linen", "亚麻"), T("Porcelain", "瓷白"), T("Graphite", "石墨"),
    };
    public static string AccentTitle => T("Accent", "强调色");
    public static string AccentNote => T("Selection ring, link icons and switches", "选中环、链接图标与开关");
    public static string[] AccentNames => new[]
    {
        T("Blue", "蓝"), T("Terra", "陶土"), T("Mint", "薄荷"), T("Navy", "藏青"),
    };
    public static string CardColorTitle => T("Card colour", "卡片颜色");
    public static string CardColorNote => T("Applies to every card, colour-chip/photo frames and sticky paper", "作用于所有卡片、色值卡与图片卡外框、便签纸色");
    // Shown instead when a content-colour scheme is active: those five card kinds are then
    // painted by the scheme, so this setting only reaches what the scheme deliberately skips.
    public static string CardColorNoteTinted => T("Content colours are on: only colour chips, photos, sticky paper and windows still follow this", "已按类型配色：此设置现在只作用于色值卡、图片卡、便签纸色与窗口");
    public static string CardColorCustom => T("Custom (#RRGGBB)", "自定义（#RRGGBB）");
    public static string[] CardColorNames => new[]
    {
        T("Follow theme", "跟随主题"), T("Warm yellow", "暖黄"), T("Paper", "纸白"), T("Sky", "天蓝"),
        T("Mint", "薄荷"), T("Blush", "浅粉"), T("Graphite", "深灰"), T("Pure white", "纯白"),
    };
    // 0.7.0 · content-tinted cards + spliced rope
    public static string SectionTints => T("Content colours", "按类型配色");
    public static string TintSchemeTitle => T("Card palette", "卡片配色方案");
    public static string TintSchemeNote => T("Layered on the theme: tints text/link/code/file/note cards; windows, pegs, colour chips and photos still follow the theme", "叠在主题之上：只给文字 / 链接 / 代码 / 文件 / 便签上色；窗口、夹子、色值卡与图片卡仍由主题决定");
    public static string TintSchemeOff => T("Off (classic look)", "关闭（保持旧配色）");
    public static string TintRopeTitle => T("Rope gradient", "绳子拼接渐变");
    public static string TintRopeNote => T("Dye only the stretches where cards hang; bare rope stays hemp-coloured", "只染挂着卡片的绳段，空绳段保持麻绳本色");
    public static string TintModeTitle => T("Gradient style", "渐变方式");
    public static string TintModeSmooth => T("Smooth", "平滑过渡");
    public static string TintModeBanded => T("Banded", "硬边拼接");
    public static string TintStrengthTitle => T("Tint strength", "染色强度");
    public static string TintStrengthNote => T("How much of the card colour reaches the rope", "卡片颜色渗到绳子上的比例");
    public static string TintStickyTitle => T("Tint sticky notes", "便签也按类型着色");
    public static string TintStickyNote => T("Otherwise notes keep the paper colour you picked", "关闭时便签保留你选的纸色");
    public static string TintFineTitle => T("Split file colours", "文件按类别细分");
    public static string TintFineNote => T("Give each file category its own tint", "音视频、压缩包、文档等各用一种颜色");
    public static string TintStripTitle => T("Palette preview", "当前色板");
    public static string TintStripNote => T("Text / link / code / file / note · pasted colours are kept readable", "文字 / 链接 / 代码 / 文件 / 便签 · 粘贴内容自带颜色会自动校正对比度");
    public static string[] TintKindNames => new[]
    {
        T("Text", "文字"), T("Link", "链接"), T("Code", "代码"), T("File", "文件"), T("Note", "便签"),
    };

    public static string SectionGeometry => T("Line & cards", "绳子与卡片");
    public static string RopeSagTitle => T("Rope sag", "绳子弧度");
    public static string RopeSagNote => T("How far the rope dips in the middle; cards follow it", "绳子中部的下垂量，卡片会跟着弧线");
    public static string CardSizeTitle => T("Card size", "卡片大小");
    public static string CardSizeNote => T("Scales every card and its spacing", "等比缩放所有卡片与间距");
    public static string PreviewTitle => T("Preview", "预览");
    public static string PreviewNote => T("Changes are applied when you save", "保存后立即生效");

    // Frequency ordering
    public static string SectionRanking => T("Smart order", "智能排序");
    public static string FreqThresholdTitle => T("Rank after this many uses", "取用几次后提前");
    public static string FreqThresholdNote => T("0 turns it off. Cards that reach it move to the front, most used first.", "设为 0 关闭；达到次数的卡片自动前移，次数多的排更前");
    public static string FreqWindowTitle => T("Count uses within", "统计时间窗口");
    public static string FreqWindowNote => T("Only takes in the last N minutes; older ones are ignored. Type 1–129600 (up to 90 days), or use −/+ to step by magnitude.",
                                             "只统计最近 N 分钟内的取用，更早的不计入；可直接输入 1–129600 分钟（最长 90 天），或用 −/+ 按量级增减");
    public static string MinutesUnit => T(" min", " 分钟");
    public static string UsageNone => T("Not used yet", "尚未取用");
    // {0} is a human-readable window label (see WindowLabel), not a bare number.
    public static string UsageTip => T("Used {1} times in the last {0} · {2} total", "近 {0} 取用 {1} 次 · 累计 {2} 次");

    // Minutes are the stored unit, but a 10080-minute window reads better as "7 天".
    public static string WindowLabel(int minutes)
    {
        if (minutes >= 1440 && minutes % 1440 == 0)
            return string.Format(T("{0} days", "{0} 天"), minutes / 1440);
        if (minutes >= 60 && minutes % 60 == 0)
            return string.Format(T("{0} hours", "{0} 小时"), minutes / 60);
        return string.Format(T("{0} min", "{0} 分钟"), minutes);
    }
    public static string ResetUsage => T("Reset usage statistics…", "重置使用统计…");
    public static string ResetUsageConfirm => T("Clear usage counts for {0} cards? Your clips are kept.", "清除 {0} 张卡片的取用统计？剪贴板内容会保留。");

    // Sticky notes
    public static string NewSticky => T("New sticky note…", "新建便签…");
    public static string SectionSticky => T("Sticky notes", "便签");
    public static string NoteColorTitle => T("Default note colour", "默认便签颜色");
    public static string NoteColorNote => T("Notes stay until you delete them. Ctrl+Alt+N adds one anywhere; double-click a note to edit it.",
                                            "便签会一直保留到手动删除；任意时刻按 Ctrl+Alt+N 新建，双击便签编辑。");
    public static string[] NoteColorNames => new[]
    {
        T("Follow theme", "跟随主题"), T("Lemon", "柠檬黄"), T("Peach", "蜜桃"), T("Sky", "天蓝"),
        T("Mint", "薄荷"), T("Lavender", "薰衣草"), T("Paper", "纸白"), T("Graphite", "石墨"),
    };
    public static string StickyTitle => T("Sticky note", "便签");
    public static string StickyEmpty => T("Double-click to write something…", "双击写点什么…");
    public static string StickyTag => T("NOTE", "便签");
    public static string StickyTip => T("Double-click to edit · right-click to change colour", "双击编辑 · 右键更换颜色");
    public static string EditSticky => T("Edit note…", "编辑便签…");
    public static string NoteColorMenu => T("Note colour", "便签颜色");
    public static string DeleteSticky => T("Delete note", "删除便签");
    public static string DeleteStickyConfirm => T("Delete this sticky note? It will not come back.", "删除这张便签？此操作不可撤销。");
    public static string FilterSticky => T("Notes", "便签");
    public static string NewStickyKey => T(" · Ctrl+N note", " · Ctrl+N 便签");

    // Smart tags (v0.5.0): all recognition is local, no data leaves the machine.
    public static string FilterTags => T("Tagged", "已识别");
    public static string FilterTagRow => T("Tags", "标签");
    public static string FilterNoTags => T("No recognised tags yet", "暂无已识别标签");
    public static string TagsTip => T("Tags: {0}", "标签：{0}");
    public static string TagMore => T("+{0}", "+{0}");
    public static string SmartTagsTitle => T("Smart tags", "智能标签");
    public static string SmartTagsNote => T("Spot phone numbers, emails, waybills, ID/card numbers, addresses, dates, amounts and order numbers with local rules. Nothing is uploaded.",
                                            "用本地规则识别手机号、邮箱、快递单、身份证、银行卡、地址、日期、金额和订单号；不联网、不上传任何内容。");
    public static string QuickLookFrom => T("From {0}", "来自 {0}");
    public static string QuickLookTags => T("Tags {0}", "标签 {0}");

    // File categories (v0.6.0): a file card knows whether it holds media, archives, documents...
    public static string FilterCategoryRow => T("Files", "文件类别");
    public static string FilterNoCategories => T("No file categories yet", "暂无文件类别");

    // P2 · paste modes (v0.6.0)
    public static string SectionPasteModes => T("Paste modes", "粘贴方式");
    public static string MergeNumberTitle => T("Number merged clips", "合并时自动编号");
    public static string MergeNumberNote => T("Prefix each picked card with 1. 2. 3. …", "给选中的每张卡片加上 1. 2. 3. … 前缀");
    public static string QuickInsertTitle => T("Quick insert shortcut", "快速插入快捷键");
    public static string QuickInsertNote => T("Type the latest text (or the first search hit) straight into the focused window without touching the clipboard. Off by default.",
                                              "把最近一条文字（或搜索首条命中）直接打进当前窗口，完全不动剪贴板。默认关闭。");
    public static string QuickInsertChordTitle => T("Insert chord", "插入组合键");
    public static string QuickInsertCopyTitle => T("Copy instead of typing", "改为仅复制");
    public static string QuickInsertCopyNote => T("Use when Unicode typing is unreliable (terminals, remote desktops).", "终端 / 远程桌面里字符注入不可靠时改用这一项。");
    public static string SequenceHotkeyTitle => T("Next-clip shortcut (paste sequence)", "逐条粘贴·下一条快捷键");
    public static string SequenceHotkeyNote => T("Only ADVANCES an already running manual sequence — it never starts one. Start a queue from a card's right-click menu. Off by default.",
                                                 "只推进已经启动的手动队列，不能用来启动队列；队列由卡片右键菜单创建。默认关闭。");
    public static string SequenceChordTitle => T("Next-clip chord", "下一条组合键");
    public static string SequenceIntervalTitle => T("Auto interval", "自动间隔");
    public static string SequenceIntervalNote => T("Delay between clips when pasting a sequence automatically.", "自动逐条粘贴时，每条之间的间隔。");
    public static string SequenceCopyTitle => T("Copy only in sequences", "逐条粘贴仅复制");
    public static string SequenceCopyNote => T("Never simulate Ctrl+V; just put each clip on the clipboard.", "不模拟 Ctrl+V，只把每条内容依次放到剪贴板上。");
    public static string HotkeyChordNote => T("Write a chord, e.g. Ctrl+3 or Ctrl+Alt+Shift+V", "填写组合键，如 Ctrl+3、Ctrl+Alt+Shift+V");
    public static string HotkeyChordInvalid => T("Use at least one modifier plus a key, e.g. Ctrl+3", "至少需要一个修饰键加一个按键，例如 Ctrl+3");
    public static string HotkeyChordAvailable => T("Available", "可用");
    public static string HotkeyChordChecking => T("Checking availability…", "检测占用中…");
    public static string HotkeyChordTaken => T("Already used by another app or Windows. Pick a different chord.", "已被其他程序或系统占用，请换一个组合键");
    public static string HotkeyChordDupMain => T("Same as the shortcut that opens the line.", "与「打开晾衣绳」的快捷键重复");
    public static string HotkeyChordDupNote => T("Same as New sticky note (Ctrl+Alt+N).", "与「新建便签」快捷键（Ctrl+Alt+N）重复");
    public static string HotkeyChordDupAux => T("Same as the other paste-mode shortcut.", "与另一个粘贴方式快捷键重复");
    public static string HotkeyChordWinWarn => T("Contains Win: Windows may intercept it, so it might not fire.", "含 Win 键：系统可能截获，按下不一定生效");
    public static string HotkeyChordImeWarn => T("Alt+Shift often switches input languages and may be swallowed by the IME.", "Alt+Shift 常用于切换输入语言，可能被输入法截获");
    public static string HotkeyChordDisabled => T("Turn the switch on to check this chord.", "打开上方开关后才检查此组合键");
    public static string HotkeyChordNoProbe => T("Availability is checked when saving.", "保存时检查是否可用");
    public static string HotkeyChordSaveBlocked => T("A paste-mode shortcut conflicts. Fix the highlighted chords before saving.", "有粘贴方式快捷键冲突，请先修改标红的组合键再保存");
    public static string HotkeyChordSaveBlockedTitle => T("Shortcut conflict", "快捷键冲突");
    public static string MsUnit => T(" ms", " 毫秒");
    public static string HotkeyFailedMain => T("Open line ({0})", "打开晾衣绳（{0}）");
    public static string HotkeyFailedNote => T("New sticky note ({0})", "新建便签（{0}）");
    public static string HotkeyFailedNext => T("Next clip ({0})", "逐条粘贴下一条（{0}）");
    public static string HotkeyFailedInsert => T("Quick insert ({0})", "快速插入（{0}）");
    public static string HotkeyFailedDetail => T("These shortcuts could not be registered and will not fire:\n{0}\n\nThe chords are already used by another app or Windows. Pick different ones in Settings → Paste modes.",
                                                 "以下快捷键注册失败，按下去不会生效：\n{0}\n\n组合键已被其他程序或系统占用，请在 设置 → 粘贴方式 里换一个。");
    public static string SequenceNeedHotkey => T("Enable the 'Next-clip shortcut' in Settings before using manual paste sequence.", "使用手动逐条粘贴前，请先在设置里开启「下一条快捷键」。");

    // Item menu, right-click with a Ctrl+Click selection
    public static string QuickInsertThis => T("Quick insert this (clipboard untouched)", "快速插入这一条（不动剪贴板）");
    public static string MergeCopyMany => T("Copy {0} clips merged", "合并复制这 {0} 条");
    public static string MergePasteMany => T("Merge & paste {0} clips", "合并并粘贴这 {0} 条");
    public static string SequenceManualMany => T("Paste {0} clips one by one…", "逐条粘贴这 {0} 条…");
    public static string SequenceAutoMany => T("Auto-paste {0} clips…", "自动逐条粘贴这 {0} 条…");
    public static string MultiClear => T("Clear selection", "清除多选");
    public static string MergeNeedTwo => T("Ctrl+Click at least two cards to merge.", "请按住 Ctrl 单击选中至少两张卡片再合并。");
    public static string MergeNoText => T("None of the selected cards has text to merge.", "选中的卡片没有可合并的文字。");
    public static string MergeCopied => T("Merged {0} clips into the clipboard ({1} skipped).", "已把 {0} 条合并到剪贴板（跳过 {1} 条）。");
    public static string SequenceStartedAuto => T("Auto-pasting {0} clips every {1} ms (Esc stops)", "自动逐条粘贴：{0} 条 · 每 {1} 毫秒一条（Esc 中止）");
    public static string SequenceStartedManual => T("Sequence: {0} clips · first one sent · press {1} for the next (Esc stops)", "逐条粘贴：{0} 条 · 第 1 条已贴出 · 按 {1} 贴下一条（Esc 中止）");
    public static string SequenceStopped => T("Paste sequence stopped.", "逐条粘贴已中止。");
    public static string SequenceFinished => T("Paste sequence finished.", "逐条粘贴已完成。");
    public static string SequenceNoQueue => T("No paste sequence is running. This chord only sends the NEXT clip: Ctrl+Click at least two cards, then right-click 'Paste N clips one by one…' to start a queue.",
                                               "当前没有逐条粘贴队列。这个组合键只负责贴下一条：请先按住 Ctrl 单击选中至少两张卡片，再右键「逐条粘贴这 N 条…」启动队列。");
    public static string QuickInsertNoTarget => T("Open 绳忆 from the window you want to type into, then try quick insert again.", "请先从要输入的窗口里呼出 绳忆，再使用快速插入。");
    public static string QuickInsertNothing => T("No text clip to insert yet.", "还没有可以插入的文字内容。");
    public static string QuickInsertCopied => T("Copied (quick insert is set to copy only).", "已复制（快速插入当前设为仅复制）。");

    // 0.10.0 · diagnostics
    public static string SectionDiagnostics => T("Diagnostics", "诊断日志");
    public static string DiagEnabledTitle => T("Diagnostic log", "诊断日志");
    public static string DiagEnabledNote => T("Records errors and key events locally for troubleshooting.", "在本机记录错误与关键事件，用于排查问题。");
    public static string DiagLevelTitle => T("Level", "记录级别");
    public static string DiagLevelNote => T("Verbose adds high-frequency details; use it while reproducing a problem.", "详细模式包含高频细节，建议只在复现问题时开启。");
    public static string DiagLevelErrors => T("Errors only", "仅错误");
    public static string DiagLevelStandard => T("Errors + key events", "错误 + 关键事件");
    public static string DiagLevelVerbose => T("Verbose", "详细");
    public static string DiagClearTitle => T("Clear log", "清空日志");
    public static string DiagPrivacyNote => T("No clipboard text, images or full paths are stored. Export from the tray menu; nothing is uploaded.",
                                              "不记录剪贴板正文、图片或完整路径。可在托盘菜单导出，不会自动上传。");
    public static string DiagClear => T("Clear", "清空");
    public static string DiagCleared => T("Diagnostic log cleared.", "诊断日志已清空。");
    public static string DiagExportMenu => T("Export diagnostics…", "导出诊断包…");
    public static string DiagExportTitle => T("Save diagnostic bundle", "保存诊断包");
    public static string DiagExportDone => T("Diagnostic bundle saved to {0}", "诊断包已保存到 {0}");
}
