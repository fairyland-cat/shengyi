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
    public static string FilterHint => T("Pinyin works · try app:chrome, tag:phone / tag:手机号, kind:image, in:7d",
                                         "支持拼音 · 可用 app:chrome、tag:phone / tag:手机号、kind:图片、in:7d");
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
    public static string ThemeNote => T("Palette for the rope, pegs, cards and windows", "绳子、夹子、卡片与窗口的配色");
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
    public static string CardColorNote => T("Applies to notes, links and file cards", "作用于便签、链接和文件卡片");
    public static string CardColorCustom => T("Custom (#RRGGBB)", "自定义（#RRGGBB）");
    public static string[] CardColorNames => new[]
    {
        T("Follow theme", "跟随主题"), T("Warm yellow", "暖黄"), T("Paper", "纸白"), T("Sky", "天蓝"),
        T("Mint", "薄荷"), T("Blush", "浅粉"), T("Graphite", "深灰"), T("Pure white", "纯白"),
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
    public static string FreqWindowNote => T("Only takes in the last N days count; older ones are ignored.", "只统计最近 N 天内的取用，更早的不计入");
    public static string DaysUnit => T(" d", " 天");
    public static string UsageNone => T("Not used yet", "尚未取用");
    public static string UsageTip => T("Used {1} times in the last {0} days · {2} total", "近 {0} 天取用 {1} 次 · 累计 {2} 次");
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
}
