using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shengyi;

// A small toggle used in the advanced filter drawer. Colour follows the live theme, just like
// the filter tabs above it.
sealed class FilterChip : Border
{
    readonly TextBlock label;
    public object? Value { get; }
    public bool IsOn { get; private set; }
    public event Action? Changed;

    public FilterChip(string text, object? value)
    {
        Value = value;
        CornerRadius = new CornerRadius(9);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(9, 3, 9, 3);
        Margin = new Thickness(0, 0, 6, 4);
        Cursor = Cursors.Hand;
        label = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Child = label;
        Repaint();
        MouseLeftButtonUp += (_, e) => { e.Handled = true; SetOn(!IsOn); Changed?.Invoke(); };
    }

    public void SetOn(bool value)
    {
        if (IsOn == value) return;
        IsOn = value;
        Repaint();
    }

    public void Repaint()
    {
        Background = IsOn ? Theme.B(Theme.P.Accent) : Theme.B(Theme.WithAlpha(Theme.P.PanelInk, 0x16));
        BorderBrush = Theme.B(Theme.WithAlpha(Theme.P.PanelInk, IsOn ? (byte)0x00 : (byte)0x30));
        label.Foreground = IsOn ? Brushes.White : Theme.B(Theme.P.PanelInk);
        label.FontWeight = IsOn ? FontWeights.SemiBold : FontWeights.Normal;
    }
}

// The drawer below the keyboard-mode bar: source apps, content types and a time range. It only
// collects state — LineWindow turns it into a SearchQuery and the store does the actual lookup.
sealed class SearchFilterPanel : Border
{
    public const double PanelHeight = 200;   // five chip rows (source / tags / type / file category / time) + hint

    static readonly ClipKind[] KindOrder =
    {
        ClipKind.Text, ClipKind.Code, ClipKind.Link, ClipKind.Color,
        ClipKind.Image, ClipKind.Files, ClipKind.Sticky,
    };

    public event Action? Changed;

    readonly WrapPanel appRow = new(), tagRow = new(), kindRow = new(), catRow = new();
    readonly StackPanel timeChipsRow = new() { Orientation = Orientation.Horizontal };
    readonly List<FilterChip> appChips = new(), tagChips = new(), kindChips = new(), catChips = new(), timeChips = new();
    readonly List<TextBlock> texts = new();
    readonly HashSet<string> selectedApps = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> selectedTags = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> selectedCategories = new(StringComparer.OrdinalIgnoreCase);
    readonly TextBlock countText, hintText, clear;
    readonly TextBlock appEmpty, tagEmpty, catEmpty;
    int withinDays;

    public IEnumerable<string> SelectedApps => selectedApps;
    public IEnumerable<string> SelectedTags => selectedTags;
    public IEnumerable<string> SelectedCategories => selectedCategories;
    public IEnumerable<ClipKind> SelectedKinds => kindChips.Where(c => c.IsOn).Select(c => (ClipKind)c.Value!);
    public int WithinDays => withinDays;
    public bool HasAny => selectedApps.Count > 0 || selectedTags.Count > 0 || selectedCategories.Count > 0
        || kindChips.Any(c => c.IsOn) || withinDays > 0;

    public SearchFilterPanel()
    {
        Width = 760;
        Height = PanelHeight;
        CornerRadius = new CornerRadius(14);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(14, 10, 14, 8);

        // Type chips are fixed; source chips are rebuilt from the store whenever the drawer opens.
        for (int i = 0; i < KindOrder.Length; i++)
        {
            var chip = new FilterChip(S.FilterKindNames[i], KindOrder[i]);
            chip.Changed += Fire;
            kindChips.Add(chip);
            kindRow.Children.Add(chip);
        }

        foreach (var (name, days) in new[]
        {
            (S.FilterAny, 0), (S.FilterToday, 1), (S.FilterDays7, 7), (S.FilterDays30, 30),
        })
        {
            var chip = new FilterChip(name, days);
            chip.Changed += () =>
            {
                if (!chip.IsOn && timeChips.All(c => !c.IsOn)) chip.SetOn(true); // keep one selected
                withinDays = chip.IsOn ? days : 0;
                foreach (var other in timeChips) if (other != chip) other.SetOn(false);
                Fire();
            };
            timeChips.Add(chip);
            timeChipsRow.Children.Add(chip);
        }
        timeChips[0].SetOn(true);

        appEmpty = new TextBlock { Text = S.FilterNoApps, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        tagEmpty = new TextBlock { Text = S.FilterNoTags, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        catEmpty = new TextBlock { Text = S.FilterNoCategories, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        countText = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4) };
        clear = new TextBlock
        {
            Text = S.FilterClear, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 2, 4),
        };
        clear.MouseLeftButtonUp += (_, e) => { e.Handled = true; Reset(); Changed?.Invoke(); };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(countText);
        right.Children.Add(clear);

        var timeDock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        timeDock.Children.Add(right);
        timeDock.Children.Add(timeChipsRow);

        hintText = new TextBlock { Text = S.FilterHint, FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };

        catEmpty.Visibility = Visibility.Collapsed;
        catRow.Children.Add(catEmpty);

        var stack = new StackPanel();
        stack.Children.Add(Row(S.FilterSource, appRow));
        stack.Children.Add(Row(S.FilterTagRow, tagRow));
        stack.Children.Add(Row(S.FilterKind, kindRow));
        stack.Children.Add(Row(S.FilterCategoryRow, catRow));
        stack.Children.Add(Row(S.FilterTime, timeDock));
        stack.Children.Add(hintText);
        Child = stack;
        Restyle();
    }

