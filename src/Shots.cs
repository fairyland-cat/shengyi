using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Shengyi;

// `shengyi.exe --shots <folder>` writes the README screenshots: the line in keyboard mode over a mock desktop,
// every card style, and the settings window. Uses made-up sample clips in a temp folder and off-screen
// windows, so it never shows your own clipboard history or captures your screen.
static class Shots
{
    const double SceneWidth = 1440, SceneHeight = 560;

    public static void Run(string folder, Action done)
    {
        Directory.CreateDirectory(folder);
        ClipStore.Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shengyi-shots");
        if (Directory.Exists(ClipStore.Root)) Directory.Delete(ClipStore.Root, true);
        var settings = new Settings();
        Theme.Apply(settings); // screenshots should follow the same palette as the live app
        var store = new ClipStore(() => settings);
        Seed(store);

        var windows = new List<LineWindow>();
        var reveal = new RevealController(() => windows, () => false);
        var actions = new Actions(store, reveal, () => settings);
        LineWindow.Stage = new Size(SceneWidth, LineWindow.PanelHeight);
        var line = new LineWindow(Forms.Screen.PrimaryScreen!, store, actions, () => settings);
        windows.Add(line);
        line.Reveal(keyboard: true);

        // A second off-screen line shows the new search drawer and pinyin lookup.
        var searchLine = new LineWindow(Forms.Screen.PrimaryScreen!, store, actions, () => settings);
        windows.Add(searchLine);
        searchLine.Reveal(keyboard: true);
        searchLine.SetQuery("zhoubao"); // "周报" typed as pinyin
        searchLine.SetFiltersOpen(true);

        var prefs = new SettingsWindow(settings, true, () => { }, () => { })
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false,
        };
        prefs.Show();

