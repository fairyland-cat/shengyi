using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shengyi;

// The searchable text of a clip, split out of Clips.cs so it has no WPF dependency and can be
// compiled by the index self-test. ClipItem itself is declared partial in Clips.cs.
sealed partial class ClipItem
{
    // Bilingual type words make "只看图片" style searches work on cards with no text at all.
    static readonly string[] KindWords =
    {
        "text 文字 文本", "link 链接 网址", "code 代码", "color 颜色 色值",
        "image 图片 照片 截图 png", "files 文件 文件夹", "sticky 便签 笔记",
    };

    // The human content of the card. Unlike the old SearchText, this also covers file names,
    // image cards (which used to search as an empty string) and the visible words inside HTML.
    public string BodyText()
    {
        if (Kind == ClipKind.Files)
            return string.Join(" ", (Files ?? new()).Select(f => Path.GetFileName(f.TrimEnd('\\', '/'))));
        if (Kind == ClipKind.Image) return KindWords[(int)Kind];
        var text = Text ?? "";
        var html = HtmlText();
        return html.Length == 0 ? text : text + " " + html;
    }

    // CF_HTML carries a header and fragment markers; keep only the visible words. Capped because
    // a single rich copy can be several megabytes and would dominate the in-memory index.
    string HtmlText()
    {
        if (Html is not { Length: > 0 } html) return "";
        const string Begin = "<!--StartFragment-->", End = "<!--EndFragment-->";
        int start = html.IndexOf(Begin, StringComparison.OrdinalIgnoreCase);
        int end = html.IndexOf(End, StringComparison.OrdinalIgnoreCase);
        string frag = start >= 0 && end > start ? html[(start + Begin.Length)..end] : html;
        if (frag.Length > 4000) frag = frag[..4000];
        frag = Regex.Replace(frag, "<[^>]*>", " ");
        frag = Regex.Replace(frag, @"\s+", " ");
        return System.Net.WebUtility.HtmlDecode(frag).Trim();
    }

    // Searchable metadata: source app, clipboard formats, smart tags and the content type.
    public string MetaText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(SourceApp)) parts.Add(SourceApp);
        if (Formats != null) parts.AddRange(Formats);
        if (Tags != null)
            foreach (var tag in Tags)
            {
                parts.Add(tag);
                string name = SmartTags.Name(tag);   // "手机号" as well as "phone"
                if (!string.Equals(name, tag, System.StringComparison.OrdinalIgnoreCase)) parts.Add(name);
            }
        if (!string.IsNullOrEmpty(FileCategory))
        {
            parts.Add(FileCategory);
            string category = FileCategories.Name(FileCategory); // "压缩包" as well as "archive"
            if (!string.Equals(category, FileCategory, StringComparison.OrdinalIgnoreCase)) parts.Add(category);
        }
        if ((int)Kind >= 0 && (int)Kind < KindWords.Length) parts.Add(KindWords[(int)Kind]);
        return string.Join(" ", parts);
    }

    public string SearchText() => BodyText() + " " + MetaText();
}
