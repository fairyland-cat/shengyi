using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shengyi;

static class About
{
    public const string Author = "shengyi";
    public const string GitHub = "https://github.com/fairyland-cat";
    public const string Repo = "https://github.com/fairyland-cat/shengyi";
    public static string Version => typeof(About).Assembly.GetName().Version?.ToString(3) ?? "";

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

// Edit a text clip before using it again.
sealed class TextEditWindow : ChromeWindow
{
    public TextEditWindow(ClipItem item, ClipStore store, Actions actions) : base(S.EditTitle, 760, 540)
    {
        bool code = item.Kind == ClipKind.Code;
        var box = new TextBox
        {
            Text = item.Text ?? "",
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = code ? TextWrapping.NoWrap : TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var field = Fields.Wrap(box);
        box.FontSize = 14;
        if (code) box.FontFamily = new FontFamily("Consolas");
        field.Padding = new Thickness(14, 12, 14, 12);
        field.Margin = new Thickness(30, 0, 30, 0);

        void SaveText() => store.UpdateText(item, box.Text);
        var cancel = new Pill(S.Cancel, false);
        var saveCopy = new Pill(S.SaveAndCopy, false) { Margin = new Thickness(6, 0, 6, 0) };
        var save = new Pill(S.Save, true) { MinWidth = 120 };
        cancel.Click += Close;
        saveCopy.Click += () => { SaveText(); actions.Copy(item, plain: false); Close(); };
        save.Click += () => { SaveText(); Close(); };

        var count = Theme.Text("", 11, Theme.Faint);
        void Count() => count.Text = string.Format(S.Chars, box.Text.Length);
        box.TextChanged += (_, _) => Count();
        Count();

        Body.RowDefinitions.Add(new RowDefinition());
        Body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });
        Body.Children.Add(field);
        var footer = Footer(count, cancel, saveCopy, save);
        Grid.SetRow(footer, 1);
        Body.Children.Add(footer);

        Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; SaveText(); Close(); }
        };
    }

    internal static Border Footer(UIElement left, params UIElement[] buttons)
    {
        var g = new Grid { Margin = new Thickness(30, 0, 30, 0) };
        if (left is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in buttons) right.Children.Add(b);
        g.Children.Add(right);
        return new Border { BorderBrush = Theme.B(Theme.Line), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 14, 0, 0), Child = g };
    }
}

// Create or edit a sticky note. A new note is only written to the store when it is saved, so
// cancelling never litters the line with empty cards.
sealed class StickyEditWindow : ChromeWindow
{
    readonly ClipItem item;
    readonly ClipStore store;
    readonly Actions actions;
    readonly bool isNew;
    readonly TextBox box;
    readonly Border field;
    string color;

    public StickyEditWindow(ClipItem item, ClipStore store, Actions actions, bool isNew)
        : base(S.StickyTitle, 620, 470)
    {
        this.item = item;
        this.store = store;
        this.actions = actions;
        this.isNew = isNew;
        color = item.NoteColor ?? "auto";

        box = new TextBox
        {
            Text = item.Text ?? "",
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 15,
            Padding = new Thickness(2),
        };
        field = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 16, 14),
            Child = box,
        };

        int index = Math.Max(0, Array.IndexOf(Theme.NotePresets, color));
        var swatches = new Swatches(Theme.NotePresets.Select(Theme.ParseHex).ToArray(), index, i => S.NoteColorNames[i]);
        swatches.Margin = new Thickness(0, 8, 0, 12);
        void Paint()
        {
            var n = Theme.NotePalette(color);
            field.Background = Theme.B(Theme.Mix(n.top, n.bottom, 0.5));
            field.BorderBrush = Theme.B(n.border);
            box.Foreground = Theme.B(n.ink);
            box.CaretBrush = Theme.B(n.ink);
        }
        swatches.Changed += i => { color = Theme.NotePresets[i]; Paint(); };
        Paint();

        var head = new StackPanel();
        head.Children.Add(Theme.Text(S.NoteColorTitle, 12, Theme.Muted));
        head.Children.Add(swatches);
        DockPanel.SetDock(head, Dock.Top);
        var panel = new DockPanel { Margin = new Thickness(30, 0, 30, 0) };
        panel.Children.Add(head);
        panel.Children.Add(field);

        var count = Theme.Text("", 11, Theme.Faint);
        void Count() => count.Text = string.Format(S.Chars, box.Text.Length);
        box.TextChanged += (_, _) => Count();
        Count();

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(count);
        if (!isNew)
        {
            var delete = new Pill(S.DeleteSticky, false) { Margin = new Thickness(16, 0, 0, 0) };
            delete.Click += () =>
            {
                if (MessageBox.Show(S.DeleteStickyConfirm, S.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                store.Remove(item);
                Close();
            };
            left.Children.Add(delete);
        }

        var cancel = new Pill(S.Cancel, false);
        var saveCopy = new Pill(S.SaveAndCopy, false) { Margin = new Thickness(6, 0, 6, 0) };
        var save = new Pill(S.Save, true) { MinWidth = 120 };
        cancel.Click += Close;
        void Save()
        {
            item.Text = box.Text;
            item.NoteColor = color;
            if (isNew) store.AddSticky(item);
            else store.UpdateSticky(item, box.Text, color);
        }
        saveCopy.Click += () => { Save(); actions.Copy(item, plain: false); Close(); };
        save.Click += () => { Save(); Close(); };

        Body.RowDefinitions.Add(new RowDefinition());
        Body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });
        Body.Children.Add(panel);
        var footer = TextEditWindow.Footer(left, cancel, saveCopy, save);
        Grid.SetRow(footer, 1);
        Body.Children.Add(footer);

        Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Save(); Close(); }
        };
    }
}

