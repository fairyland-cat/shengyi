using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace Shengyi;

enum Filter { All, Text, Images, Links, Files, Pinned, Sticky, Tags }

// Transparent strip along the top of one monitor. Draws the line and the cards (pinned first),
// scrolls sideways, and in keyboard mode shows a search bar and takes the keyboard focus.
// Rope sag, card size and all colours come from the live Theme.
sealed class LineWindow : Window
{
    const double LineTop = 7, CardTop = 1, Gap = 18, PinGap = 40, SideMargin = 48, Fade = 36;
    const int FilterCount = 8;
    const double FilterTopGap = 40;
    static double Sag => Theme.RopeSag;
    static double FilterBottom => BarTop + FilterTopGap + SearchFilterPanel.PanelHeight;

    // Grow the strip when the rope dips further or the cards are enlarged, so nothing is clipped.
    // The keyboard bar sits below the deepest possible card, not at a fixed offset.
    static double CardsBottom => CardTop + Sag + CardView.PegDropScaled + CardView.TallestCard * Theme.CardScale + 6;
    public static double BarTop => Math.Max(154, CardsBottom + 12);
    public static double PanelHeight => Math.Max(200, BarTop + 46);

    public Forms.Screen Screen { get; }
    public double Scale { get; private set; }
    public bool IsOut { get; private set; }
    public bool KeyboardMode { get; private set; }
    public event Action? Dismissed;

    // Set by --shots: windows are created off screen at this size and never take focus.
    public static Size? Stage { get; set; }

    readonly ClipStore store;
    readonly Actions actions;
    readonly Func<Settings> settings;
    readonly Canvas shelf = new(), viewport = new(), strip = new();
    readonly TranslateTransform slide = new(), scroll = new();
    readonly Path rope;
    // Reused brush + stop pool: scrolling only mutates offsets/colours, never allocates.
    readonly LinearGradientBrush ropePaint = new() { MappingMode = BrushMappingMode.Absolute };
    double lastGradientOffset = double.NaN;
    bool gradientActive;
    readonly Border hint, searchBar, tabBar, helpBar;
    readonly TextBlock hintText = new() { Foreground = Brushes.White, FontSize = 12 };
    readonly StackPanel bar = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    readonly TextBox search;
    readonly TextBlock placeholder, keysHelp;
    readonly SearchFilterPanel filterPanel;
    readonly Border gear;
    readonly List<(Filter filter, Border tab, TextBlock label)> tabs = new();
    readonly Dictionary<ClipItem, CardView> views = new();
    readonly Random rng = new();
    List<ClipItem> order = new();
    double[] xs = Array.Empty<double>();
    double offset, target, maxOffset, contentBottom = 40;
    int selected;
    Filter filter;
    IntPtr hwnd;
    bool ticking, filterOpen;
    string highlight = "";
    TimeSpan lastFrame;

