using System;
using System.Collections.Generic;

namespace Shengyi;

// Clipboard-native image formats. A copy from a browser or a screenshot tool often arrives as
// PNG/JFIF/GIF bytes; re-encoding everything through PngBitmapEncoder (the old behaviour) threw
// away animation, the original compression and the format the receiving app expects. We now keep
// the original bytes on disk and put the same format back on the clipboard.
//
// No WPF / WinForms dependency: the mapping is pure data so the console self-test can compile it.
static class ImageFormats
{
    public const string Png = "png";
    public const string Jpeg = "jpg";
    public const string Gif = "gif";
    public const string Webp = "webp";
    public const string Bmp = "bmp";

    // Order is the preference order when an app offers several formats at once. PNG first because
    // it is lossless and keeps alpha; then the compressed formats that carry their own header.
    // CF_DIB / CF_DIBV5 are deliberately absent: they are header-less bitmaps, so Watchers falls
    // back to a GDI+ conversion to PNG instead of pretending the raw bytes are a file.
    static readonly (string format, string id)[] Known =
    {
        ("PNG", Png),
        ("image/png", Png),
        ("JFIF", Jpeg),
        ("JPEG", Jpeg),
        ("image/jpeg", Jpeg),
        ("JPG", Jpeg),
        ("GIF", Gif),
        ("image/gif", Gif),
        ("image/webp", Webp),
        ("WebP", Webp),
        ("image/bmp", Bmp),
        ("BMP", Bmp),
    };

    public static readonly string[] All = { Png, Jpeg, Gif, Webp, Bmp };

    // The registered clipboard format we write back. Null means "SetImage is enough": GDI+ has no
    // decoder for WebP, and BMP/DIB is already covered by the standard bitmap flavour.
    public static string? WriteFormat(string? id) => id switch
    {
        Png => "PNG",
        Jpeg => "JFIF",
        Gif => "GIF",
        Webp => "image/webp",
        _ => null,
    };

    public static string Extension(string? id) => id switch
    {
        Jpeg => ".jpg",
        Gif => ".gif",
        Webp => ".webp",
        Bmp => ".bmp",
        _ => ".png", // null / unknown = history written before 0.6.0, always PNG
    };

    public static bool IsKnown(string? id) => id switch
    {
        Png or Jpeg or Gif or Webp or Bmp => true,
        _ => false,
    };

    // Given the formats an app actually put on the clipboard, pick the best one and say which
    // internal id, which file extension and which format name to use when reading it back.
    public static string? Best(IEnumerable<string> formats, out string? readFormat)
    {
        readFormat = null;
        foreach (var id in All)
            foreach (var f in formats)
                if (TryMatch(f, out var candidate) && candidate == id)
                {
                    readFormat = f;
                    return id;
                }
        return null;
    }

    public static bool TryMatch(string? format, out string id)
    {
        foreach (var (name, value) in Known)
            if (string.Equals(name, format, StringComparison.OrdinalIgnoreCase))
            {
                id = value;
                return true;
            }
        id = "";
        return false;
    }
}
