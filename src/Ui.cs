using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Shengyi;

// Borderless window: rounded card with a soft shadow, a title bar you can drag, and a close button.
// Content is laid out at a fixed size and scaled down if the screen is too small for it.
class ChromeWindow : Window
{
    protected readonly Grid Body = new();

    public ChromeWindow(string title, double width, double height)
    {
        Title = "绳忆 " + title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.Font;
        ShowInTaskbar = true;

        const double margin = 16;
        var work = SystemParameters.WorkArea;
        double scale = Math.Min(1, Math.Min((work.Height - 12) / (height + 2 * margin), (work.Width - 12) / (width + 2 * margin)));
        Width = (width + 2 * margin) * scale;
        Height = (height + 2 * margin) * scale;

        var bar = new Grid { Height = 60, Background = Brushes.Transparent };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, 0, 0, 0) };
        brand.Children.Add(new TextBlock { Text = "绳忆", FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Bold, FontSize = 19, Foreground = Theme.B(Theme.Ink) });
        brand.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = Theme.B(Theme.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 3, 0, 0) });
        bar.Children.Add(brand);
        var close = new Pill(null, false)
        {
            Width = 34, Height = 34, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 20, 0),
            Child = new Path { Data = Geometry.Parse("M 0,0 L 10,10 M 10,0 L 0,10"), Stroke = Theme.B(Theme.Muted), StrokeThickness = 1.3, Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        close.Click += Cancel;
        bar.Children.Add(close);
        bar.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == bar || e.OriginalSource is TextBlock || e.OriginalSource is StackPanel) DragMove(); };

        var layout = new Grid { Width = width, Height = height };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(bar);
        Grid.SetRow(Body, 1);
        layout.Children.Add(Body);

        var card = new Border
        {
            Margin = new Thickness(margin),
            CornerRadius = new CornerRadius(22),
            Background = Theme.B(Theme.P.Window),
            BorderBrush = Theme.B(Theme.Line),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 25, ShadowDepth = 5, Opacity = 0.14, Color = Color.FromRgb(0x27, 0x34, 0x49) },
            Child = layout,
        };
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = new Grid { Width = width + 2 * margin, Height = height + 2 * margin, Children = { card } } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Cancel(); } };
    }

    protected virtual void Cancel() => Close();

    // A panel with a title, for grouping rows.
    public static Border Card(string title, params UIElement[] rows)
    {
        var stack = new StackPanel();
        stack.Children.Add(Theme.Text(title, 15, Theme.Ink, bold: true));
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) stack.Children.Add(new Border { Height = 1, Background = Theme.B(Theme.Divider) });
            stack.Children.Add(rows[i]);
        }
        return new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = Theme.B(Theme.P.Window),
            BorderBrush = Theme.B(Theme.Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 13, 20, 6),
            Child = stack,
        };
    }

    // Title and description on the left, the control on the right.
    public static Grid Row(string title, string? note, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 9, 0, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        left.Children.Add(Theme.Text(title, 13, Theme.Ink));
        if (note != null)
        {
            var n = Theme.Text(note, 11, Theme.Muted);
            n.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(n);
        }
        g.Children.Add(left);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    // Title and description above a full-width control.
    public static StackPanel Stacked(string title, string? note, UIElement control)
    {
        var s = new StackPanel { Margin = new Thickness(0, 9, 0, 10) };
        s.Children.Add(Theme.Text(title, 13, Theme.Ink));
        if (note != null)
        {
            var n = Theme.Text(note, 11, Theme.Muted);
            n.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(n);
        }
        if (control is FrameworkElement fe) fe.Margin = new Thickness(0, 10, 0, 0);
        s.Children.Add(control);
        return s;
    }
}

// Rounded button: dark and filled for the main action, plain grey text otherwise.
sealed class Pill : Border
{
    readonly TextBlock? label;
    public event Action? Click;

    public Pill(string? text, bool primary)
    {
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(18, 10, 18, 10);
        Background = primary ? Theme.B(Theme.Ink) : Brushes.Transparent;
        Cursor = Cursors.Hand;
        Focusable = true;
        if (text != null)
        {
            label = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = primary ? Theme.B(Theme.P.PrimaryInk) : Theme.B(Theme.P.ChipInk),
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Child = label;
        }
        if (!primary)
        {
            MouseEnter += (_, _) => Background = Theme.B(Theme.Surface);
            MouseLeave += (_, _) => Background = Brushes.Transparent;
        }
        else
        {
            MouseEnter += (_, _) => Opacity = 0.86;
            MouseLeave += (_, _) => Opacity = 1;
        }
        MouseLeftButtonUp += (_, e) => { e.Handled = true; Click?.Invoke(); };
        KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Click?.Invoke(); } };
    }

    public string Text { set { if (label != null) label.Text = value; } }
}