    public LineWindow(Forms.Screen screen, ClipStore store, Actions actions, Func<Settings> settings)
    {
        Screen = screen;
        this.store = store;
        this.actions = actions;
        this.settings = settings;
        var b = screen.Bounds;
        Scale = Native.ScaleAt(b.Left + b.Width / 2, b.Top + b.Height / 2);

        Title = S.AppName;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = b.Width / Scale;
        Height = PanelHeight;
        slide.Y = -Height;

        rope = new Path
        {
            StrokeThickness = 1.8,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.4 },
        };
        hint = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 4, 12, 4),
            IsHitTestVisible = false,
            Child = hintText,
        };

        // Keyboard-mode bar: search box, filter tabs, key help.
        search = new TextBox
        {
            Width = 210,
            FontSize = 13,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        placeholder = new TextBlock
        {
            Text = S.SearchPlaceholder,
            FontSize = 13,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
        };
        var glyph = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        // The gear opens the advanced filter drawer; it lives inside the search field so the
        // crowded keyboard bar doesn't need another full-size control.
        gear = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = S.FilterGearTip,
            Child = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        gear.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SetFiltersOpen(!filterOpen);
            if (KeyboardMode) search.Focus();
        };
        var searchRow = new DockPanel();
        DockPanel.SetDock(glyph, Dock.Left);
        searchRow.Children.Add(glyph);
        DockPanel.SetDock(gear, Dock.Right);
        searchRow.Children.Add(gear);
        searchRow.Children.Add(new Grid { Children = { placeholder, search } });
        searchBar = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 5, 12, 5), Child = searchRow };
        bar.Children.Add(searchBar);
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        tabBar = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(3), Margin = new Thickness(8, 0, 0, 0), Child = segments };
        bar.Children.Add(tabBar);
        foreach (var (f, text) in new[]
        {
            (Filter.All, S.FilterAll), (Filter.Text, S.FilterText), (Filter.Images, S.FilterImages),
            (Filter.Links, S.FilterLinks), (Filter.Files, S.FilterFiles), (Filter.Pinned, S.FilterPinned),
            (Filter.Sticky, S.FilterSticky), (Filter.Tags, S.FilterTags),
        })
        {
            var label = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var tab = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(11, 3, 11, 3),
                Cursor = Cursors.Hand,
                Child = label,
            };
            tab.MouseLeftButtonDown += (_, e) => { e.Handled = true; SetFilter(f); };
            tabs.Add((f, tab, label));
            segments.Children.Add(tab);
        }
        keysHelp = new TextBlock { FontSize = 11 };
        helpBar = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = keysHelp,
        };
        bar.Children.Add(helpBar);
        Canvas.SetLeft(bar, SideMargin);
        Canvas.SetTop(bar, BarTop);

        filterPanel = new SearchFilterPanel { Visibility = Visibility.Collapsed };
        filterPanel.Changed += () => { selected = 0; target = 0; Relayout(false); };
        Canvas.SetLeft(filterPanel, SideMargin);
        Canvas.SetTop(filterPanel, BarTop + FilterTopGap);

        strip.RenderTransform = scroll;
        viewport.Children.Add(strip);
        shelf.Children.Add(rope);
        shelf.Children.Add(viewport);
        shelf.Children.Add(hint);
        shelf.Children.Add(bar);
        shelf.Children.Add(filterPanel);
        shelf.RenderTransform = slide;
        Content = new Grid { ClipToBounds = true, Children = { shelf } };

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
            Place();
        };
        SizeChanged += (_, _) => Relayout(false);
        IsVisibleChanged += (_, _) => SetTicking(IsVisible);
        PreviewKeyDown += OnKey;
        PreviewMouseWheel += OnWheel;
        Deactivated += (_, _) => { if (IsOut && KeyboardMode) Dismissed?.Invoke(); };
        search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
            selected = 0;
            target = 0;
            Relayout(false);
        };

        store.Items.CollectionChanged += OnItemsChanged;
        store.Bumped += OnBumped;
        store.Updated += OnUpdated;
        store.UsageReset += OnUsageReset;
        actions.MultiChanged += OnMultiChanged;
        Theme.Changed += OnThemeChanged;
        UpdateChrome();
    }

    // Exactly cover the top of the monitor, in physical pixels.
    void Place()
    {
        var b = Screen.Bounds;
        Scale = Native.ScaleAt(b.Left + b.Width / 2, b.Top + b.Height / 2);
        if (Stage is { } stage)
        {
            Native.SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, (int)(stage.Width * Scale), (int)Math.Ceiling(Height * Scale), Native.SWP_NOACTIVATE);
            return;
        }
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.Left, b.Top, b.Width,
            (int)Math.Ceiling(Height * Scale), Native.SWP_NOACTIVATE);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // WPF applies the suggested rectangle after this; put the window back afterwards.
        if (hwnd != IntPtr.Zero) Dispatcher.BeginInvoke(Place);
    }

    public void Reveal(bool keyboard)
    {
        if (IsOut && KeyboardMode == keyboard) return;
        bool wasOut = IsOut;
        IsOut = true;
        KeyboardMode = keyboard;
        if (!wasOut)
        {
            filter = Filter.All;
            selected = 0;
            target = offset = 0;
            scroll.X = 0;
            filterPanel.Reset();
            SetFiltersOpen(false, false);
            search.Text = "";
        }
        bar.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
        keysHelp.Text = (settings().PasteOnClick ? S.KeysHelpPaste : S.KeysHelpCopy) + S.NewStickyKey;
        UpdateTabs();
        if (!IsVisible) Show();
        SetActivatable(keyboard);
        Relayout(false);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        if (keyboard && Stage == null)
        {
            Native.ForceForeground(hwnd);
            Activate();
            search.Focus();
            Keyboard.Focus(search);
        }
        if (!wasOut)
        {
            foreach (var v in views.Values) v.Velocity += (rng.NextDouble() - 0.5) * 50;
            SlideTo(0, EasingMode.EaseOut, 280, null);
        }
    }

    public void Retract()
    {
        if (!IsOut) return;
        IsOut = false;
        KeyboardMode = false;
        actions.ClearMulti(); // a hidden line must not keep an invisible Ctrl+Click selection
        SetActivatable(false);
        SlideTo(-Height, EasingMode.EaseIn, 200, () => { if (!IsOut) Hide(); });
    }

    // Keyboard mode needs focus; otherwise the line must never steal it from the app you're working in.
    void SetActivatable(bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        ex = on ? ex & ~Native.WS_EX_NOACTIVATE : ex | Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }

    void SlideTo(double y, EasingMode mode, int ms, Action? done)
    {
        var a = new DoubleAnimation(y, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = mode } };
        if (done != null) a.Completed += (_, _) => done();
        slide.BeginAnimation(TranslateTransform.YProperty, a);
    }

    // The area that keeps the line out: the top of the monitor down to just below the cards (or the bar).
    public bool HoverZoneContains(Native.POINT p)
    {
        var b = Screen.Bounds;
        return p.X >= b.Left && p.X < b.Right && p.Y >= b.Top && p.Y <= b.Top + (contentBottom + 14) * Scale;
    }

    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Relayout(IsOut);

    void OnBumped(ClipItem item)
    {
        if (views.TryGetValue(item, out var v)) v.Wobble();
    }

    void OnUpdated(ClipItem item)
    {
        if (views.ContainsKey(item)) Unrealize(item);
        Relayout(false);
    }

    // A deliberate reset should be visible at once, unlike a single take.
    void OnUsageReset() => Relayout(false);

    // Ctrl+Click selection lives in Actions so every monitor sees the same set.
    void OnMultiChanged() => UpdateDecor();

    // A palette/size change invalidates every realized card: rebuild them from the new theme.
    void OnThemeChanged()
    {
        foreach (var v in views.Values) v.Detach();
        views.Clear();
        strip.Children.Clear();
        UpdateWindowHeight();
        Canvas.SetTop(bar, BarTop);
        Canvas.SetTop(filterPanel, BarTop + FilterTopGap);
        UpdateChrome();
        if (hwnd != IntPtr.Zero) Place();
        Relayout(false);
        if (!IsOut) slide.Y = -Height;
    }

    void UpdateChrome()
    {
        var p = Theme.P;
        UpdateRopeGradient(force: true);
        hint.Background = Theme.B(p.Hint);
        searchBar.Background = Theme.B(p.Panel);
        tabBar.Background = Theme.B(p.Panel);
        helpBar.Background = Theme.B(p.Hint);
        search.Foreground = Theme.B(p.PanelInk);
        search.CaretBrush = Theme.B(p.PanelInk);
        placeholder.Foreground = Theme.B(Theme.WithAlpha(p.PanelInk, 0x99));
        keysHelp.Foreground = Theme.B(Theme.WithAlpha(p.PanelInk, 0xCC));
        gear.Background = filterOpen ? Theme.B(Theme.P.TabOn) : Brushes.Transparent;
        ((TextBlock)gear.Child).Foreground = filterOpen ? Theme.B(Theme.P.TabOnInk) : Theme.B(p.PanelInk);
        filterPanel.Restyle();
        UpdateTabs();
    }

    // 0.7.0 · dye the rope only where cards hang in the current viewport.
    // centres use the SAME coordinates as PlaceCards(): xs[i] + card width/2 - offset.
    // 色值卡 / 图片卡 (and sticky notes with their own paper) contribute no colour, so bare
    // stretches — including those between two cards — stay the plain hemp colour.
    void UpdateRopeGradient(bool force = false)
    {
        double W = ActualWidth;
        if (W <= 0) { rope.Stroke = Theme.B(Theme.P.Rope); return; }
        if (!Theme.RopeGradientOn)
        {
            rope.Stroke = Theme.B(Theme.P.Rope);
            gradientActive = false;
            lastGradientOffset = double.NaN;
            return;
        }
        if (!force && !double.IsNaN(lastGradientOffset) && Math.Abs(offset - lastGradientOffset) < 2) return;
        lastGradientOffset = offset;

        var cards = new List<(double center, Rgb color)>();
        for (int i = 0; i < order.Count && i < xs.Length; i++)
        {
            double center = xs[i] + CardView.SizeOf(order[i]).Width / 2 - offset;
            if (center < -60 || center > W + 60) continue;
            if (Theme.TintOf(order[i]) is { } c) cards.Add((center, c));
        }
        var stops = CardTints.RopeStops(cards, W, Theme.RopeBase, Theme.RopeStrength, Theme.RopeBanded);
        ropePaint.StartPoint = new Point(0, 0);
        ropePaint.EndPoint = new Point(W, 0);
        while (ropePaint.GradientStops.Count < stops.Length)
            ropePaint.GradientStops.Add(new GradientStop());
        for (int i = 0; i < stops.Length; i++)
        {
            ropePaint.GradientStops[i].Offset = stops[i].Offset;
            ropePaint.GradientStops[i].Color = Theme.Wpf(stops[i].Color);
        }
        while (ropePaint.GradientStops.Count > stops.Length)
            ropePaint.GradientStops.RemoveAt(ropePaint.GradientStops.Count - 1);
        if (!gradientActive) { rope.Stroke = ropePaint; gradientActive = true; }
    }

    // Opening the drawer makes the strip taller; the window, the hover zone and the viewport
    // all follow the same height so the panel is never clipped or left outside the hit area.
    void UpdateWindowHeight()
    {
        Height = Math.Max(PanelHeight, filterOpen ? FilterBottom + 16 : 0);
        if (hwnd != IntPtr.Zero) Place();
    }

    public void SetFiltersOpen(bool open, bool relayout = true)
    {
        filterOpen = open;
        filterPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        gear.Background = open ? Theme.B(Theme.P.TabOn) : Brushes.Transparent;
        ((TextBlock)gear.Child).Foreground = open ? Theme.B(Theme.P.TabOnInk) : Theme.B(Theme.P.PanelInk);
        if (open)
        {
            filterPanel.SetApps(store.Items.Where(i => i.SourceApp != null)
                .GroupBy(i => i.SourceApp!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Select(g => g.Key));
            filterPanel.SetTags(store.Items.Where(i => i.Tags != null)
                .SelectMany(i => i.Tags!).Distinct(StringComparer.OrdinalIgnoreCase));
            filterPanel.SetCategories(store.Items.Where(i => i.FileCategory != null)
                .Select(i => i.FileCategory!).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        UpdateWindowHeight();
        if (relayout) { selected = 0; target = 0; Relayout(false); }
    }

    // Used by the off-screen screenshots, and handy for any future automation.
    public void SetQuery(string text) => search.Text = text;

    // Quick insert asks the active line for its first text hit before falling back to "latest text".
    public string Query => search.Text;
    public ClipItem? TopTextResult =>
        order.FirstOrDefault(i => (i.IsText || i.IsSticky) && !string.IsNullOrWhiteSpace(i.Text));

    void SetFilter(Filter f)
    {
        filter = f;
        selected = 0;
        target = 0;
        UpdateTabs();
        Relayout(false);
        if (KeyboardMode) search.Focus();
    }

    void UpdateTabs()
    {
        foreach (var (f, tab, label) in tabs)
        {
            bool on = f == filter;
            tab.Background = on ? Theme.B(Theme.P.TabOn) : Brushes.Transparent;
            label.Foreground = on ? Theme.B(Theme.P.TabOnInk) : Theme.B(Theme.P.TabOffInk);
            label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    List<ClipItem> Visible()
    {
        if (!KeyboardMode)
        {
            highlight = "";
            return Ranking.Order(store.Items);
        }

        var q = SearchQuery.Parse(search.Text, filterPanel.SelectedApps,
                                  filterPanel.SelectedKinds, filterPanel.WithinDays, filterPanel.SelectedTags,
                                  filterPanel.SelectedCategories);
        q.AddTab(filter);
        highlight = q.Highlight;

        IEnumerable<ClipItem> match;
        Dictionary<ClipItem, double>? scores = null;
        if (q.HasTerms || q.HasFilters)
        {
            var hits = store.Search(q);
            match = hits.Select(h => h.item);
            scores = hits.ToDictionary(h => h.item, h => h.score);
        }
        else match = store.Items;

        // Only this view order changes; Items keeps its physical order for dedup, Sweep() and JSON.
        var ranked = Ranking.Order(match);
        if (!q.HasTerms || scores == null) return ranked; // filters alone keep the frequency order
        return ranked
            .OrderBy(i => i.IsFront ? 0 : 1)             // pinned cards and notes always lead
            .ThenByDescending(i => scores.TryGetValue(i, out double s) ? s : 0)
            .ThenByDescending(i => i.Time)
            .ToList();
    }

    // The rope is a quadratic Bezier; at horizontal position x its dip is 4*Sag*t*(1-t).
    double RopeY(double screenX)
    {
        double W = ActualWidth;
        if (W <= 0 || Sag <= 0) return 0;
        double t = Math.Clamp(screenX / W, 0, 1);
        return 4 * Sag * t * (1 - t);
    }

    void PlaceCards()
    {
        for (int i = 0; i < order.Count; i++)
            if (views.TryGetValue(order[i], out var v))
            {
                double w = CardView.SizeOf(order[i]).Width;
                Canvas.SetTop(v, CardTop + RopeY(xs[i] + w / 2 - offset));
            }
    }

    void Relayout(bool animate)
    {
        double W = ActualWidth;
        if (W <= 0) return;

        var fig = new PathFigure { StartPoint = new Point(0, LineTop) };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(W / 2, LineTop + 2 * Sag), new Point(W, LineTop), true));
        rope.Data = new PathGeometry(new[] { fig });
        viewport.Width = W;
        viewport.Height = Height;

        // Pinned cards first, then a longer stretch of bare line, then the history.
        order = Visible();
        filterPanel.SetCount(order.Count);
        xs = new double[order.Count];
        double x = SideMargin, tallest = 0;
        for (int i = 0; i < order.Count; i++)
        {
            if (i > 0 && order[i - 1].IsFront && !order[i].IsFront) x += PinGap - Gap;
            xs[i] = x;
            var size = CardView.SizeOf(order[i]);
            x += size.Width + Gap;
            tallest = Math.Max(tallest, size.Height);
        }
        double contentWidth = order.Count == 0 ? 0 : x - Gap + SideMargin;
        if (contentWidth < W)
            for (int i = 0; i < xs.Length; i++) xs[i] += (W - contentWidth) / 2;
        maxOffset = Math.Max(0, contentWidth - W);
        target = Math.Clamp(target, 0, maxOffset);
        offset = Math.Clamp(offset, 0, maxOffset);
        scroll.X = -offset;
        selected = Math.Clamp(selected, 0, Math.Max(0, order.Count - 1));

        var shown = new HashSet<ClipItem>(order);
        foreach (var gone in views.Keys.Where(k => !shown.Contains(k)).ToList()) Unrealize(gone);
        for (int i = 0; i < order.Count; i++)
            if (views.TryGetValue(order[i], out var v)) Move(v, xs[i], animate);
        Realize();
        PlaceCards();
        UpdateRopeGradient(force: true);

        hint.Visibility = order.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (order.Count == 0)
        {
            hintText.Text = store.Items.Count == 0 ? S.Hint : S.NoMatches;
            hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(hint, (W - hint.DesiredSize.Width) / 2);
            Canvas.SetTop(hint, LineTop + Sag + 12);
        }
        double cards = order.Count == 0 ? LineTop + Sag + 12 + hint.DesiredSize.Height
            : CardTop + Sag + CardView.PegDropScaled + tallest + 6;
        contentBottom = KeyboardMode ? (filterOpen ? FilterBottom : BarTop + 34) : cards;
    }

    // Only cards near the visible stretch exist as elements; the history can be long.
    void Realize()
    {
        double lo = offset - 200, hi = offset + ActualWidth + 200;
        for (int i = 0; i < order.Count; i++)
        {
            var item = order[i];
            double w = CardView.SizeOf(item).Width;
            bool want = xs[i] + w >= lo && xs[i] <= hi;
            bool has = views.ContainsKey(item);
            if (want && !has)
            {
                var v = new CardView(item, actions)
                {
                    Velocity = (rng.Next(2) == 0 ? -1 : 1) * 40,
                    Highlight = highlight,
                };
                views[item] = v;
                strip.Children.Add(v);
                Canvas.SetLeft(v, xs[i]);
                Canvas.SetTop(v, CardTop + RopeY(xs[i] + w / 2 - offset));
            }
            else if (!want && has) Unrealize(item);
        }
        UpdateDecor();
    }

    void Unrealize(ClipItem item)
    {
        var v = views[item];
        v.Detach();
        strip.Children.Remove(v);
        views.Remove(item);
    }

    // Selection ring, Ctrl+number badges, and the fades that say there's more to scroll to.
    void UpdateDecor()
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (!views.TryGetValue(order[i], out var v)) continue;
            v.Number = KeyboardMode && i < 9 ? i + 1 : 0;
            v.Selected = KeyboardMode && i == selected;
            v.Checked = actions.IsMulti(order[i]);
            v.Highlight = highlight;
        }
        double W = viewport.Width;
        if (W <= 0 || maxOffset <= 0) { viewport.OpacityMask = null; return; }
        var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(W, 0) };
        mask.GradientStops.Add(new GradientStop(offset > 1 ? Colors.Transparent : Colors.Black, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, Fade / W));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - Fade / W));
        mask.GradientStops.Add(new GradientStop(offset < maxOffset - 1 ? Colors.Transparent : Colors.Black, 1));
        viewport.OpacityMask = mask;
    }

    static void Move(UIElement v, double left, bool animate)
    {
        if (!animate)
        {
            v.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(v, left);
            return;
        }
        var a = new DoubleAnimation(left, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        v.BeginAnimation(Canvas.LeftProperty, a);
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (maxOffset <= 0) return;
        target = Math.Clamp(target - e.Delta, 0, maxOffset);
        e.Handled = true;
    }

    void Select(int i)
    {
        if (order.Count == 0) return;
        selected = Math.Clamp(i, 0, order.Count - 1);
        double x = xs[selected], w = CardView.SizeOf(order[selected]).Width, W = ActualWidth;
        if (x - target < SideMargin) target = x - SideMargin;
        else if (x + w - target > W - SideMargin) target = x + w - W + SideMargin;
        target = Math.Clamp(target, 0, maxOffset);
        UpdateDecor();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (!KeyboardMode) return;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var item = selected < order.Count ? order[selected] : null;
        bool empty = search.Text.Length == 0;

        switch (key)
        {
            case Key.Escape:
                if (filterOpen) SetFiltersOpen(false);
                else if (!empty) search.Clear();
                else Dismissed?.Invoke();
                break;
            case Key.Left: Select(selected - 1); break;
            case Key.Right: Select(selected + 1); break;
            case Key.Home when empty: Select(0); break;
            case Key.End when empty: Select(order.Count - 1); break;
            case Key.Tab: SetFilter((Filter)(((int)filter + (shift ? FilterCount - 1 : 1)) % FilterCount)); break;
            case Key.Enter when item != null:
                actions.Primary(item, shift, views.GetValueOrDefault(item));
                Dismissed?.Invoke();
                break;
            case Key.Space when empty && item != null: actions.QuickLook(item); break;
            case Key.Delete when empty && item != null: actions.Remove(item); break;
            case Key.P when ctrl && item != null: actions.TogglePin(item); break;
            case Key.E when ctrl && item != null: actions.Edit(item); break;
            case Key.N when ctrl: actions.NewSticky(); break;
            // Ctrl+M merges the Ctrl+Click selection into the clipboard; add Shift to paste it too.
            // (Ctrl+C is deliberately left alone so it still copies text inside the search box.)
            case Key.M when ctrl && actions.MultiItems.Count >= 2: actions.Merge(shift); break;
            case >= Key.D1 and <= Key.D9 when ctrl:
            {
                int n = key - Key.D1;
                if (n >= order.Count) break;
                actions.Primary(order[n], shift, views.GetValueOrDefault(order[n]));
                Dismissed?.Invoke();
                break;
            }
            default: return;
        }
        e.Handled = true;
    }

    void SetTicking(bool on)
    {
        if (on == ticking) return;
        ticking = on;
        lastFrame = TimeSpan.Zero;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        if (t == lastFrame) return;
        double dt = lastFrame == TimeSpan.Zero ? 0.016 : Math.Min((t - lastFrame).TotalSeconds, 0.05);
        lastFrame = t;

        if (offset != target)
        {
            offset = Math.Abs(target - offset) < 0.5 ? target : offset + (target - offset) * Math.Min(1, dt * 12);
            scroll.X = -offset;
            Realize();
            PlaceCards();
            UpdateRopeGradient();
        }
        foreach (var v in views.Values) v.Step(dt, rng);
    }

    protected override void OnClosed(EventArgs e)
    {
        SetTicking(false);
        Theme.Changed -= OnThemeChanged;
        store.Items.CollectionChanged -= OnItemsChanged;
        store.Bumped -= OnBumped;
        store.Updated -= OnUpdated;
        store.UsageReset -= OnUsageReset;
        actions.MultiChanged -= OnMultiChanged;
        foreach (var v in views.Values) v.Detach();
        base.OnClosed(e);
    }
}
