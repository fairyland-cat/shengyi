using System;
using System.Collections.Generic;

namespace Shengyi;

// One search as the line sees it: free words (AND), plus structured filters. Words are folded
// to lower case because the index stores everything lower-cased; the UI panels and the optional
// app:/tag:/kind:/in: prefixes all end up here before the index is asked anything.
sealed class SearchQuery
{
    public List<string> Terms { get; } = new();
    public HashSet<string> Apps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<ClipKind> Kinds { get; } = new();
    public int WithinDays { get; set; }
    public bool PinnedOnly { get; set; }
    public bool TaggedOnly { get; set; }

    public bool HasTerms => Terms.Count > 0;
    public bool HasFilters => Apps.Count > 0 || Tags.Count > 0 || Kinds.Count > 0
        || WithinDays > 0 || PinnedOnly || TaggedOnly;
    public string Highlight => string.Join(" ", Terms);

    // The quick filter tabs are just another kind filter; they combine with the panel choices.
    public void AddTab(Filter filter)
    {
        switch (filter)
        {
            case Filter.Text:
                Kinds.Add(ClipKind.Text); Kinds.Add(ClipKind.Code);
                Kinds.Add(ClipKind.Color); Kinds.Add(ClipKind.Sticky); break;
            case Filter.Images: Kinds.Add(ClipKind.Image); break;
            case Filter.Links: Kinds.Add(ClipKind.Link); break;
            case Filter.Files: Kinds.Add(ClipKind.Files); break;
            case Filter.Sticky: Kinds.Add(ClipKind.Sticky); break;
            case Filter.Pinned: PinnedOnly = true; break;
            case Filter.Tags: TaggedOnly = true; break;
        }
    }

    public static SearchQuery Parse(string input, IEnumerable<string>? apps = null,
        IEnumerable<ClipKind>? kinds = null, int withinDays = 0, IEnumerable<string>? tags = null)
    {
        var q = new SearchQuery { WithinDays = withinDays };
        if (apps != null) foreach (var a in apps) q.Apps.Add(a);
        if (kinds != null) foreach (var k in kinds) q.Kinds.Add(k);
        if (tags != null) foreach (var t in tags) q.Tags.Add(SmartTags.Canonical(t));

        foreach (var raw in (input ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim();
            if (word.Length == 0) continue;
            int colon = word.IndexOf(':');
            if (colon > 0)
            {
                var key = word[..colon].ToLowerInvariant();
                var value = word[(colon + 1)..];
                if (value.Length > 0)
                {
                    switch (key)
                    {
                        case "app": q.Apps.Add(value); continue;
                        case "tag": q.Tags.Add(SmartTags.Canonical(value)); continue;
                        case "kind": if (TryKind(value, out var kind)) q.Kinds.Add(kind); continue;
                        case "in":
                            if (int.TryParse(value.TrimEnd('d', 'D'), out int days) && days > 0)
                                q.WithinDays = Math.Clamp(days, 1, 3650);
                            continue;
                    }
                }
            }
            q.Terms.Add(word.ToLowerInvariant());
        }
        return q;
    }

    public static bool TryKind(string word, out ClipKind kind)
    {
        switch (word.ToLowerInvariant())
        {
            case "text": case "文字": case "文本": kind = ClipKind.Text; return true;
            case "code": case "代码": kind = ClipKind.Code; return true;
            case "link": case "链接": case "网址": kind = ClipKind.Link; return true;
            case "color": case "colour": case "颜色": kind = ClipKind.Color; return true;
            case "image": case "图片": case "照片": kind = ClipKind.Image; return true;
            case "files": case "file": case "文件": kind = ClipKind.Files; return true;
            case "sticky": case "note": case "便签": kind = ClipKind.Sticky; return true;
            default: kind = default; return false;
        }
    }
}