// iOS-style switch.
sealed class Toggle : Border
{
    readonly TranslateTransform knob = new();
    readonly SolidColorBrush track = new();
    bool on;
    public event Action? Changed;

    public Toggle(bool value)
    {
        Width = 40;
        Height = 24;
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(3);
        Background = track;
        Cursor = Cursors.Hand;
        Focusable = true;
        Child = new Ellipse
        {
            Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = knob,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.18 },
        };
        on = value;
        Render(false);
        MouseLeftButtonUp += (_, e) => { e.Handled = true; Flip(); };
        KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Flip(); } };
    }

    public bool IsOn => on;

    void Flip()
    {
        on = !on;
        Render(true);
        Changed?.Invoke();
    }

    void Render(bool animate)
    {
        var color = on ? Theme.P.Accent : Theme.P.TrackOff;
        double x = on ? 16 : 0;
        if (!animate) { track.Color = color; knob.X = x; return; }
        var d = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        track.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, d));
        knob.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, d) { EasingFunction = ease });
    }
}

// A row of mutually exclusive choices in one rounded track; the chosen one is a raised chip.
sealed class Segmented : Border
{
    readonly Border[] chips;
    int selected = -1;
    public event Action? Changed;

    public Segmented(int value, params string[] options)
    {
        CornerRadius = new CornerRadius(11);
        Background = Theme.B(Theme.P.Track);
        Padding = new Thickness(3);
        HorizontalAlignment = HorizontalAlignment.Left;
        var grid = new UniformGrid { Rows = 1 };
        chips = new Border[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            chips[i] = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Child = new TextBlock { Text = options[i], FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center },
            };
            chips[i].MouseLeftButtonUp += (_, e) => { e.Handled = true; Selected = index; Changed?.Invoke(); };
            grid.Children.Add(chips[i]);
        }
        Child = grid;
        Selected = value;
    }

    public int Selected
    {
        get => selected;
        set
        {
            selected = Math.Clamp(value, 0, chips.Length - 1);
            for (int i = 0; i < chips.Length; i++)
            {
                bool on = i == selected;
                chips[i].Background = on ? Theme.B(Theme.P.Chip) : Brushes.Transparent;
                chips[i].Effect = on ? new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.10 } : null;
                var t = (TextBlock)chips[i].Child;
                t.Foreground = Theme.B(on ? Theme.Ink : Theme.P.ChipInk);
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }
}

// A compact row of clickable colour dots. A null entry means "follow the theme".
sealed class Swatches : StackPanel
{
    readonly Border[] dots;
    int selected = -1;
    public event Action<int>? Changed;

    public Swatches(Color?[] colors, int value, Func<int, string> tip)
    {
        Orientation = Orientation.Horizontal;
        dots = new Border[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            int index = i;
            var dot = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip(i),
                Background = colors[i] is { } c ? Theme.B(c) : Theme.Vertical(Theme.P.PaperA, Theme.P.PaperB),
            };
            if (colors[i] == null)
                dot.Child = new TextBlock
                {
                    Text = "A", FontSize = 10, Foreground = Theme.B(Theme.P.PaperInk),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
            dot.MouseLeftButtonUp += (_, e) => { e.Handled = true; Selected = index; Changed?.Invoke(index); };
            dots[i] = dot;
            Children.Add(dot);
        }
        Selected = value;
    }

    public int Selected
    {
        get => selected;
        set
        {
            selected = Math.Clamp(value, 0, dots.Length - 1);
            for (int i = 0; i < dots.Length; i++)
                dots[i].BorderBrush = i == selected ? Theme.B(Theme.P.Accent) : Brushes.Transparent;
        }
    }

    public void ClearSelection()
    {
        selected = -1;
        foreach (var dot in dots) dot.BorderBrush = Brushes.Transparent;
    }
}

// Live miniature of the line: rope sag, card size and all colours react to the settings draft.
sealed class AppearancePreview : Border
{
    readonly Canvas canvas = new();
    readonly Path rope = new() { StrokeThickness = 1.8, IsHitTestVisible = false };
    readonly Border[] cards = new Border[3];
    readonly Border[] pegs = new Border[3];
    Settings? last;

