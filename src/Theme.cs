using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shengyi;

// Every colour used by the line, the cards and 绳忆's own windows. A Settings snapshot selects a
// base palette, an accent, and optionally a custom card colour; anything not overridden inherits
// from the base palette.
sealed class ThemePalette
{
    public Color Rope;
    public Color PegA, PegB, PegC, PegPinA, PegPinB, PegPinC;
    public Color PaperA, PaperB, PaperBorder, PaperInk, PaperDim;
    public Color CardA, CardB, CardBorder, CardInk, CardDim;
    public Color CodeA, CodeB, CodeBorder, CodeInk, CodeDim;
    public Color Tile, Keycap, KeycapInk, LinkIconBg, LinkInk;
    public Color Accent;
    public Color Badge, Close, Panel, Hint, PanelInk, TabOn, TabOnInk, TabOffInk;
    public Color Ink, Body, Muted, Faint, Line, Divider, Surface, Focus;
    public Color Window, WindowInk, PrimaryInk, Track, TrackOff, Chip, ChipInk;
    public bool Dark;

    public ThemePalette Copy() => (ThemePalette)MemberwiseClone();
}

static class Theme
{
    public static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");
    public static readonly string[] Names = { "soft", "silver", "linen", "porcelain", "graphite" };
    public static readonly string[] AccentNames = { "blue", "terra", "mint", "navy" };
    public static readonly string[] CardPresets =
    {
        "auto", "#FFF4C2", "#F5F5F3", "#E8F1FB", "#E6F6EC", "#FCE8E8", "#3A3F47", "#FFFFFF",
    };
    // Paper colours for sticky notes. "auto" uses the active theme's paper instead.
    public static readonly string[] NotePresets =
    {
        "auto", "#FFF3B0", "#FFE0D6", "#DDEBFA", "#DEF3E4", "#EDE4FA", "#F5F5F2", "#3A4048",
    };

    static readonly Dictionary<string, ThemePalette> palettes = BuildPalettes();
    static readonly Dictionary<uint, SolidColorBrush> brushes = new();
    static ThemePalette current = palettes[Names[0]];
    static Settings snapshot = new();

    public static ThemePalette P => current;
    public static event Action? Changed;

    // Kept as properties so the existing Theme.Ink / Theme.Line call sites keep working.
    public static Color Ink => current.Ink;
    public static Color Body => current.Body;
    public static Color Muted => current.Muted;
    public static Color Faint => current.Faint;
    public static Color Line => current.Line;
    public static Color Divider => current.Divider;
    public static Color Surface => current.Surface;
    public static Color Focus => current.Focus;

    public static double CardScale => Math.Clamp(snapshot.CardSizePercent, 70, 150) / 100.0;
    public static double RopeSag => Math.Clamp(snapshot.RopeSag, 0, 48);

    // Also snapshotted here so the line window and every card read the same live values.
    // Apply() always raises Changed, so saving settings re-sorts the cards immediately.
    public static int FreqThreshold => Math.Clamp(snapshot.FreqThreshold, 0, 20);
    public static int FreqWindowMinutes => FreqWindow.Clamp(snapshot.FreqWindowMinutes);

