using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Shengyi;

// One clip on the line: a photo, a sticky note, a code card, a link card, a colour chip or a file
// card, held by a peg (red when pinned). Handles swing physics, hover/selection and the mouse gestures.
// Colours come from Theme.P; the whole card is scaled with a LayoutTransform so the line can keep
// treating it as a plain box of SizeOf(item).
sealed class CardView : Grid
{
    public const double PegDrop = 10; // card top sits this far below the peg top
    const double ImageHeight = 92, Radius = 10;
    public const double TallestCard = 112; // sticky notes are the tallest card

    public ClipItem Item { get; }
    public double Angle, Velocity; // degrees, degrees per second

    readonly Actions actions;
    readonly Border frame, close, badge, number, ring, check;
    readonly TextBlock badgeText = new() { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold };
    readonly TextBlock numberText = new() { FontSize = 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly ScaleTransform scale = new();
    readonly RotateTransform rotate = new();
    readonly DropShadowEffect shadow = new() { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.26 };
    readonly DispatcherTimer holdTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    static readonly SolidColorBrush HighlightBrush = MakeHighlightBrush();
    TextBlock? bodyText;
    Color? tintedBottom; // set by Dress(); the sticky pin follows its card when tinted
    string bodyFull = "", highlight = "";
    Point downAt;
    bool pressed, hover, selected, checkedOn;
    int shownNumber = -1;

    static SolidColorBrush MakeHighlightBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xD5, 0x4F));
        brush.Freeze();
        return brush;
    }

    // Unscaled card size; only the line's layout uses the scaled SizeOf below.
    public static Size BaseSizeOf(ClipItem item) => item.Kind switch
    {
        ClipKind.Image => new Size(Math.Clamp(ImageHeight * Aspect(item), 64, 200) + 10, ImageHeight + 10),
        ClipKind.Text => new Size(150, 104),
        ClipKind.Code => new Size(184, 104),
        ClipKind.Link => new Size(180, 76),
        ClipKind.Color => new Size(88, 94),
        ClipKind.Sticky => new Size(158, 112),
        _ => new Size(170, 62),
    };

    // Size in line coordinates, including the current card-size setting.
    public static Size SizeOf(ClipItem item)
    {
        var s = BaseSizeOf(item);
        double k = Theme.CardScale;
        return new Size(s.Width * k, s.Height * k);
    }

    public static double PegDropScaled => PegDrop * Theme.CardScale;

    static double Aspect(ClipItem i) => i.Width > 0 && i.Height > 0 ? (double)i.Width / i.Height : 1.6;

    public CardView(ClipItem item, Actions actions)
    {
        Item = item;
        this.actions = actions;
        var p = Theme.P;
        bool sticky = item.IsSticky;
        var size = BaseSizeOf(item);
        Width = size.Width;
        Height = PegDrop + size.Height;
        LayoutTransform = new ScaleTransform(Theme.CardScale, Theme.CardScale);
        RenderTransformOrigin = new Point(0.5, 0);
        RenderTransform = new TransformGroup { Children = { scale, rotate } };

        // Focus ring: an accent outline with a soft glow, a few pixels clear of the card.
        ring = new Border
        {
            CornerRadius = new CornerRadius(Radius + 4),
            BorderThickness = new Thickness(2),
            BorderBrush = Theme.B(p.Accent),
            Background = Theme.B(Theme.WithAlpha(p.Accent, 0x24)),
            Margin = new Thickness(-5, PegDrop - 5, -5, -5),
            Effect = new DropShadowEffect { Color = p.Accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.85 },
            Opacity = 0,
            IsHitTestVisible = false,
        };

        frame = new Border
        {
            Width = size.Width,
            Height = size.Height,
            Margin = new Thickness(0, PegDrop, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(Radius),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.B(p.CardBorder),
            Effect = shadow,
            Cursor = Cursors.Hand,
        };

        // Computed on open, so the counts stay current without rebuilding the card on every take.
        var tipText = new TextBlock { FontSize = 12, Foreground = Theme.B(p.WindowInk) };
        var tip = new ToolTip
        {
            Content = tipText,
            Background = Theme.B(p.Window),
            BorderBrush = Theme.B(p.Line),
            Foreground = Theme.B(p.WindowInk),
        };
        tip.Opened += (_, _) =>
        {
            tipText.Text = Item.IsSticky ? S.StickyTip
                : Item.TotalUses == 0 ? S.UsageNone
                : string.Format(S.UsageTip, Theme.FreqWindowDays,
                    Item.CountUses(Theme.FreqWindowDays), Item.TotalUses);
            if (!string.IsNullOrEmpty(Item.SourceApp))
                tipText.Text += "  ·  " + string.Format(S.SourceTip, Item.SourceApp);
            if (Item.Tags is { Count: > 0 } tags)
                tipText.Text += "  ·  " + string.Format(S.TagsTip, string.Join("、", tags.Select(SmartTags.Name)));
        };
        ToolTipService.SetToolTip(frame, tip);

        badge = new Border
        {
            Background = Theme.B(p.Badge),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            IsHitTestVisible = false,
            Child = badgeText,
        };
        var inner = new Grid();
        inner.Children.Add(Dress(item));
        inner.Children.Add(badge);
        Round(inner, Math.Max(3, Radius - 1 - frame.Padding.Left));
        frame.Child = inner;

        var peg = new Border
        {
            Width = 10,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = item.Pinned
                ? Horizontal(p.PegPinA, p.PegPinB, p.PegPinC)
                : Horizontal(p.PegA, p.PegB, p.PegC),
            BorderBrush = B(0, 0, 0, 0x40),
            BorderThickness = new Thickness(0.8),
            IsHitTestVisible = false,
            Child = new Border // the spring
            {
                Width = 4,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = B(0, 0, 0, 0x55),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            },
        };
        // A note is held by a round pin in its own paper colour instead of a clothes peg.
        if (sticky)
        {
            peg.Width = 12;
            peg.Height = 12;
            peg.CornerRadius = new CornerRadius(6);
            peg.Background = Theme.B(Theme.Mix(tintedBottom ?? Theme.NoteBase(item.NoteColor), Colors.Black, 0.32));
            peg.Child = null;
        }

        // Ctrl+number keycap under the card in keyboard mode.
        number = new Border
        {
            MinWidth = 20,
            Height = 18,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 0, 5, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = B(0, 0, 0, 0x22),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -(TallestCard - size.Height) - 27), // one baseline for every card, below the tallest
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = numberText,
        };

        close = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Theme.B(p.Close),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, PegDrop - 7, -7, 0),
            Opacity = 0,
            IsHitTestVisible = false,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = Brushes.White,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; actions.Remove(Item); };

        // Ctrl+Click selection for merge / sequence paste, drawn on the card's top-left corner.
        check = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Theme.B(p.Accent),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            // Mirror the ✕ on the opposite corner: straddling the card edge keeps the badge
            // clear of the first line of text instead of covering the first character.
            Margin = new Thickness(-7, PegDrop - 6, 0, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "✓", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
            },
        };

        Children.Add(ring);
        Children.Add(frame);
        Children.Add(peg);
        Children.Add(number);
        // What the local recogniser found, as a small badge below the bottom-right corner. It
        // shares the Ctrl+number baseline, so it never covers the card body or its char counter.
        if (item.Tags is { Count: > 0 } tags && item.Kind is not (ClipKind.Image or ClipKind.Color))
        {
            string main = SmartTags.Primary(tags) ?? "";
            string more = tags.Count > 1 ? " " + string.Format(S.TagMore, tags.Count - 1) : "";
            Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(6, 1, 6, 1),
                Background = Theme.B(Theme.WithAlpha(p.Accent, 0xE8)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                // A fixed offset keeps the badge just under the card for every card height;
                // sharing the keycap baseline pushed it into the keyboard toolbar.
                Margin = new Thickness(0, 0, 2, -20),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = main + more, FontSize = 9.5, Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                },
            });
        }
        // File cards carry a coarse category (media / archive / document / picture / folder).
        // It sits on the opposite side from the smart-tag badge, so both can show at once.
        if (item.Kind == ClipKind.Files && item.FileCategory is { } category)
        {
            Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(6, 1, 6, 1),
                Background = Theme.B(p.Panel),
                BorderBrush = Theme.B(Theme.WithAlpha(p.PanelInk, 0x40)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 0, -20),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = FileCategories.Name(category), FontSize = 9.5,
                    Foreground = Theme.B(p.PanelInk), FontWeight = FontWeights.SemiBold,
                },
            });
        }
        Children.Add(check);
        Children.Add(close);
        Restyle();
        ApplyHighlight();

        MouseEnter += (_, _) =>
        {
            hover = true;
            close.IsHitTestVisible = true;
            Fade(close, 1, 120);
            Wobble();
            Restyle();
        };
        MouseLeave += (_, _) =>
        {
            hover = false;
            close.IsHitTestVisible = false;
            Fade(close, 0, 160);
            Restyle();
        };
        frame.MouseLeftButtonDown += OnDown;
        frame.MouseMove += OnMove;
        frame.MouseLeftButtonUp += OnUp;
        frame.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            actions.QuickLook(Item);
        };
        frame.MouseRightButtonUp += (_, e) => { e.Handled = true; actions.ShowMenu(Item, this); };
        holdTimer.Tick += OnHold;
    }

    public void Detach() => holdTimer.Stop();

    public bool Selected
    {
        set
        {
            if (value == selected) return;
            selected = value;
            Fade(ring, value ? 1 : 0, 160);
            Restyle();
        }
    }

    // Part of the Ctrl+Click merge selection (as opposed to the keyboard cursor above).
    public bool Checked
    {
        set
        {
            if (value == checkedOn) return;
            checkedOn = value;
            check.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Restyle();
        }
    }

    // Ctrl+1…9 shortcut shown in keyboard mode; 0 hides it.
    public int Number
    {
        set
        {
            if (value == shownNumber) return;
            shownNumber = value;
            numberText.Text = value.ToString();
            number.Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Words from the search box, painted on the card body. The same CardView is reused while
    // the query changes, so this rebuilds only the Inlines of the body TextBlock.
    public string Highlight
    {
        get => highlight;
        set
        {
            string next = value ?? "";
            if (next == highlight) return;
            highlight = next;
            ApplyHighlight();
        }
    }

    void CaptureBody(UIElement element, string full)
    {
        bodyFull = full;
        bodyText = (element as DockPanel)?.Tag as TextBlock;
    }

    void ApplyHighlight()
    {
        if (bodyText == null) return;
        var inlines = bodyText.Inlines;
        inlines.Clear();
        string text = bodyFull;
        if (text.Length == 0 || highlight.Length == 0) { inlines.Add(new Run(text)); return; }

        var spans = new List<(int start, int end)>();
        foreach (var term in highlight.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int from = 0;
            while (from <= text.Length - term.Length)
            {
                int at = text.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;
                spans.Add((at, at + term.Length));
                from = at + Math.Max(1, term.Length);
            }
        }
        if (spans.Count == 0) { inlines.Add(new Run(text)); return; }
        spans.Sort((a, b) => a.start != b.start ? a.start.CompareTo(b.start) : b.end.CompareTo(a.end));

        int cursor = 0;
        foreach (var (start, end) in spans)
        {
            if (start < cursor) continue; // overlapping hit: the earlier run already covers it
            if (start > cursor) inlines.Add(new Run(text[cursor..start]));
            inlines.Add(new Run(text[start..end]) { Background = HighlightBrush });
            cursor = end;
        }
        if (cursor < text.Length) inlines.Add(new Run(text[cursor..]));
    }

    // Selected cards and the one under the pointer lift a little: bigger, deeper shadow, on top.
    void Restyle()
    {
        double s = selected ? 1.07 : (hover || checkedOn) ? 1.035 : 1;
        var a = new DoubleAnimation(s, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        bool lifted = selected || hover || checkedOn;
        shadow.BlurRadius = lifted ? 24 : 16;
        shadow.ShadowDepth = lifted ? 7 : 4;
        shadow.Opacity = lifted ? 0.34 : 0.26;
        Panel.SetZIndex(this, selected ? 20 : hover ? 10 : 0);
        number.Background = selected ? Theme.B(Theme.P.Accent) : Theme.B(Theme.P.Keycap);
        numberText.Foreground = selected ? Brushes.White : Theme.B(Theme.P.KeycapInk);
    }

    public void Wobble() => Velocity += (Random.Shared.Next(2) == 0 ? -1 : 1) * 40;

    public void Flash(string text)
    {
        badgeText.Text = "✓  " + text;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1100) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100))));
        badge.BeginAnimation(OpacityProperty, a);
    }

    // Damped pendulum around the peg, with the occasional gust of breeze.
    public void Step(double dt, Random rng)
    {
        if (rng.NextDouble() < dt * 0.12) Velocity += (rng.NextDouble() - 0.5) * 10;
        Velocity += (-40 * Angle - 1.6 * Velocity) * dt;
        Angle += Velocity * dt;
        rotate.Angle = Angle;
    }

    // Styles the frame for the item's kind and returns what goes inside it.
    UIElement Dress(ClipItem item)
    {
        var p = Theme.P;
        var paint = Theme.CardPaint(item);
        bool tinted = paint is not null;
        tintedBottom = paint?.Bottom;

        // Classic colours per card kind. A tint scheme replaces all five at once; 色值卡 and
        // 图片卡 never get a paint, so they (and their real content) stay exactly as before.
        Color bgTop, bgBottom, border, ink, dim;
        switch (item.Kind)
        {
            case ClipKind.Code:
                bgTop = bgBottom = p.CodeA; border = p.CodeBorder; ink = p.CodeInk; dim = p.CodeDim; break;
            case ClipKind.Link:
                bgTop = bgBottom = p.CardA; border = p.CardBorder; ink = p.LinkInk; dim = p.CardDim; break;
            case ClipKind.Files:
                bgTop = p.CardA; bgBottom = p.CardB; border = p.CardBorder; ink = p.CardInk; dim = p.CardDim; break;
            case ClipKind.Text:
                bgTop = p.PaperA; bgBottom = p.PaperB; border = p.PaperBorder; ink = p.PaperInk; dim = p.PaperDim; break;
            default:
                bgTop = bgBottom = p.CardA; border = p.CardBorder; ink = p.CardInk; dim = p.CardDim; break;
        }
        if (paint is { } q)
        {
            bgTop = q.Top; bgBottom = q.Bottom; border = q.Border; ink = q.Ink; dim = q.Dim;
        }
        frame.BorderBrush = Theme.B(border);
        // Tinted cards always use a gentle top→bottom gradient; untinted ones keep their
        // original solid / gradient choice so "off" is pixel-identical to 0.6.1.
        Brush CardBg(bool classicVertical) =>
            tinted || classicVertical ? Theme.Vertical(bgTop, bgBottom) : Theme.B(bgTop);
        Color iconBg = tinted ? Theme.Mix(bgBottom, Colors.White, 0.35) : p.LinkIconBg;
        Color tileBg = tinted ? Theme.Mix(bgTop, Colors.White, 0.45) : p.Tile;

        switch (item.Kind)
        {
            case ClipKind.Image:
            {
                frame.Background = CardBg(false);
                frame.Padding = new Thickness(4);
                var g = new Grid { Background = Theme.B(p.CodeB) };
                g.Children.Add(new Image { Source = Thumbs.LoadFull(item.ImagePath!, 200), Stretch = Stretch.UniformToFill });
                return g;
            }
            case ClipKind.Code:
            {
                frame.Background = CardBg(false);
                frame.Padding = new Thickness(10, 8, 10, 6);
                var lines = (item.Text ?? "").Replace("\r\n", "\n").Split('\n');
                var preview = string.Join("\n", lines.SkipWhile(l => l.Trim().Length == 0).Take(8)).Replace("\t", "    ");
                var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                foreach (var c in new[] { Color.FromRgb(0xF8, 0x71, 0x71), Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0x34, 0xD3, 0x99) })
                    dots.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 4, 0) });
                var code = Note(preview, string.Format(S.Lines, lines.Length), Theme.B(ink), Theme.B(dim),
                    wrap: false, mono: true, rich: false, header: dots);
                CaptureBody(code, preview);
                return code;
            }
            case ClipKind.Link:
            {
                frame.Background = CardBg(false);
                frame.Padding = new Thickness(10, 9, 10, 8);
                bool mail = item.Text!.Trim().StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
                var (domain, rest) = SplitLink(item.Text!.Trim());
                var head = new DockPanel();
                var icon = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = Theme.B(iconBg),
                    Margin = new Thickness(0, 0, 7, 0),
                    Child = new TextBlock
                    {
                        Text = mail ? "" : "",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 11,
                        Foreground = Theme.B(p.Accent),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                DockPanel.SetDock(icon, Dock.Left);
                head.Children.Add(icon);
                head.Children.Add(new TextBlock
                {
                    Text = domain, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.B(ink),
                });
                var stack = new StackPanel();
                stack.Children.Add(head);
                stack.Children.Add(new TextBlock
                {
                    Text = rest, FontSize = 11, Margin = new Thickness(0, 5, 0, 0), MaxHeight = 30,
                    TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.B(dim),
                });
                return stack;
            }
            case ClipKind.Color:
            {
                // 色值卡不染色：外框跟随主题，内部是复制到的真实颜色。
                frame.Background = CardBg(false);
                frame.Padding = new Thickness(5);
                var dock = new DockPanel();
                var label = new TextBlock
                {
                    Text = item.Text!.Trim(), FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(0, 4, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.B(dim),
                };
                DockPanel.SetDock(label, Dock.Bottom);
                dock.Children.Add(label);
                var color = Classify.ParseColor(item.Text!) ?? Colors.Transparent;
                dock.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(color),
                    BorderBrush = B(0, 0, 0, 0x1A),
                    BorderThickness = new Thickness(1),
                });
                return dock;
            }
            case ClipKind.Files:
            {
                frame.Background = CardBg(true);
                frame.Padding = new Thickness(9, 8, 10, 8);
                var files = item.Files ?? new();
                var first = files.FirstOrDefault() ?? "";
                UIElement icon = Thumbs.FileIcon(first) is { } src
                    ? new Image { Source = src, Width = 24, Height = 24 }
                    : new TextBlock
                    {
                        Text = Directory.Exists(first) ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18,
                        Foreground = Theme.B(dim), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    };
                var tile = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(9),
                    Background = Theme.B(tileBg),
                    BorderBrush = Theme.B(border),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 9, 0),
                    Child = icon,
                };
                var dock = new DockPanel();
                DockPanel.SetDock(tile, Dock.Left);
                dock.Children.Add(tile);
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock
                {
                    Text = Path.GetFileName(first.TrimEnd('\\')), FontSize = 12, FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.B(ink),
                });
                stack.Children.Add(new TextBlock
                {
                    Text = files.Count > 1 ? string.Format(S.MoreFiles, files.Count - 1) : Path.GetFileName(Path.GetDirectoryName(first.TrimEnd('\\')) ?? ""),
                    FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.B(dim),
                });
                dock.Children.Add(stack);
                return dock;
            }
            case ClipKind.Sticky:
            {
                var n = Theme.NotePalette(item.NoteColor);
                Color nInk = tinted ? ink : n.ink;
                Color nDim = tinted ? dim : n.dim;
                frame.Background = tinted ? CardBg(true) : Theme.Vertical(n.top, n.bottom);
                frame.BorderBrush = Theme.B(tinted ? border : n.border);
                frame.CornerRadius = new CornerRadius(4);
                frame.Padding = new Thickness(11, 8, 11, 7);
                var text = (item.Text ?? "").Trim().Replace("\r\n", "\n").Replace('\t', ' ');
                if (text.Length > 240) text = text[..240] + "…";

                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var tag = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = Theme.B(Theme.WithAlpha(nInk, 0x20)),
                    Padding = new Thickness(5, 1, 5, 1),
                    Child = new TextBlock { Text = S.StickyTag, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Theme.B(nDim) },
                };
                DockPanel.SetDock(tag, Dock.Left);
                head.Children.Add(tag);
                head.Children.Add(new TextBlock
                {
                    Text = item.Time.ToString("MM/dd"), FontSize = 9.5,
                    Foreground = Theme.B(nDim), HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var dock = new DockPanel();
                DockPanel.SetDock(head, Dock.Top);
                dock.Children.Add(head);
                var noteBody = new TextBlock
                {
                    Text = text.Length == 0 ? S.StickyEmpty : text,
                    FontSize = 12, LineHeight = 16.5,
                    TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = Theme.B(nInk),
                    Opacity = text.Length == 0 ? 0.65 : 1,
                };
                bodyFull = noteBody.Text;
                bodyText = noteBody;
                dock.Children.Add(noteBody);
                var grid = new Grid();
                grid.Children.Add(dock);
                grid.Children.Add(new System.Windows.Shapes.Path // folded bottom-right corner
                {
                    Data = Geometry.Parse("M 0,14 L 14,0 L 14,14 Z"),
                    Fill = Theme.B(Theme.WithAlpha(nInk, 0x20)),
                    Width = 14, Height = 14,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    IsHitTestVisible = false,
                });
                return grid;
            }
            default:
            {
                frame.Background = CardBg(true);
                frame.Padding = new Thickness(11, 9, 11, 6);
                var text = (item.Text ?? "").Trim().Replace("\r\n", "\n").Replace('\t', ' ');
                if (text.Length > 300) text = text[..300];
                var paper = Note(text, string.Format(S.Chars, (item.Text ?? "").Length), Theme.B(ink), Theme.B(dim),
                    wrap: true, mono: false, rich: item.IsRich, header: null);
                CaptureBody(paper, text);
                return paper;
            }
        }
    }

    static UIElement Note(string text, string footer, Brush fg, Brush dim, bool wrap, bool mono, bool rich, UIElement? header)
    {
        var dock = new DockPanel();
        if (header != null)
        {
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);
        }
        var foot = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (rich)
        {
            var tag = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = B(0, 0, 0, 0x10),
                Padding = new Thickness(4, 0, 4, 0),
                Child = new TextBlock { Text = "Aa", FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = dim },
            };
            DockPanel.SetDock(tag, Dock.Left);
            foot.Children.Add(tag);
        }
        foot.Children.Add(new TextBlock { Text = footer, FontSize = 10, Foreground = dim, HorizontalAlignment = HorizontalAlignment.Right });
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);
        var body = new TextBlock
        {
            Text = text,
            Foreground = fg,
            FontSize = mono ? 10.5 : 12,
            LineHeight = mono ? 14 : 16.5,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (mono) body.FontFamily = new FontFamily("Consolas");
        dock.Children.Add(body);
        dock.Tag = body; // CardView uses this to paint search hits
        return dock;
    }

    static (string domain, string rest) SplitLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return (url, "");
        if (u.Scheme == "mailto") return (url[7..], "");
        var host = u.Host.StartsWith("www.") ? u.Host[4..] : u.Host;
        var rest = Uri.UnescapeDataString(u.PathAndQuery + u.Fragment);
        return (host, rest == "/" ? "" : rest);
    }

    // Border rounds its background but not its content; clip the content to match.
    static void Round(FrameworkElement e, double r) =>
        e.SizeChanged += (_, _) => e.Clip = new RectangleGeometry(new Rect(e.RenderSize), r, r);

    static void Fade(UIElement e, double to, int ms) =>
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)));

    static SolidColorBrush B(byte r, byte g, byte b, byte a = 0xFF) => new(Color.FromArgb(a, r, g, b));
    static LinearGradientBrush Horizontal(Color a, Color b, Color c) => new(new GradientStopCollection
    {
        new GradientStop(a, 0), new GradientStop(b, 0.55), new GradientStop(c, 1),
    }, 0);

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            Cancel();
            actions.Open(Item);
            return;
        }
        // Ctrl+Click picks the card for merge / sequence paste. It never starts a drag, a hold
        // or a take, and it is deliberately ignored outside the real app (Preview passes null).
        if (actions != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            actions.ToggleMulti(Item);
            Cancel();
            return;
        }
        pressed = true;
        downAt = e.GetPosition(null);
        frame.CaptureMouse();
        holdTimer.Start();
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (!pressed) return;
        var d = e.GetPosition(null) - downAt;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        Cancel();
        actions.DragOut(Item, this);
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!pressed) return;
        e.Handled = true;
        Cancel();
        actions.Primary(Item, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), this);
    }

    void OnHold(object? sender, EventArgs e)
    {
        if (!pressed) { holdTimer.Stop(); return; }
        Cancel();
        actions.Edit(Item);
    }

    void Cancel()
    {
        pressed = false;
        holdTimer.Stop();
        if (frame.IsMouseCaptured) frame.ReleaseMouseCapture();
    }
}