    public AppearancePreview()
    {
        Height = 116;
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        for (int i = 0; i < 3; i++)
        {
            pegs[i] = new Border { Width = 6, Height = 14, CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
            cards[i] = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), IsHitTestVisible = false };
            canvas.Children.Add(pegs[i]);
            canvas.Children.Add(cards[i]);
        }
        canvas.Children.Add(rope);
        Child = canvas;
        SizeChanged += (_, _) => { if (last != null) Update(last); };
    }

    public void Update(Settings s)
    {
        last = s;
        var p = Theme.Resolve(s);
        Background = Theme.B(p.Surface);
        BorderBrush = Theme.B(p.Line);

        double W = ActualWidth > 0 ? ActualWidth : 316;
        const double lineTop = 12;
        double sag = Math.Clamp(s.RopeSag, 0, 48) * 0.55;
        double k = Math.Clamp(s.CardSizePercent, 70, 150) / 100.0;
        var fig = new PathFigure { StartPoint = new Point(0, lineTop) };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(W / 2, lineTop + 2 * sag), new Point(W, lineTop), true));
        rope.Data = new PathGeometry(new[] { fig });

        var scheme = CardTints.Find(s.CardTintScheme);
        var ropeRgb = scheme?.Rope ?? Theme.RgbOf(p.Rope);
        rope.Stroke = Theme.B(Theme.Wpf(ropeRgb));

        const double margin = 20;
        double pitch = Math.Max(1, (W - 2 * margin) / 3);
        // The miniature hangs Text / Link / Code cards, matching the first three tint slots.
        int[] kinds = { (int)TintKind.Text, (int)TintKind.Link, (int)TintKind.Code };
        var centres = new (double center, Rgb color)[3];
        var paints = new (Rgb top, Rgb bottom, Rgb border, Rgb ink, Rgb dim)?[3];
        for (int i = 0; i < 3; i++)
        {
            double w = Math.Min(Math.Max(24, pitch - 12), 76 * k), h = 48 * k;
            double x = margin + i * pitch + (pitch - w) / 2;
            double t = Math.Clamp((x + w / 2) / W, 0, 1);
            double y = lineTop + 4 * sag * t * (1 - t) + 8;
            if (CardTints.TintOf(s.CardTintScheme, kinds[i]) is { } tint)
            {
                paints[i] = CardTints.CardPalette(tint);
                centres[i] = (x + w / 2, tint);
            }
            else centres[i] = (x + w / 2, ropeRgb);

            var card = cards[i];
            card.Width = w;
            card.Height = h;
            var q = paints[i];
            card.Background = q is { } paint
                ? Theme.Vertical(Theme.Wpf(paint.top), Theme.Wpf(paint.bottom))
                : i switch
                {
                    0 => Theme.Vertical(p.PaperA, p.PaperB),
                    1 => Theme.B(p.CardA),
                    _ => Theme.B(p.CodeA),
                };
            card.BorderBrush = Theme.B(q is { } pb
                ? Theme.Wpf(pb.border)
                : i == 0 ? p.PaperBorder : p.CardBorder);
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            var peg = pegs[i];
            peg.Background = Theme.Peg(p.PegA, p.PegB, p.PegC);
            Canvas.SetLeft(peg, x + w / 2 - 3);
            Canvas.SetTop(peg, y - 7);
        }

        if (scheme != null && s.RopeGradient)
        {
            var stops = CardTints.RopeStops(centres, W, ropeRgb,
                Math.Clamp(s.RopeTintStrength, 0, 100) / 100.0, s.RopeGradientBanded);
            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(W, 0),
            };
            foreach (var stop in stops)
                brush.GradientStops.Add(new GradientStop(Theme.Wpf(stop.Color), stop.Offset));
            rope.Stroke = brush;
        }
    }
}