    // ---- 0.7.0 · content-tinted cards ---------------------------------------
    // The tint scheme lives in Settings but every card / line reads it through Theme, so
    // Theme.Changed (raised by Apply) repaints everything after a settings save.
    public static Color Wpf(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
    public static Rgb RgbOf(Color c) => new(c.R, c.G, c.B);

    public static string SchemeId => string.IsNullOrWhiteSpace(snapshot.CardTintScheme)
        ? CardTints.Off : snapshot.CardTintScheme!;
    public static bool TintsOn => CardTints.Find(SchemeId) != null;
    public static Rgb RopeBase => CardTints.RopeOf(SchemeId, RgbOf(P.Rope));
    public static bool RopeGradientOn => snapshot.RopeGradient && TintsOn;
    public static double RopeStrength => Math.Clamp(snapshot.RopeTintStrength, 0, 100) / 100.0;
    public static bool RopeBanded => snapshot.RopeGradientBanded;
    public static bool FineGrainedTints => snapshot.FineGrainedFileTints;

    public readonly record struct CardPaintColors(Color Top, Color Bottom, Color Border, Color Ink, Color Dim);

    /// <summary>The colour a card contributes to its own paint and to the rope, or null.</summary>
    public static Rgb? TintOf(ClipItem item)
    {
        if (item.IsSticky && !snapshot.TintStickyNotes) return null;
        return CardTints.TintOf(SchemeId, (int)item.Kind, item.FileCategory, snapshot.FineGrainedFileTints);
    }

    /// <summary>
    /// Colours for one card, or null when it must keep the classic theme colours.
    /// 色值卡 / 图片卡 always return null; sticky notes return null unless the user opted in.
    /// Rich colours carried by HTML / RTF are honoured but forced through a WCAG contrast check.
    /// </summary>
    public static CardPaintColors? CardPaint(ClipItem item)
    {
        if (TintOf(item) is not { } tint) return null;
        var rich = item.IsRich ? CardTints.ExtractRichColors(item.Html, item.Rtf) : default;
        var pal = CardTints.CardPalette(tint, rich.Foreground, rich.Background);
        return new CardPaintColors(Wpf(pal.top), Wpf(pal.bottom), Wpf(pal.border), Wpf(pal.ink), Wpf(pal.dim));
    }

    public static void Apply(Settings s)
    {
        snapshot = s;
        current = Resolve(s);
        Changed?.Invoke();
    }

    // Computes a palette without touching the live one; the settings preview uses this.
    public static ThemePalette Resolve(Settings s)
    {
        string name = Array.IndexOf(Names, s.ThemeName ?? "") >= 0 ? s.ThemeName! : Names[0];
        var p = palettes[name].Copy();
        p.Accent = AccentColor(s.Accent);
        if (ParseHex(s.CardColor) is { } custom) ApplyCardColor(p, custom);
        return p;
    }

    public static Color AccentColor(string? name) => name switch
    {
        "terra" => C(0xD9, 0x77, 0x57),
        "mint" => C(0x3E, 0xA8, 0x73),
        "navy" => C(0x33, 0x52, 0xA8),
        _ => C(0x3B, 0x82, 0xF6),
    };

    // A sticky note can override just its own paper; everything else is derived from that colour
    // so text stays readable on both pale yellow and dark graphite paper.
    public static Color NoteBase(string? hex) => ParseHex(hex) ?? Mix(P.PaperA, P.PaperB, 0.5);

    public static (Color top, Color bottom, Color border, Color ink, Color dim) NotePalette(string? hex)
    {
        var b = NoteBase(hex);
        bool light = Luma(b) > 140;
        return (
            Mix(b, Colors.White, 0.42),
            b,
            WithAlpha(Mix(b, Colors.Black, 0.32), 0x70),
            light ? C(0x3B, 0x35, 0x2C) : C(0xF3, 0xF1, 0xEA),
            light ? C(0x8A, 0x7C, 0x62) : C(0xC8, 0xC2, 0xB6));
    }

    public static SolidColorBrush B(Color c)
    {
        uint key = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        if (!brushes.TryGetValue(key, out var b))
        {
            b = new SolidColorBrush(c);
            b.Freeze();
            brushes[key] = b;
        }
        return b;
    }

    public static TextBlock Text(string text, double size, Color color, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = B(color),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
    };

    public static LinearGradientBrush Vertical(Color top, Color bottom) => new(top, bottom, 90);

    public static LinearGradientBrush Peg(Color a, Color b, Color c) => new(new GradientStopCollection
    {
        new GradientStop(a, 0), new GradientStop(b, 0.55), new GradientStop(c, 1),
    }, 0);

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t),
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static double Luma(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color? ParseHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
        string t = s.Trim().TrimStart('#');
        if (t.Length != 6) return null;
        if (!byte.TryParse(t[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(t[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(t[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)) return null;
        return Color.FromRgb(r, g, b);
    }

    static void ApplyCardColor(ThemePalette p, Color c)
    {
        p.PaperA = Mix(c, Colors.White, 0.55);
        p.PaperB = Mix(c, Colors.White, 0.12);
        p.PaperBorder = WithAlpha(Mix(c, Colors.Black, 0.30), 0x60);
        p.CardA = Mix(c, Colors.White, 0.68);
        p.CardB = Mix(c, Colors.White, 0.28);
        p.CardBorder = WithAlpha(Mix(c, Colors.Black, 0.25), 0x40);
        p.Tile = Mix(c, Colors.White, 0.82);
        p.Keycap = Mix(c, Colors.White, 0.75);
        bool light = Luma(p.PaperB) > 140;
        p.PaperInk = light ? C(0x33, 0x30, 0x2C) : C(0xF5, 0xF3, 0xEE);
        p.PaperDim = light ? C(0x8A, 0x7C, 0x62) : C(0xCC, 0xC6, 0xBA);
        p.CardInk = light ? C(0x1B, 0x24, 0x33) : C(0xF2, 0xF4, 0xF8);
        p.CardDim = light ? C(0x64, 0x74, 0x8B) : C(0xB8, 0xC0, 0xCC);
        p.KeycapInk = p.CardInk;
        p.LinkIconBg = Mix(c, Colors.White, 0.45);
        p.LinkInk = Mix(p.Accent, p.CardInk, 0.35);
    }

    static Color C(int r, int g, int b, byte a = 0xFF) => Color.FromArgb(a, (byte)r, (byte)g, (byte)b);

    static Dictionary<string, ThemePalette> BuildPalettes()
    {
        // "soft" is the original 绳忆 look; every value below is the old hard-coded colour.
        var soft = new ThemePalette
        {
            Rope = C(0x8A, 0x76, 0x5E),
            PegA = C(0xEE, 0xD9, 0xB6), PegB = C(0xCB, 0xA4, 0x74), PegC = C(0xA9, 0x82, 0x55),
            PegPinA = C(0xFC, 0x8A, 0x8A), PegPinB = C(0xDC, 0x26, 0x26), PegPinC = C(0xB9, 0x1C, 0x1C),
            PaperA = C(0xFF, 0xFB, 0xEB), PaperB = C(0xFE, 0xF0, 0xBE), PaperBorder = C(0xD9, 0xA4, 0x41, 0x40),
            PaperInk = C(0x44, 0x40, 0x3C), PaperDim = C(0xA8, 0x94, 0x6A),
            CardA = Colors.White, CardB = C(0xEC, 0xF0, 0xF5), CardBorder = C(0, 0, 0, 0x1C),
            CardInk = C(0x0F, 0x17, 0x2A), CardDim = C(0x64, 0x74, 0x8B),
            CodeA = C(0x1F, 0x24, 0x30), CodeB = C(0x1F, 0x29, 0x37), CodeBorder = C(0xFF, 0xFF, 0xFF, 0x14),
            CodeInk = C(0xE2, 0xE8, 0xF0), CodeDim = C(0x64, 0x74, 0x8B),
            Tile = Colors.White, Keycap = C(0xFF, 0xFF, 0xFF, 0xF2), KeycapInk = C(0x33, 0x41, 0x55),
            LinkIconBg = C(0xEF, 0xF6, 0xFF), LinkInk = C(0x1D, 0x4E, 0xD8),
            Accent = C(0x3B, 0x82, 0xF6),
            Badge = C(0x11, 0x18, 0x27, 0xE6), Close = C(0x11, 0x18, 0x27, 0xEE),
            Panel = C(0x20, 0x20, 0x20, 0xE0), Hint = C(0x20, 0x20, 0x20, 0xA0),
            PanelInk = Colors.White, TabOn = Colors.White, TabOnInk = C(0x11, 0x18, 0x27),
            TabOffInk = C(0xFF, 0xFF, 0xFF, 0xD0),
            Ink = C(0x25, 0x28, 0x2D), Body = C(0x5B, 0x66, 0x75), Muted = C(0x92, 0x9C, 0xAB),
            Faint = C(0xAD, 0xB5, 0xC0), Line = C(0xE9, 0xED, 0xF2), Divider = C(0xF1, 0xF3, 0xF6),
            Surface = C(0xF7, 0xF8, 0xFA), Focus = C(0x8C, 0xAB, 0xC8),
            Window = Colors.White, WindowInk = C(0x25, 0x28, 0x2D), PrimaryInk = Colors.White,
            Track = C(0xF0, 0xF2, 0xF5), TrackOff = C(0xDC, 0xE1, 0xE8),
            Chip = Colors.White, ChipInk = C(0x6B, 0x74, 0x80),
            Dark = false,
        };

        var silver = soft.Copy();
        silver.Rope = C(0x9A, 0xA0, 0xA8);
        silver.PegA = C(0xF2, 0xF3, 0xF5); silver.PegB = C(0xB4, 0xB9, 0xC0); silver.PegC = C(0x8E, 0x94, 0x9B);
        silver.PaperA = Colors.White; silver.PaperB = C(0xE8, 0xEB, 0xF0); silver.PaperBorder = C(0x9A, 0xA0, 0xA8, 0x50);
        silver.PaperInk = C(0x2B, 0x34, 0x40); silver.PaperDim = C(0x7C, 0x87, 0x97);
        silver.CardB = C(0xE6, 0xEA, 0xF0); silver.LinkIconBg = C(0xEE, 0xF2, 0xF8); silver.LinkInk = C(0x24, 0x50, 0x7F);
        silver.Ink = C(0x2A, 0x30, 0x38); silver.Line = C(0xE2, 0xE6, 0xEC); silver.Surface = C(0xF4, 0xF6, 0xF9);
        silver.WindowInk = silver.Ink;

        var linen = soft.Copy();
        linen.Rope = C(0xB8, 0xA0, 0x7A);
        linen.PegA = C(0xEA, 0xD8, 0xB2); linen.PegB = C(0xC3, 0xA5, 0x70); linen.PegC = C(0x9C, 0x7B, 0x47);
        linen.PaperA = C(0xFB, 0xF3, 0xE2); linen.PaperB = C(0xF0, 0xDF, 0xB9); linen.PaperBorder = C(0xC8, 0xA9, 0x6A, 0x50);
        linen.PaperInk = C(0x4A, 0x40, 0x32); linen.PaperDim = C(0xA0, 0x8B, 0x62);
        linen.CardA = C(0xFF, 0xFC, 0xF4); linen.CardB = C(0xED, 0xE2, 0xCD); linen.Tile = C(0xFF, 0xFC, 0xF4);
        linen.LinkIconBg = C(0xF5, 0xEF, 0xE0); linen.LinkInk = C(0x7A, 0x5A, 0x2A);
        linen.Line = C(0xEC, 0xE4, 0xD5); linen.Surface = C(0xFA, 0xF6, 0xEE); linen.Divider = C(0xF3, 0xED, 0xE2);

        var porcelain = soft.Copy();
        porcelain.Rope = C(0xC9, 0xCB, 0xD0);
        porcelain.PegA = Colors.White; porcelain.PegB = C(0xE4, 0xE4, 0xE8); porcelain.PegC = C(0xB9, 0xBB, 0xC2);
        porcelain.PaperA = Colors.White; porcelain.PaperB = C(0xF2, 0xF2, 0xF6); porcelain.PaperBorder = C(0xC9, 0xCB, 0xD0, 0x60);
        porcelain.PaperInk = C(0x33, 0x36, 0x3C); porcelain.PaperDim = C(0x9A, 0x9D, 0xA6);
        porcelain.CardB = C(0xF0, 0xF0, 0xF4); porcelain.LinkIconBg = C(0xF2, 0xF3, 0xF7); porcelain.LinkInk = C(0x4A, 0x55, 0x70);
        porcelain.Line = C(0xE8, 0xE9, 0xED); porcelain.Surface = C(0xF8, 0xF8, 0xFA);

        var graphite = soft.Copy();
        graphite.Dark = true;
        graphite.Rope = C(0x8A, 0x90, 0x9A);
        graphite.PegA = C(0x6B, 0x70, 0x79); graphite.PegB = C(0x4A, 0x4E, 0x56); graphite.PegC = C(0x2E, 0x32, 0x3A);
        graphite.PaperA = C(0x3A, 0x3F, 0x47); graphite.PaperB = C(0x2B, 0x30, 0x38); graphite.PaperBorder = C(0x55, 0x5B, 0x65);
        graphite.PaperInk = C(0xEC, 0xEF, 0xF3); graphite.PaperDim = C(0xA8, 0xB0, 0xBD);
        graphite.CardA = C(0x34, 0x39, 0x41); graphite.CardB = C(0x29, 0x2E, 0x35); graphite.CardBorder = C(0x55, 0x5B, 0x65);
        graphite.CardInk = C(0xE8, 0xEB, 0xF0); graphite.CardDim = C(0xA0, 0xAA, 0xB8);
        graphite.CodeA = C(0x1B, 0x1F, 0x26); graphite.CodeB = C(0x19, 0x1D, 0x23);
        graphite.CodeInk = C(0xD7, 0xDE, 0xE8); graphite.CodeDim = C(0x7E, 0x88, 0x98);
        graphite.Tile = C(0x45, 0x4B, 0x55); graphite.Keycap = C(0x2A, 0x30, 0x38); graphite.KeycapInk = C(0xE8, 0xEB, 0xF0);
        graphite.LinkIconBg = C(0x2C, 0x39, 0x4B); graphite.LinkInk = C(0x8F, 0xB8, 0xEA);
        graphite.Badge = C(0x0B, 0x0E, 0x12, 0xE6); graphite.Close = C(0x0B, 0x0E, 0x12, 0xEE);
        graphite.Panel = C(0x10, 0x13, 0x18, 0xE0); graphite.Hint = C(0x10, 0x13, 0x18, 0xA0);
        graphite.TabOn = C(0xE8, 0xEB, 0xF0); graphite.TabOnInk = C(0x1A, 0x1E, 0x24); graphite.TabOffInk = C(0xFF, 0xFF, 0xFF, 0xC0);
        graphite.Ink = C(0xEC, 0xEF, 0xF3); graphite.Body = C(0xB8, 0xC0, 0xCC); graphite.Muted = C(0x8E, 0x97, 0xA5);
        graphite.Faint = C(0x6F, 0x78, 0x86); graphite.Line = C(0x3A, 0x40, 0x48); graphite.Divider = C(0x2F, 0x35, 0x3D);
        graphite.Surface = C(0x23, 0x28, 0x2F); graphite.Focus = C(0x5B, 0x8A, 0xC9);
        graphite.Window = C(0x1E, 0x22, 0x28); graphite.WindowInk = C(0xEC, 0xEF, 0xF3); graphite.PrimaryInk = C(0x1A, 0x1E, 0x24);
        graphite.Track = C(0x2A, 0x30, 0x38); graphite.TrackOff = C(0x4A, 0x50, 0x58);
        graphite.Chip = C(0x3A, 0x41, 0x4A); graphite.ChipInk = C(0xA8, 0xB0, 0xBD);

        return new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase)
        {
            ["soft"] = soft, ["silver"] = silver, ["linen"] = linen,
            ["porcelain"] = porcelain, ["graphite"] = graphite,
        };
    }
}