    UIElement Row(string title, UIElement content)
    {
        var label = new TextBlock
        {
            Text = title, FontSize = 11, Width = 42, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 6, 0),
        };
        texts.Add(label);
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(label, Dock.Left);
        dock.Children.Add(label);
        dock.Children.Add(content);
        return dock;
    }

    // Apps are passed in already sorted by how often they appear. Selections survive a rebuild.
    public void SetApps(IEnumerable<string> apps)
    {
        appRow.Children.Clear();
        appChips.Clear();
        var list = apps.Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
        if (list.Count == 0)
        {
            appRow.Children.Add(appEmpty);
            return;
        }
        foreach (var app in list)
        {
            var chip = new FilterChip(app, app);
            chip.SetOn(selectedApps.Contains(app));
            chip.Changed += () =>
            {
                if (chip.IsOn) selectedApps.Add(app); else selectedApps.Remove(app);
                Fire();
            };
            appChips.Add(chip);
            appRow.Children.Add(chip);
        }
        Restyle();
    }

    // Only tags that actually occur get a chip. Canonical ids come from SmartTags; the label is
    // localised, so the chip reads 手机号 while tag:phone keeps working in the search box.
    public void SetTags(IEnumerable<string> tags)
    {
        tagRow.Children.Clear();
        tagChips.Clear();
        var present = new HashSet<string>(tags.Where(SmartTags.IsKnown), StringComparer.OrdinalIgnoreCase);
        var list = SmartTags.All.Where(present.Contains).Take(9).ToList();
        if (list.Count == 0)
        {
            tagRow.Children.Add(tagEmpty);
            return;
        }
        foreach (var tag in list)
        {
            var chip = new FilterChip(SmartTags.Name(tag), tag);
            chip.SetOn(selectedTags.Contains(tag));
            chip.Changed += () =>
            {
                if (chip.IsOn) selectedTags.Add(tag); else selectedTags.Remove(tag);
                Fire();
            };
            tagChips.Add(chip);
            tagRow.Children.Add(chip);
        }
        Restyle();
    }

    // Only categories that actually occur get a chip. The label is localised, while cat:media
    // and cat:音视频 both map to the same canonical id in the search box.
    public void SetCategories(IEnumerable<string> categories)
    {
        catRow.Children.Clear();
        catChips.Clear();
        var present = new HashSet<string>(categories.Where(FileCategories.IsKnown), StringComparer.OrdinalIgnoreCase);
        var list = FileCategories.All.Where(present.Contains).ToList();
        if (list.Count == 0)
        {
            catRow.Children.Add(catEmpty);
            return;
        }
        foreach (var category in list)
        {
            var chip = new FilterChip(FileCategories.Name(category), category);
            chip.SetOn(selectedCategories.Contains(category));
            chip.Changed += () =>
            {
                if (chip.IsOn) selectedCategories.Add(category); else selectedCategories.Remove(category);
                Fire();
            };
            catChips.Add(chip);
            catRow.Children.Add(chip);
        }
        Restyle();
    }

    public void SetCount(int n) => countText.Text = string.Format(S.FilterResult, n);

    public void Reset()
    {
        selectedApps.Clear();
        selectedTags.Clear();
        selectedCategories.Clear();
        withinDays = 0;
        foreach (var c in appChips) c.SetOn(false);
        foreach (var c in tagChips) c.SetOn(false);
        foreach (var c in catChips) c.SetOn(false);
        foreach (var c in kindChips) c.SetOn(false);
        for (int i = 0; i < timeChips.Count; i++) timeChips[i].SetOn(i == 0);
    }

    public void Restyle()
    {
        Background = Theme.B(Theme.P.Panel);
        BorderBrush = Theme.B(Theme.WithAlpha(Theme.P.PanelInk, 0x28));
        var ink = Theme.B(Theme.P.PanelInk);
        foreach (var t in texts) t.Foreground = ink;
        countText.Foreground = ink;
        clear.Foreground = ink;
        appEmpty.Foreground = ink;
        tagEmpty.Foreground = ink;
        catEmpty.Foreground = ink;
        hintText.Foreground = Theme.B(Theme.WithAlpha(Theme.P.PanelInk, 0xAA));
        foreach (var c in appChips.Concat<FilterChip>(tagChips).Concat(catChips).Concat(kindChips).Concat(timeChips)) c.Repaint();
    }

    void Fire() => Changed?.Invoke();
}