        After(1800, () =>
        {
            Save(Scene(Snapshot((FrameworkElement)line.Content), line.Height), System.IO.Path.Combine(folder, "line.png"));
            Save(Scene(Snapshot((FrameworkElement)searchLine.Content), searchLine.Height, desktop: false), System.IO.Path.Combine(folder, "search.png"));
            Save((FrameworkElement)prefs.Content, System.IO.Path.Combine(folder, "settings.png"));
            line.Close();
            searchLine.Close();
            prefs.Close();
            store.Dispose();
            Preview.Render(System.IO.Path.Combine(folder, "cards.png"), done);
        });
    }

    // Added oldest first, so the list reads newest-first like real use. A few cards get fake
    // usage stamps so the screenshot shows smart ordering; one deliberately sits outside the
    // rolling window, to show that old takes do not move a card forward.
    static void Seed(ClipStore store)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        void Used(ClipItem item, int times, int hoursAgo)
        {
            var stamps = new List<long>();
            for (int k = times; k >= 1; k--) stamps.Add(now - (hoursAgo + k) * 3600_000L);
            item.UseTimes = stamps; // oldest first
            item.TotalUses = times;
        }
        ClipItem Text(string t, bool pin = false, int uses = 0, int hoursAgo = 2, string? app = null)
        {
            var item = new ClipItem { Kind = Classify.Text(t), Text = t, Hash = Classify.HashText(t), SourceApp = app };
            store.Add(item);
            if (pin) store.SetPinned(item, true);
            if (uses > 0) Used(item, uses, hoursAgo);
            return item;
        }
        Text("绳忆\nshengyi\ngithub.com/fairyland-cat/shengyi", pin: true);
        Text("#25282D", uses: 9, hoursAgo: 10 * 24); // 10 days old: outside the 7-day window
        Text("https://github.com/fairyland-cat/shengyi", uses: 5, hoursAgo: 3, app: "chrome");
        var shot = Picture(DrawScreenshot);
        store.Add(new ClipItem { Kind = ClipKind.Image, Hash = ImageHash.Of(shot), SourceApp = "snippingtool" }, shot);
        Text("const clips = history.filter(c => c.pinned)\n    .map(c => c.text);\nconsole.log(clips.length);", uses: 3, hoursAgo: 5, app: "code");
        var files = new List<string> { @"C:\Windows\notepad.exe" };
        store.Add(new ClipItem { Kind = ClipKind.Files, Files = files, Hash = Classify.HashFiles(files), SourceApp = "explorer" });
        Text("rgb(52, 211, 153)", app: "figma");
        var photo = Picture(DrawLandscape);
        store.Add(new ClipItem { Kind = ClipKind.Image, Hash = ImageHash.Of(photo), SourceApp = "snippingtool" }, photo);
        Text("周报：本周完成了剪贴板历史的搜索、固定和卡片样式，下周开始做安装包和使用文档。", uses: 8, app: "notepad");
        Text("下周一上午十点，三楼会议室，讨论第四季度发布计划。", uses: 1, app: "outlook");
        void Note(string t, string color) =>
            store.AddSticky(new ClipItem { Kind = ClipKind.Sticky, Text = t, NoteColor = color });
        Note("待办：\n· 预约体检\n· 回复设计稿", "#DDEBFA");
        Note("周五前交周报\n下午三点设计评审\n记得买咖啡豆", "#FFF3B0");
    }

    // A mock desktop: soft wallpaper, a document window you'd paste into, and the line across the top.
    // The search screenshot leaves out the document window so the filter drawer stays legible.
    static FrameworkElement Scene(BitmapSource lineImage, double lineHeight, bool desktop = true)
    {
        var root = new Canvas { Width = SceneWidth, Height = SceneHeight, ClipToBounds = true };
        root.Background = new LinearGradientBrush(Color.FromRgb(0xE4, 0xEB, 0xF5), Color.FromRgb(0xF1, 0xEA, 0xF3), 35);
        foreach (var (x, y, r, c) in new[] { (180.0, 420.0, 260.0, Color.FromArgb(0x55, 0xBF, 0xD4, 0xF5)), (1250.0, 120.0, 300.0, Color.FromArgb(0x50, 0xF5, 0xD0, 0xE4)) })
        {
            var blob = new System.Windows.Shapes.Ellipse { Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(c), Effect = new BlurEffect { Radius = 80 } };
            Canvas.SetLeft(blob, x - r);
            Canvas.SetTop(blob, y - r);
            root.Children.Add(blob);
        }

        if (desktop)
        {
            var doc = new StackPanel { Margin = new Thickness(40, 30, 40, 0) };
            // The mock document is always white, so its ink must not follow the dark theme.
            doc.Children.Add(Theme.Text("第四季度发布计划", 22, Color.FromRgb(0x25, 0x28, 0x2D), bold: true));
            foreach (var w in new[] { 0.92, 0.85, 0.6 })
                doc.Children.Add(new Border { Height = 9, CornerRadius = new CornerRadius(4), Background = Theme.B(Color.FromRgb(0xE6, 0xEA, 0xF0)), Width = 720 * w, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 0) });
            var caret = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            caret.Children.Add(Theme.Text("会议时间：", 15, Color.FromRgb(0x5B, 0x66, 0x75)));
            caret.Children.Add(new Border { Width = 2, Height = 20, Background = Theme.B(Color.FromRgb(0x3B, 0x82, 0xF6)) });
            doc.Children.Add(caret);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 14, 0, 0) };
            foreach (var c in new[] { Color.FromRgb(0xF8, 0x71, 0x71), Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0x34, 0xD3, 0x99) })
                bar.Children.Add(new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 7, 0) });
            var window = new Border
            {
                Width = 900, Height = 300, CornerRadius = new CornerRadius(14), Background = Brushes.White,
                BorderBrush = Theme.B(Color.FromRgb(0xE9, 0xED, 0xF2)), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.12, Color = Color.FromRgb(0x27, 0x34, 0x49) },
                Child = new StackPanel { Children = { bar, doc } },
            };
            Canvas.SetLeft(window, (SceneWidth - 900) / 2);
            Canvas.SetTop(window, 246);
            root.Children.Add(window);
        }

        var lineView = new Image { Source = lineImage, Width = SceneWidth, Height = lineHeight };
        root.Children.Add(lineView);
        root.Measure(new Size(SceneWidth, SceneHeight));
        root.Arrange(new Rect(0, 0, SceneWidth, SceneHeight));
        return root;
    }

    static BitmapSource Snapshot(FrameworkElement e)
    {
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(e.ActualWidth * 2), (int)Math.Ceiling(e.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        rtb.Render(e);
        rtb.Freeze();
        return rtb;
    }

    static void Save(FrameworkElement e, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Snapshot(e)));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    static void After(int ms, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); action(); };
        t.Start();
    }

    static BitmapSource Picture(Action<DrawingContext> draw)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) draw(dc);
        var bmp = new RenderTargetBitmap(480, 300, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    static void DrawScreenshot(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF9)), null, new Rect(0, 0, 480, 300));
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 480, 34));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x2D)), null, new Rect(0, 34, 96, 266));
        var blue = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        var soft = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEB));
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(116, 54, 344, 112), 10, 10);
        for (int i = 0; i < 6; i++) dc.DrawRoundedRectangle(i == 3 ? blue : soft, null, new Rect(140 + i * 50, 140 - 14 * (i % 3 + 2), 30, 14 * (i % 3 + 2)), 4, 4);
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(116, 182, 164, 98), 10, 10);
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(296, 182, 164, 98), 10, 10);
        dc.DrawRoundedRectangle(soft, null, new Rect(134, 204, 110, 10), 5, 5);
        dc.DrawRoundedRectangle(blue, null, new Rect(314, 204, 70, 10), 5, 5);
    }

    static void DrawLandscape(DrawingContext dc)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xFD, 0xBA, 0x74), Color.FromRgb(0xF4, 0x72, 0xB6), 90), null, new Rect(0, 0, 480, 300));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xF7, 0xED)), null, new Point(330, 120), 46, 46);
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)), null, Geometry.Parse("M 0,300 L 120,170 L 210,240 L 320,150 L 480,300 Z"));
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x4C, 0x1D, 0x95)), null, Geometry.Parse("M 0,300 L 90,240 L 200,280 L 330,220 L 480,300 Z"));
    }
}
