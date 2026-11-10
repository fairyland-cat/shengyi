using System;
using System.Text;

namespace Shengyi;

// Turns Chinese text into searchable Latin spellings. Three forms are produced:
//   Full      "周报"          -> "zhoubao"     (whole-pinyin search, e.g. "zhoubao")
//   Initials  "周报 会议"     -> "zbhy"        (first-letter search, e.g. "zb")
//   Variants  "女"            -> "nv "         (ü also written as v)
// Latin words inside the text keep their own letters, so mixed content still works.
static class Pinyin
{
    public readonly record struct Transcription(string Full, string Initials, string Variants);

    public static Transcription Transcribe(string text)
    {
        var full = new StringBuilder(text.Length * 2);
        var initials = new StringBuilder(text.Length / 2 + 4);
        var variants = new StringBuilder();
        bool wordStart = true;

        foreach (char c in text)
        {
            if (char.IsSurrogate(c))
            {
                Separate();
                continue;
            }
            if (PinyinData.Of(c) is { } readings)
            {
                full.Append(readings[0]);
                initials.Append(readings[0][0]);
                for (int i = 1; i < readings.Length; i++) variants.Append(readings[i]).Append(' ');
                wordStart = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                char lower = char.ToLowerInvariant(c);
                full.Append(lower);
                if (wordStart) { initials.Append(lower); wordStart = false; }
            }
            else Separate();
        }
        return new Transcription(full.ToString(), initials.ToString(), variants.ToString());

        void Separate()
        {
            if (full.Length > 0 && full[^1] != ' ') full.Append(' ');
            wordStart = true;
        }
    }
}