static class Fields
{
    // A borderless TextBox inside a soft rounded field that outlines itself when focused.
    public static Border Wrap(TextBox box)
    {
        box.BorderThickness = new Thickness(0);
        box.Background = Brushes.Transparent;
        box.Foreground = Theme.B(Theme.P.WindowInk);
        box.FontSize = 12.5;
        box.CaretBrush = Theme.B(Theme.Ink);
        var field = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Theme.B(Theme.Surface),
            BorderBrush = Theme.B(Theme.Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7),
            Child = box,
        };
        box.GotKeyboardFocus += (_, _) => { field.BorderBrush = Theme.B(Theme.Focus); field.Background = Theme.B(Theme.P.Window); };
        box.LostKeyboardFocus += (_, _) => { field.BorderBrush = Theme.B(Theme.Line); field.Background = Theme.B(Theme.Surface); };
        return field;
    }

    // − value + in one field. The suffix is shown after the number (e.g. "%").
    public static Border Stepper(int value, int min, int max, int step, Action<int> changed, string suffix = "")
    {
        var text = new TextBlock { Text = value + suffix, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Theme.B(Theme.Ink), MinWidth = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Border Key(string glyph, int delta)
        {
            var k = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Child = new TextBlock { Text = glyph, FontSize = 15, Foreground = Theme.B(Theme.Body), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) },
            };
            k.MouseEnter += (_, _) => k.Background = Theme.B(Theme.P.Chip);
            k.MouseLeave += (_, _) => k.Background = Brushes.Transparent;
            k.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                value = Math.Clamp(value + delta, min, max);
                text.Text = value + suffix;
                changed(value);
            };
            return k;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Key("−", -step));
        row.Children.Add(text);
        row.Children.Add(Key("+", step));
        return new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = Theme.B(Theme.Surface),
            BorderBrush = Theme.B(Theme.Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Child = row,
        };
    }

    // Editable − value + field for wide numeric ranges (the smart-order window spans
    // 1..129600 minutes). Any integer can be typed; it is clamped on Enter / focus loss.
    // The buttons step by a magnitude suited to the current value, so 5-minute tweaks and
    // day-wide jumps are both one click away. The suffix sits outside the editable text.
    public static Border NumberStepper(int value, int min, int max, Action<int> changed, string suffix)
    {
        value = Math.Clamp(value, min, max);
        var box = new TextBox
        {
            Text = value.ToString(),
            Width = 72,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.B(Theme.Ink),
            CaretBrush = Theme.B(Theme.Ink),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        // Live-update for every parseable value (clamped, but never rewriting the text
        // mid-typing). This keeps Result in sync even if Save is clicked before focus moves.
        box.TextChanged += (_, _) =>
        {
            if (!int.TryParse(box.Text, out int v)) return;
            int next = Math.Clamp(v, min, max);
            if (next == value) return;
            value = next;
            changed(value);
        };
        void Normalize()
        {
            int next = int.TryParse(box.Text, out int v) ? Math.Clamp(v, min, max) : value;
            if (next != value) { value = next; changed(value); } // e.g. typed 200000 → clamped to max
            box.Text = value.ToString();
        }
        box.LostKeyboardFocus += (_, _) => Normalize();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Normalize(); e.Handled = true; } };
        box.PreviewTextInput += (_, e) =>
        {
            // Digits only; space / letters / punctuation would otherwise need a later revert.
            foreach (char c in e.Text) if (c < '0' || c > '9') { e.Handled = true; break; }
        };

        Border StepButton(string glyph, int sign)
        {
            var k = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Child = new TextBlock { Text = glyph, FontSize = 15, Foreground = Theme.B(Theme.Body), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) },
            };
            k.MouseEnter += (_, _) => k.Background = Theme.B(Theme.P.Chip);
            k.MouseLeave += (_, _) => k.Background = Brushes.Transparent;
            k.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Normalize(); // do not discard a value the user just typed
                int step = value < 10 ? 1 : value < 60 ? 5 : value < 1440 ? 15 : value < 10080 ? 60 : 1440;
                int next = Math.Clamp(value + sign * step, min, max);
                box.Text = next.ToString();
                if (next == value) return;
                value = next;
                changed(value);
            };
            return k;
        }
        var unit = new TextBlock { Text = suffix, FontSize = 12, Foreground = Theme.B(Theme.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 4, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(StepButton("−", -1));
        row.Children.Add(box);
        row.Children.Add(unit);
        row.Children.Add(StepButton("+", 1));
        return new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = Theme.B(Theme.Surface),
            BorderBrush = Theme.B(Theme.Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Child = row,
        };
    }

    // Shortcut shown as keycaps: [Ctrl] [Alt] [V].
    public static StackPanel Keycaps(string combo, bool recording)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            row.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = Theme.B(Theme.P.Chip),
                BorderBrush = Theme.B(recording ? Theme.Focus : Theme.Line),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 5, 0),
                Child = new TextBlock { Text = part, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.B(Theme.Ink) },
            });
        }
        return row;
    }
}
