using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shengyi;

static class Thumbs
{
    static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    // 0.12.0 · scrolling over image cards re-decoded the 200px thumbnail on every realize.
    // Cache only downscaled decodes (bounded, LRU) — full-size images (QuickLook / copy / hash)
    // stay uncached so a 32 MB bitmap is never retained. The file stamp in the key makes an edit
    // by Paint or an on-disk replacement self-invalidate without an explicit watcher hook.
    const int CacheCapacity = 96;
    readonly record struct ThumbKey(string Path, int Height, long Ticks, long Length);
    static readonly object cacheLock = new();
    static readonly Dictionary<ThumbKey, BitmapSource> cache = new();
    static readonly LinkedList<ThumbKey> lru = new();

    // Loads without keeping the file open, so Paint can overwrite it.
    public static BitmapSource? LoadFull(string path, int decodeHeight = 0)
    {
        ThumbKey key = default;
        bool cached = decodeHeight > 0;
        if (cached)
        {
            try
            {
                var info = new FileInfo(path);
                key = new ThumbKey(path, decodeHeight, info.LastWriteTimeUtc.Ticks, info.Length);
                lock (cacheLock)
                    if (cache.TryGetValue(key, out var hit))
                    {
                        lru.Remove(key);
                        lru.AddLast(key);
                        return hit;
                    }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                cached = false; // stat failed: fall through to a plain decode, no cache
            }
        }

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad; // (IgnoreImageCache would throw: it needs a UriSource)
            if (decodeHeight > 0) bi.DecodePixelHeight = decodeHeight;
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();
            if (cached)
            {
                lock (cacheLock)
                {
                    cache[key] = bi;
                    lru.Remove(key);
                    lru.AddLast(key);
                    while (cache.Count > CacheCapacity && lru.First is { } oldest)
                    {
                        cache.Remove(oldest.Value);
                        lru.RemoveFirst();
                    }
                }
            }
            return bi;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or FileFormatException)
        {
            return null;
        }
    }

    // A small picture for a copied file: the image itself, or the file type's icon. Null for folders.
    public static ImageSource? FileIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (ImageExts.Contains(Path.GetExtension(path).ToLowerInvariant())) return LoadFull(path, 64);
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

// Pixel fingerprint, used to avoid hanging the same image twice.
static class ImageHash
{
    public static string Of(BitmapSource src)
    {
        var bgra = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var px = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(px, stride, 0);
        for (int i = 3; i < px.Length; i += 4) px[i] = 0; // clipboard DIBs often lose alpha
        return $"i:{bgra.PixelWidth}x{bgra.PixelHeight}:{Convert.ToHexString(SHA1.HashData(px))}";
    }
}