// Shortcut, Win+V, click behaviour, history limits, ignored apps, appearance, startup — and who made it.
sealed class SettingsWindow : ChromeWindow
{
    static readonly int[] RetentionChoices = { 0, 1, 7, 30, 90 };

    public Settings Result { get; }
    public bool StartWithWindows { get; private set; }

    readonly Action suspendHotkey, resumeHotkey;
    readonly Border hotkeyBox;
    bool recording;

    public SettingsWindow(Settings current, bool startWithWindows, Action suspendHotkey, Action resumeHotkey)
        : base(S.SettingsTitle, 800, 1000)
    {
        Result = current.Clone();
        StartWithWindows = startWithWindows;
        this.suspendHotkey = suspendHotkey;
        this.resumeHotkey = resumeHotkey;

        // Shortcut recorder: click it, press the new combination. Esc cancels.
        hotkeyBox = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = Theme.B(Theme.Surface),
            BorderBrush = Theme.B(Theme.Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 5, 1, 5),
            MinWidth = 120,
            MinHeight = 36,
            Cursor = Cursors.Hand,
        };
        hotkeyBox.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!recording) StartRecording(); };
        ShowHotkey();

        var winV = new Toggle(Result.ReplaceWinV);
        winV.Changed += () => Result.ReplaceWinV = winV.IsOn;
        var click = new Segmented(Result.PasteOnClick ? 1 : 0, S.ClickCopyShort, S.ClickPasteShort);
        click.Changed += () => Result.PasteOnClick = click.Selected == 1;
        var edge = new Toggle(Result.RevealAtTopEdge);
        edge.Changed += () => Result.RevealAtTopEdge = edge.IsOn;
        var recordSource = new Toggle(Result.RecordSourceApp);
        recordSource.Changed += () => Result.RecordSourceApp = recordSource.IsOn;
        var fuzzy = new Toggle(Result.FuzzySearch);
        fuzzy.Changed += () => Result.FuzzySearch = fuzzy.IsOn;
        var startup = new Toggle(startWithWindows);
        startup.Changed += () => StartWithWindows = startup.IsOn;
        var max = Fields.Stepper(Result.MaxItems, 50, 2000, 50, v => Result.MaxItems = v);
        var retention = new Segmented(Math.Max(0, Array.IndexOf(RetentionChoices, Result.RetentionDays)),
            RetentionChoices.Select(d => d == 0 ? S.Never : string.Format(S.Days, d)).ToArray()) { HorizontalAlignment = HorizontalAlignment.Stretch };
        retention.Changed += () => Result.RetentionDays = RetentionChoices[retention.Selected];
        var excluded = new TextBox
        {
            Text = string.Join(Environment.NewLine, Result.ExcludedApps),
            AcceptsReturn = true,
            Height = 74,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        excluded.TextChanged += (_, _) =>
            Result.ExcludedApps = excluded.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        // ---- Appearance ----------------------------------------------------
        var preview = new AppearancePreview { HorizontalAlignment = HorizontalAlignment.Stretch };
        void Refresh() => preview.Update(Result);

        int themeIndex = Math.Max(0, Array.IndexOf(Theme.Names, Result.ThemeName));
        var themePicker = new Segmented(themeIndex, S.ThemeNames);
        themePicker.Changed += () => { Result.ThemeName = Theme.Names[themePicker.Selected]; Refresh(); };

        int accentIndex = Math.Max(0, Array.IndexOf(Theme.AccentNames, Result.Accent));
        var accentSwatches = new Swatches(
            Theme.AccentNames.Select(n => (Color?)Theme.AccentColor(n)).ToArray(),
            accentIndex, i => S.AccentNames[i]);
        accentSwatches.Changed += i => { Result.Accent = Theme.AccentNames[i]; Refresh(); };

        int cardIndex = Array.IndexOf(Theme.CardPresets, Result.CardColor);
        var customHex = new TextBox { Width = 96, Text = cardIndex < 0 ? Result.CardColor : "", ToolTip = S.CardColorCustom };
        var cardSwatches = new Swatches(
            Theme.CardPresets.Select(Theme.ParseHex).ToArray(),
            0, i => S.CardColorNames[i]);
        cardSwatches.Selected = Math.Max(0, cardIndex);
        if (cardIndex < 0) cardSwatches.ClearSelection();
        cardSwatches.Changed += i => { Result.CardColor = Theme.CardPresets[i]; customHex.Text = ""; Refresh(); };
        customHex.TextChanged += (_, _) =>
        {
            if (Theme.ParseHex(customHex.Text) is not { } c) return;
            Result.CardColor = Theme.ToHex(c);
            cardSwatches.ClearSelection();
            Refresh();
        };
        var hexField = Fields.Wrap(customHex);

        var sag = Fields.Stepper(Math.Clamp(Result.RopeSag, 0, 48), 0, 48, 4, v => { Result.RopeSag = v; Refresh(); });
        var size = Fields.Stepper(Math.Clamp(Result.CardSizePercent, 70, 150), 70, 150, 10,
            v => { Result.CardSizePercent = v; Refresh(); }, "%");
        var freqThreshold = Fields.Stepper(Math.Clamp(Result.FreqThreshold, 0, 20), 0, 20, 1,
            v => Result.FreqThreshold = v);
        var freqWindow = Fields.Stepper(Math.Clamp(Result.FreqWindowDays, 1, 90), 1, 90, 1,
            v => Result.FreqWindowDays = v, S.DaysUnit);
        int noteIndex = Math.Max(0, Array.IndexOf(Theme.NotePresets, Result.NoteColor));
        var noteSwatches = new Swatches(Theme.NotePresets.Select(Theme.ParseHex).ToArray(),
            noteIndex, i => S.NoteColorNames[i]);
        noteSwatches.Changed += i => Result.NoteColor = Theme.NotePresets[i];
        Refresh();

        var left = new StackPanel();
        left.Children.Add(Card(S.SectionShortcut,
            Row(S.OpenClipboard, S.OpenClipboardNote, hotkeyBox),
            Row(S.WinVTitle, S.WinVNote, winV)));
        left.Children.Add(Spaced(Card(S.SectionHabits,
            Row(S.ClickTitle, S.ClickNote, click),
            Row(S.EdgeTitle, S.EdgeNote, edge),
            Row(S.StartWithWindows, S.StartupNote, startup))));
        left.Children.Add(Spaced(Card(S.SectionAppearance,
            Stacked(S.ThemeTitle, S.ThemeNote, themePicker),
            Stacked(S.AccentTitle, S.AccentNote, accentSwatches),
            Stacked(S.CardColorTitle, S.CardColorNote, cardSwatches),
            Row(S.CardColorCustom, null, hexField))));

        var right = new StackPanel();
        right.Children.Add(Card(S.SectionSearch,
            Row(S.RecordSourceTitle, S.RecordSourceNote, recordSource),
            Row(S.FuzzyTitle, S.FuzzyNote, fuzzy)));
        right.Children.Add(Spaced(Card(S.SectionRanking,
            Row(S.FreqThresholdTitle, S.FreqThresholdNote, freqThreshold),
            Row(S.FreqWindowTitle, S.FreqWindowNote, freqWindow))));
        right.Children.Add(Spaced(Card(S.SectionHistory,
            Row(S.MaxItems, S.MaxItemsNote, max),
            Stacked(S.Retention, null, retention))));
        right.Children.Add(Spaced(Card(S.SectionPrivacy,
            Stacked(S.Excluded, S.ExcludedNote, Fields.Wrap(excluded)))));
        right.Children.Add(Spaced(Card(S.SectionGeometry,
            Row(S.RopeSagTitle, S.RopeSagNote, sag),
            Row(S.CardSizeTitle, S.CardSizeNote, size),
            Stacked(S.PreviewTitle, S.PreviewNote, preview))));
        right.Children.Add(Spaced(Card(S.SectionSticky,
            Stacked(S.NoteColorTitle, S.NoteColorNote, noteSwatches))));

        var columns = new Grid { Margin = new Thickness(36, 0, 36, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.Children.Add(left);
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = columns,
        };

        var header = new StackPanel { Margin = new Thickness(36, 0, 36, 20) };
        header.Children.Add(Theme.Text("PREFERENCES", 10, Theme.Muted, bold: true));
        var h = Theme.Text(S.SettingsHeading, 26, Theme.Ink, bold: true);
        h.Margin = new Thickness(0, 6, 0, 0);
        header.Children.Add(h);
        var sub = Theme.Text(S.SettingsSubtitle, 12, Theme.Muted);
        sub.Margin = new Thickness(0, 8, 0, 0);
        header.Children.Add(sub);

        var cancel = new Pill(S.Cancel, false) { Margin = new Thickness(0, 0, 8, 0) };
        var save = new Pill(S.Save, true) { MinWidth = 132 };
        cancel.Click += Close;
        save.Click += () => DialogResult = true;

        Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Body.RowDefinitions.Add(new RowDefinition());
        Body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
        Body.Children.Add(header);
        Grid.SetRow(scroller, 1);
        Body.Children.Add(scroller);
        var footer = TextEditWindow.Footer(AuthorBadge(), cancel, save);
        Grid.SetRow(footer, 2);
        Body.Children.Add(footer);

        PreviewKeyDown += OnKey;
        Closed += (_, _) => { if (recording) resumeHotkey(); };
    }

    static UIElement Spaced(UIElement e)
    {
        if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 16, 0, 0);
        return e;
    }

    // Avatar, name, GitHub link and version.
    static UIElement AuthorBadge()
    {
        var avatar = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Theme.B(Theme.P.Accent),
            Child = new TextBlock { Text = "S", FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(Theme.Text(About.Author, 12.5, Theme.Ink, bold: true));
        var version = Theme.Text($"绳忆 v{About.Version}", 11, Theme.Faint);
        version.Margin = new Thickness(8, 1, 0, 0);
        name.Children.Add(version);
        var link = new TextBlock
        {
            Text = "github.com/fairyland-cat/shengyi  ↗",
            FontSize = 11.5,
            Foreground = Theme.B(Theme.Body),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 3, 0, 0),
            ToolTip = About.GitHub,
        };
        link.MouseEnter += (_, _) => { link.TextDecorations = TextDecorations.Underline; link.Foreground = Theme.B(Theme.Ink); };
        link.MouseLeave += (_, _) => { link.TextDecorations = null; link.Foreground = Theme.B(Theme.Body); };
        link.MouseLeftButtonUp += (_, e) => { e.Handled = true; About.Open(About.GitHub); };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(name);
        text.Children.Add(link);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(avatar);
        row.Children.Add(text);
        return row;
    }

    void ShowHotkey(string? partial = null)
    {
        hotkeyBox.BorderBrush = Theme.B(recording ? Theme.Focus : Theme.Line);
        hotkeyBox.Background = recording ? Theme.B(Theme.P.Window) : Theme.B(Theme.Surface);
        if (recording && partial == null)
        {
            var t = Theme.Text(S.PressShortcut, 12, Theme.Muted);
            t.Margin = new Thickness(6, 0, 6, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            hotkeyBox.Child = t;
        }
        else hotkeyBox.Child = Fields.Keycaps(partial ?? HotkeyText.Describe(Result.HotkeyModifiers, Result.HotkeyKey), recording);
    }

    void StartRecording()
    {
        recording = true;
        suspendHotkey(); // otherwise the current shortcut can't be pressed here
        ShowHotkey();
        Keyboard.Focus(this);
    }

    void StopRecording()
    {
        recording = false;
        resumeHotkey();
        ShowHotkey();
    }

    protected override void Cancel()
    {
        if (recording) StopRecording();
        else Close();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (!recording || e.Key == Key.Escape) return;
        e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = (uint)Keyboard.Modifiers;
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            ShowHotkey(HotkeyText.Modifiers(mods) + "…");
            return;
        }
        if (mods == 0) return;
        Result.HotkeyModifiers = mods;
        Result.HotkeyKey = (uint)KeyInterop.VirtualKeyFromKey(k);
        StopRecording();
    }
}
