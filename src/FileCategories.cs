using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Shengyi;

// Copied files are already recorded with their paths; this gives them a coarse, human-meaningful
// category so "只看从资源管理器复制的视频/压缩包" becomes possible. Decoding media is left to the
// shell — 绳忆 only classifies by extension. Pure logic, no WPF dependency (self-test compiles it).
static class FileCategories
{
    public const string Media = "media";
    public const string Archive = "archive";
    public const string Document = "document";
    public const string Picture = "picture";
    public const string Folder = "folder";
    public const string Other = "other";

    static readonly (string id, string en, string zh, string[] exts, string[] aliases)[] Defs =
    {
        (Media, "Media", "音视频", new[]
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg",
            ".3gp", ".ts", ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma", ".opus", ".ape",
        }, new[] { "video", "audio", "movie", "music", "视频", "音频", "媒体", "音乐" }),
        (Archive, "Archives", "压缩包", new[]
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".tgz", ".zst", ".lz",
        }, new[] { "zip", "compressed", "package", "压缩", "归档", "打包" }),
        (Document, "Documents", "文档", new[]
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".csv",
            ".rtf", ".epub", ".odt", ".ods", ".pages", ".numbers", ".log", ".json", ".xml", ".yaml", ".yml",
        }, new[] { "doc", "docs", "text", "office", "文件", "文稿", "表格" }),
        (Picture, "Pictures", "图片文件", new[]
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".svg", ".ico", ".psd", ".ai", ".heic",
        }, new[] { "photo", "image", "graphic", "照片", "图像" }),
        (Folder, "Folders", "文件夹", Array.Empty<string>(), new[] { "dir", "directory", "目录" }),
        (Other, "Other", "其它", Array.Empty<string>(), new[] { "misc", "其它", "其他" }),
    };

    public static readonly string[] All = Defs.Select(d => d.id).ToArray();
    static bool Zh => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

    static readonly Dictionary<string, int> ByName = BuildNames();

    static Dictionary<string, int> BuildNames()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Defs.Length; i++)
        {
            var d = Defs[i];
            map[d.id] = i;
            map[d.en] = i;
            map[d.zh] = i;
            foreach (var a in d.aliases) map[a] = i;
        }
        return map;
    }

    public static bool IsKnown(string? id) => id != null && ByName.ContainsKey(id);

    // Localised display name; unknown ids (e.g. a hand-edited clips.json) survive unchanged.
    public static string Name(string id) => ByName.TryGetValue(id, out int i) ? (Zh ? Defs[i].zh : Defs[i].en) : id;

    // cat:video / cat:音视频 / cat:media all mean the same thing.
    public static string Canonical(string word) =>
        ByName.TryGetValue(word.Trim(), out int i) ? Defs[i].id : word.Trim();

    public static string? OfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.TrimEnd('\\', '/');
        if (Directory.Exists(trimmed)) return Folder;
        string ext;
        try { ext = Path.GetExtension(trimmed).ToLowerInvariant(); }
        catch (ArgumentException) { return null; }
        if (ext.Length == 0) return Directory.Exists(trimmed) ? Folder : null;
        foreach (var d in Defs)
            if (d.exts.Contains(ext)) return d.id;
        return Other;
    }

    // A file card can carry several paths. The dominant known category wins; a tie keeps the
    // first file's category, which is also the one shown on the card. A pure folder list is a
    // folder card. Anything unclassifiable yields null so no badge is drawn.
    public static string? Of(IEnumerable<string>? files)
    {
        if (files == null) return null;
        var list = files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (list.Count == 0) return null;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? first = null;
        foreach (var f in list)
        {
            var c = OfPath(f);
            if (c == null) continue;
            first ??= c;
            counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
        }
        if (counts.Count == 0) return null;
        int best = counts.Values.Max();
        foreach (var kv in counts)
            if (kv.Value == best) return kv.Key == Other && first != Other ? first : kv.Key;
        return first;
    }
}
