using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shengyi;

// ============================================================================
// Content-tinted cards + spliced rope gradient.
//
// Pure logic on purpose: no WPF / WinForms reference, so _verify (a plain net8.0
// console app) compiles this exact file and exercises it. All WPF bridging lives
// in Theme.cs / CardView.cs / LineWindow.cs.
//
// Rules settled in 0.7.0:
//   * 色值卡 (ClipKind.Color) and 图片卡 (ClipKind.Image) are NEVER tinted: their
//     frame keeps the active theme's colours and they contribute no rope stop.
//     Their real content (the colour chip / the photo) is never recoloured either.
//   * 文字 / 链接 / 代码 may carry their own colours from HTML / RTF. Those colours
//     are honoured on the card but pushed toward black or white until they reach a
//     measured WCAG contrast of 4.5:1 against BOTH ends of the card's gradient.
// ============================================================================

/// <summary>An 8-bit RGB colour that does not depend on System.Windows.Media.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb DarkInk = new(0x1B, 0x24, 0x33);
    public static readonly Rgb LightInk = new(0xF2, 0xF4, 0xF8);

    public static Rgb FromHex(string hex)
    {
        string t = hex.Trim().TrimStart('#');
        if (t.Length != 6) throw new ArgumentException("expected #RRGGBB", nameof(hex));
        return new Rgb(
            byte.Parse(t[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(t[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(t[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public double Luma => 0.299 * R + 0.587 * G + 0.114 * B;

    public Rgb Mix(Rgb other, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(R + (other.R - R) * t),
            (byte)Math.Round(G + (other.G - G) * t),
            (byte)Math.Round(B + (other.B - B) * t));
    }

    static double Linear(byte v)
    {
        double s = v / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    /// <summary>WCAG 2.x contrast ratio. Body text is kept at >= 4.5:1.</summary>
    public double Contrast(Rgb other)
    {
        double Rel(Rgb c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        double a = Rel(this), b = Rel(other);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    public double Distance(Rgb other) => Math.Sqrt(
        (R - other.R) * (R - other.R) + (G - other.G) * (G - other.G) + (B - other.B) * (B - other.B));
}

/// <summary>Same numeric order as ClipKind: Text, Link, Code, Color, Image, Files, Sticky.</summary>
public enum TintKind { Text = 0, Link = 1, Code = 2, Color = 3, Image = 4, Files = 5, Sticky = 6 }

/// <summary>The colours a pasted clip brought along with it (HTML CSS or RTF colour table).</summary>
public readonly record struct RichColors(Rgb? Foreground, Rgb? Background);

public sealed class TintScheme
{
    public required string Id { get; init; }
    public required string Zh { get; init; }
    public required string En { get; init; }
    public required bool Dark { get; init; }
    public required Rgb Rope { get; init; }
    /// <summary>Five colours, indexed by TintIndex: Text / Link / Code / Files / Sticky.</summary>
    public required Rgb[] Colors { get; init; }
}

public readonly record struct RopeStop(double Offset, Rgb Color);

public static class CardTints
{
    /// <summary>"off" keeps today's single-colour cards and the plain theme rope.</summary>
    public const string Off = "off";
    public const int KindCount = 5;

    static TintScheme Make(string id, string zh, string en, bool dark, string rope, params string[] colors)
    {
        if (colors.Length != KindCount) throw new ArgumentException("a scheme needs exactly 5 colours", id);
        return new TintScheme
        {
            Id = id, Zh = zh, En = en, Dark = dark, Rope = Rgb.FromHex(rope),
            Colors = colors.Select(Rgb.FromHex).ToArray(),
        };
    }

    // Colour order: Text / Link / Code / Files / Sticky.
    // 色值卡与图片卡没有条目：它们保留主题原色，也不参与绳子渐变。
    // Every scheme keeps the semantic anchors (link=blue-ish, code=dark, sticky=yellow-ish)
    // so switching palettes never forces the user to re-learn the mapping.
    public static readonly TintScheme[] Schemes =
    {
        Make("paper-dawn", "纸笺晨光", "Paper dawn", false, "#B79A72",
            "#FFF6E5", "#DBE8F6", "#4A4640", "#F6E3CB", "#FFF0B0"),
        Make("silver-cool", "银灰冷调", "Silver cool", false, "#9AA0A8",
            "#F5F8FC", "#C9DDF6", "#2F3742", "#E7E3D8", "#F2EBC4"),
        Make("linen-earth", "亚麻大地", "Linen earth", false, "#B8A07A",
            "#FBF3E2", "#D9E5DA", "#4A4234", "#E3CEA6", "#F1E0A2"),
        Make("blue-white", "青花瓷", "Blue and white", false, "#7A94B8",
            "#FFFFFF", "#2E6FB7", "#243B5A", "#D6E4F2", "#F1F3E6"),
        Make("macaron", "马卡龙", "Macaron", false, "#C7A3C4",
            "#FFF2F6", "#DDE9FF", "#B7A4E6", "#FFE7C6", "#FFF5AC"),
        Make("morandi", "莫兰迪", "Morandi", false, "#A9A29A",
            "#EDE9E2", "#C8D2D9", "#6E7377", "#D5C6B6", "#DCD2AE"),
        Make("neon-night", "霓虹暗夜", "Neon night", true, "#8A909A",
            "#3B2B6B", "#1D4FA8", "#191B33", "#A45A16", "#8A7616"),
        Make("terminal", "终端极客", "Terminal", true, "#6B7280",
            "#23313B", "#1D4A6E", "#0F171E", "#4A3A1C", "#6B5E22"),
        Make("accessible", "无障碍高对比", "Accessible", false, "#5A5A5A",
            "#FFFFFF", "#0072B2", "#000000", "#E69F00", "#F0E442"),
        Make("spectrum", "光谱彩虹", "Spectrum", false, "#8C8C9A",
            "#F94144", "#3B82F6", "#6D28D9", "#F59E0B", "#FDE047"),
    };

    public static TintScheme? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) || id == Off
            ? null
            : Schemes.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Rope base for a scheme; "off" / unknown ids fall back to the active theme's Rope.</summary>
    public static Rgb RopeOf(string? schemeId, Rgb themeRope) => Find(schemeId)?.Rope ?? themeRope;

    /// <summary>
    /// Maps ClipKind to a colour slot. 色值卡 (3) and 图片卡 (4) deliberately have no slot:
    /// they stay in the theme's colours and are skipped by the rope gradient.
    /// </summary>
    public static int? TintIndex(int kind) => kind switch
    {
        (int)TintKind.Text => 0,
        (int)TintKind.Link => 1,
        (int)TintKind.Code => 2,
        (int)TintKind.Files => 3,
        (int)TintKind.Sticky => 4,
        _ => null,
    };

    public static bool IsUntinted(int kind) => TintIndex(kind) is null;

    /// <summary>
    /// The colour a card contributes, or null when this card must not be tinted.
    /// kind is (int)ClipKind. With fineGrained on, a Files card is tinted by its
    /// FileCategory instead of the generic Files colour.
    /// </summary>
    public static Rgb? TintOf(string? schemeId, int kind, string? fileCategory = null, bool fineGrained = false)
    {
        var scheme = Find(schemeId);
        if (scheme is null || TintIndex(kind) is not { } index) return null;
        var tint = scheme.Colors[index];
        if (fineGrained && (TintKind)kind == TintKind.Files && !string.IsNullOrEmpty(fileCategory))
            tint = FileTint(tint, fileCategory);
        return tint;
    }

    // Optional extension: media / archive / document / picture / folder / other.
    static readonly Dictionary<string, Rgb> CategoryHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["media"] = Rgb.FromHex("#8B5CF6"),
        ["archive"] = Rgb.FromHex("#F59E0B"),
        ["document"] = Rgb.FromHex("#3B82F6"),
        ["picture"] = Rgb.FromHex("#10B981"),
        ["folder"] = Rgb.FromHex("#94A3B8"),
        ["other"] = Rgb.FromHex("#78716C"),
    };

    public static Rgb FileTint(Rgb files, string? category, double weight = 0.35) =>
        category != null && CategoryHints.TryGetValue(category, out var hint)
            ? files.Mix(hint, Math.Clamp(weight, 0, 1))
            : files;

    // ------------------------------------------------------------------
    // Pasted content that carries its own colours.
    // ------------------------------------------------------------------

    static readonly Dictionary<string, Rgb> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new Rgb(0, 0, 0), ["white"] = new Rgb(255, 255, 255),
        ["red"] = new Rgb(255, 0, 0), ["green"] = new Rgb(0, 128, 0),
        ["blue"] = new Rgb(0, 0, 255), ["yellow"] = new Rgb(255, 255, 0),
        ["orange"] = new Rgb(255, 165, 0), ["purple"] = new Rgb(128, 0, 128),
        ["gray"] = new Rgb(128, 128, 128), ["grey"] = new Rgb(128, 128, 128),
        ["silver"] = new Rgb(192, 192, 192), ["maroon"] = new Rgb(128, 0, 0),
        ["teal"] = new Rgb(0, 128, 128), ["navy"] = new Rgb(0, 0, 128),
        ["lime"] = new Rgb(0, 255, 0), ["cyan"] = new Rgb(0, 255, 255),
        ["magenta"] = new Rgb(255, 0, 255), ["fuchsia"] = new Rgb(255, 0, 255),
    };

    /// <summary>Parses #rgb / #rrggbb / #rrggbbaa / rgb() / rgba() / a common CSS name.</summary>
    public static Rgb? ParseColorValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string v = raw.Trim().TrimEnd(';', ' ', '"', '\'').ToLowerInvariant();
        if (v.StartsWith('#'))
        {
            string hex = v[1..];
            if (hex.Length == 3 && hex.All(Uri.IsHexDigit))
                return new Rgb((byte)(Convert.ToInt32(hex[..1], 16) * 17),
                               (byte)(Convert.ToInt32(hex[1..2], 16) * 17),
                               (byte)(Convert.ToInt32(hex[2..3], 16) * 17));
            if (hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit))
                return new Rgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
            return null;
        }
        var m = Regex.Match(v, @"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            byte Clamp(string s) => (byte)Math.Clamp(int.Parse(s, CultureInfo.InvariantCulture), 0, 255);
            return new Rgb(Clamp(m.Groups[1].Value), Clamp(m.Groups[2].Value), Clamp(m.Groups[3].Value));
        }
        return NamedColors.TryGetValue(v, out var named) ? named : null;
    }

    static Rgb? CssColor(string html, string property)
    {
        // Strip <style> blocks: browser clipboard HTML carries whole stylesheets whose first
        // colour has nothing to do with the text the user actually selected.
        string body = Regex.Replace(html, @"<style\b[^>]*>[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        if (body.Length > 20000) body = body[..20000];
        string pattern = property == "color"
            ? @"(?<![\w-])color\s*:\s*([^;""'}<]+)"
            : @"background(?:-color)?\s*:\s*([^;""'}<]+)";
        foreach (Match m in Regex.Matches(body, pattern, RegexOptions.IgnoreCase))
            if (ParseColorValue(m.Groups[1].Value) is { } c) return c;
        return null;
    }

    static Rgb? HtmlBgColor(string html)
    {
        var m = Regex.Match(html, @"bgcolor\s*=\s*[""']?\s*(#[0-9a-fA-F]{3,8}|[a-zA-Z]+)", RegexOptions.IgnoreCase);
        return m.Success ? ParseColorValue(m.Groups[1].Value) : null;
    }

    static List<Rgb?> RtfColorTable(string rtf)
    {
        var list = new List<Rgb?>();
        var block = Regex.Match(rtf, @"\\colortbl\s*([^}]*)", RegexOptions.IgnoreCase);
        if (!block.Success) return list;
        foreach (string entry in block.Groups[1].Value.Split(';'))
        {
            var r = Regex.Match(entry, @"\\red(\d+)", RegexOptions.IgnoreCase);
            var g = Regex.Match(entry, @"\\green(\d+)", RegexOptions.IgnoreCase);
            var b = Regex.Match(entry, @"\\blue(\d+)", RegexOptions.IgnoreCase);
            if (!r.Success || !g.Success || !b.Success) { list.Add(null); continue; }
            byte Clamp(Match m) => (byte)Math.Clamp(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 0, 255);
            list.Add(new Rgb(Clamp(r), Clamp(g), Clamp(b)));
        }
        return list;
    }

    static Rgb? RtfColor(string rtf, string control, List<Rgb?> table)
    {
        if (table.Count == 0) return null;
        // Skip the colour table itself, then take the first colour switch in the document body.
        int from = rtf.IndexOf(@"\colortbl", StringComparison.OrdinalIgnoreCase);
        if (from < 0) from = 0;
        int close = rtf.IndexOf('}', from);
        if (close > from) from = close;
        foreach (Match m in Regex.Matches(rtf[from..], @"\\" + control + @"(\d+)", RegexOptions.IgnoreCase))
        {
            int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (n > 0 && n < table.Count && table[n] is { } c) return c; // 0 = automatic
        }
        return null;
    }

    /// <summary>
    /// Reads the foreground / background a clip brought from the source application.
    /// Only colours that would be visible on the card are picked up; keywords such as
    /// "windowtext", "inherit" and "transparent" are ignored so the theme's ink wins.
    /// </summary>
    public static RichColors ExtractRichColors(string? html, string? rtf)
    {
        Rgb? fg = null, bg = null;
        if (!string.IsNullOrEmpty(html))
        {
            fg ??= CssColor(html, "color");
            bg ??= CssColor(html, "background") ?? HtmlBgColor(html);
        }
        if (!string.IsNullOrEmpty(rtf))
        {
            var table = RtfColorTable(rtf);
            fg ??= RtfColor(rtf, "cf", table);
            bg ??= RtfColor(rtf, "highlight", table) ?? RtfColor(rtf, "cb", table);
        }
        return new RichColors(fg, bg);
    }

    /// <summary>
    /// Keeps a pasted colour when it is readable, otherwise nudges it toward black or white
    /// (whichever pole gives the better worst-case contrast) by the smallest amount that
    /// reaches minRatio against BOTH ends of the card gradient. Hue is preserved as far as
    /// the contrast requirement allows. If the two surfaces are so far apart that no single
    /// ink can reach minRatio, the best achievable colour is returned.
    /// </summary>
    public static Rgb SafeInk(Rgb top, Rgb bottom, Rgb? source, Rgb fallback, double minRatio = 4.5)
    {
        Rgb ink = source ?? fallback;
        double Worst(Rgb c) => Math.Min(c.Contrast(top), c.Contrast(bottom));
        if (Worst(ink) >= minRatio) return ink;
        Rgb pole = Worst(Rgb.Black) >= Worst(Rgb.White) ? Rgb.Black : Rgb.White;
        double lo = 0, hi = 1;
        for (int i = 0; i < 24; i++)
        {
            double mid = (lo + hi) / 2;
            if (Worst(ink.Mix(pole, mid)) >= minRatio) hi = mid; else lo = mid;
        }
        return ink.Mix(pole, hi);
    }

    /// <summary>
    /// Derives the five colours a card needs from one tint. Mirrors Theme.NotePalette so
    /// light and dark tints both stay readable without per-scheme ink tables.
    /// sourceInk / sourceBg come from ExtractRichColors when the paste carried formatting.
    /// </summary>
    public static (Rgb top, Rgb bottom, Rgb border, Rgb ink, Rgb dim) CardPalette(
        Rgb tint, Rgb? sourceInk = null, Rgb? sourceBg = null)
    {
        // Pick the ink by measured contrast, not by a luma threshold: mid-bright colours such
        // as #009E73 or #F94144 sit in a band where a naive "dark tint -> light ink" rule
        // fails 4.5:1.
        bool darkInk = tint.Contrast(Rgb.DarkInk) >= tint.Contrast(Rgb.LightInk);
        var toward = darkInk ? Rgb.White : Rgb.Black;
        var top = tint.Mix(toward, darkInk ? 0.42 : 0.18);
        var bottom = tint.Mix(toward, darkInk ? 0.12 : 0.05);
        var border = tint.Mix(darkInk ? Rgb.Black : Rgb.White, 0.30);
        var baseInk = darkInk ? Rgb.DarkInk : Rgb.LightInk;
        var baseDim = darkInk ? Rgb.FromHex("#64748B") : Rgb.FromHex("#B8C0CC");

        // A pasted background is hinted at, not copied: the card still reads as its content
        // type, but the ink is chosen against this blended surface too.
        if (sourceBg is { } sb)
        {
            top = top.Mix(sb, 0.12);
            bottom = bottom.Mix(sb, 0.20);
        }
        var ink = SafeInk(top, bottom, sourceInk, baseInk, 4.5);
        var dim = SafeInk(top, bottom, null, baseDim, 3.0);
        // Last-resort nudge for colours stuck near the mid-luma crossover (e.g. pure red).
        for (int i = 0; i < 12 && Math.Min(ink.Contrast(top), ink.Contrast(bottom)) < 4.5; i++)
        {
            top = top.Mix(toward, 0.06);
            bottom = bottom.Mix(toward, 0.06);
            ink = SafeInk(top, bottom, sourceInk, baseInk, 4.5);
            dim = SafeInk(top, bottom, null, baseDim, 3.0);
        }
        return (top, bottom, border, ink, dim);
    }

    /// <summary>
    /// Builds the spliced gradient for the rope. Only cards hanging inside the current viewport
    /// contribute; bare stretches stay the theme rope colour. Cards that must not be tinted
    /// (色值卡 / 图片卡) are simply absent from the input list.
    ///
    /// smooth  : each card owns a flat +/-pad plateau, WPF interpolates between plateaus.
    /// banded  : two stops at the same offset at every midpoint = a hard colour seam.
    ///
    /// Offsets are normalised 0..1, strictly non-decreasing, with both ends pinned to baseRope,
    /// so they can be copied straight into a LinearGradientBrush whose MappingMode is Absolute
    /// and whose StartPoint/EndPoint are (0,0)/(width,0).
    /// </summary>
    public static RopeStop[] RopeStops(
        IReadOnlyList<(double center, Rgb color)> cards,
        double width, Rgb baseRope, double strength = 0.7, bool banded = false)
    {
        var plain = new[] { new RopeStop(0, baseRope), new RopeStop(1, baseRope) };
        if (width <= 0 || cards == null || cards.Count == 0) return plain;

        double s = Math.Clamp(strength, 0, 1);
        double pad = Math.Clamp(width * 0.018, 6, 26);
        var visible = new List<(double c, Rgb col)>();
        foreach (var (c, col) in cards)
            if (c > -pad && c < width + pad)
                visible.Add((Math.Clamp(c, 0, width), col.Mix(baseRope, 1 - s)));
        if (visible.Count == 0) return plain;
        visible.Sort((a, b) => a.c.CompareTo(b.c));

        var stops = new List<RopeStop> { new(0, baseRope) };
        for (int i = 0; i < visible.Count; i++)
        {
            var (c, col) = visible[i];
            if (banded)
            {
                double left = i == 0 ? 0 : (visible[i - 1].c + c) / 2;
                double right = i == visible.Count - 1 ? width : (c + visible[i + 1].c) / 2;
                stops.Add(new RopeStop(left / width, col));
                stops.Add(new RopeStop(right / width, col));
            }
            else
            {
                stops.Add(new RopeStop(Math.Clamp((c - pad) / width, 0, 1), col));
                stops.Add(new RopeStop(Math.Clamp((c + pad) / width, 0, 1), col));
            }
        }
        stops.Add(new RopeStop(1, baseRope));

        double prev = 0;
        for (int i = 0; i < stops.Count; i++)
        {
            double o = Math.Clamp(stops[i].Offset, prev, 1);
            stops[i] = new RopeStop(o, stops[i].Color);
            prev = o;
        }
        return stops.ToArray();
    }
}
