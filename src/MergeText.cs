using System;
using System.Collections.Generic;
using System.Linq;

namespace Shengyi;

// The text produced by "merge paste" / "merge copy". Kept dependency-free so the console
// self-test can assert the exact output, including numbering and the skipped-card count.
static class MergeText
{
    public sealed class Result
    {
        public string Text { get; set; } = "";
        public int Used { get; set; }
        public int Skipped { get; set; }
        public int Count => Used + Skipped;
    }

    // One card becomes one block. Text-like cards contribute their text; file cards contribute
    // their paths (one per line); image cards have no text representation and are skipped.
    public static string? Part(ClipItem item)
    {
        switch (item.Kind)
        {
            case ClipKind.Files:
                var paths = (item.Files ?? new List<string>())
                    .Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
                return paths.Count == 0 ? null : string.Join("\n", paths);
            case ClipKind.Image:
                return null;
            default:
                var text = (item.Text ?? "").Trim();
                return text.Length == 0 ? null : text;
        }
    }

    public static Result Build(IEnumerable<ClipItem> items, bool numbered, string newline = "\n")
    {
        var blocks = new List<string>();
        int skipped = 0;
        foreach (var item in items)
        {
            var part = Part(item);
            if (part == null) { skipped++; continue; }
            blocks.Add(part.Replace("\r\n", "\n"));
        }
        string text = numbered
            ? string.Join(newline, blocks.Select((b, i) => (i + 1) + ". " + b))
            : string.Join(newline, blocks);
        return new Result { Text = text, Used = blocks.Count, Skipped = skipped };
    }
}
