using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Shengyi;

// Sticky is last on purpose: Kind is serialized as a number, so old clips.json files keep
// meaning exactly what they did before this type existed.
enum ClipKind { Text, Link, Code, Color, Image, Files, Sticky }

// One thing that was copied. Text lives in the index; images are PNG files in the Images folder.
// Searchable-text extraction lives in ClipText.cs (a partial class kept dependency-free so the
// index can be exercised without WPF).
sealed partial class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipKind Kind { get; set; }
    public string? Text { get; set; }
    public string? Html { get; set; }
    public string? Rtf { get; set; }
    public string? Image { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<string>? Files { get; set; }
    // Original clipboard image format (png / jpg / gif / webp / bmp). Since 0.6.0 the exact bytes
    // are kept on disk instead of always re-encoding to PNG. Null = history from an older version,
    // which is PNG by definition.
    public string? ImageFormat { get; set; }
    // Transient: the exact clipboard bytes, handed from the watcher to Add() and then dropped.
    // Never serialized — clips.json keeps only the file name and the format id.
    [JsonIgnore] public byte[]? OriginalBytes { get; set; }
    public bool Pinned { get; set; }
    public DateTime Time { get; set; }
    public string Hash { get; set; } = "";
    public string? NoteColor { get; set; }            // sticky notes only; null / "auto" follows the theme

    // Where the copy came from, the formats the owning app exposed, and any smart tags.
    // All optional: clips.json written by older versions loads unchanged.
    public string? SourceApp { get; set; }            // process name without .exe, e.g. "chrome"
    public List<string>? Formats { get; set; }        // clipboard format names, e.g. "HTML Format"
    public List<string>? Tags { get; set; }           // canonical smart tags, e.g. "phone" (see SmartTags.cs)
    public string? FileCategory { get; set; }         // file cards: media / archive / document / ...

    // How often the card was taken from 绳忆. TotalUses never decreases; UseTimes holds the UTC
    // Unix-millisecond stamps of the last 90 days, oldest first, so a rolling window can be
    // recomputed at any time (also after the user widens the window in settings).
    public int TotalUses { get; set; }
    public List<long>? UseTimes { get; set; }

    [JsonIgnore] public bool IsRich => Html != null || Rtf != null;
    [JsonIgnore] public bool IsText => Kind is ClipKind.Text or ClipKind.Link or ClipKind.Code or ClipKind.Color;
    [JsonIgnore] public bool IsSticky => Kind == ClipKind.Sticky;
    // Sticky notes are permanent notes, not history: they always lead the line, are never swept,
    // and never take part in frequency ordering (even if someone unpins one by editing the JSON).
    [JsonIgnore] public bool IsFront => Pinned || IsSticky;
    [JsonIgnore] public string? ImagePath => Image == null ? null : Path.Combine(ClipStore.ImageDir, Image);
    [JsonIgnore] public long LastUsedMs => UseTimes is { Count: > 0 } t ? t[^1] : 0;
    [JsonIgnore] public int RecentUses => CountUses(Theme.FreqWindowDays);

    public static long Cutoff(int windowDays) =>
        DateTimeOffset.UtcNow.AddDays(-Math.Clamp(windowDays, 1, 90)).ToUnixTimeMilliseconds();

    public int CountUses(int windowDays) => CountUses(Cutoff(windowDays));

    public int CountUses(long cutoffUtcMs)
    {
        if (UseTimes is not { Count: > 0 } times) return 0;
        int n = 0;
        for (int i = times.Count - 1; i >= 0 && times[i] >= cutoffUtcMs; i--) n++;
        return n; // oldest first, so the first stale stamp ends the scan
    }

}

static class Classify
{
    public static ClipKind Text(string text)
    {
        var t = text.Trim();
        if (t.Length <= 40 && ParseColor(t) != null) return ClipKind.Color;
        if (t.IndexOfAny(new[] { ' ', '\n', '\t' }) < 0 && Uri.TryCreate(t, UriKind.Absolute, out var u)
            && u.Scheme is "http" or "https" or "ftp" or "mailto") return ClipKind.Link;
        if (LooksLikeCode(text)) return ClipKind.Code;
        return ClipKind.Text;
    }

    static bool LooksLikeCode(string s)
    {
        var lines = s.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count < 2) return false;
        int score = 0;
        foreach (var l in lines)
        {
            if (l.StartsWith("    ") || l.StartsWith('\t')) score++;
            var end = l.TrimEnd();
            if (end.EndsWith(';') || end.EndsWith('{') || end.EndsWith('}') || end.EndsWith(')')) score++;
            if (l.Contains("=>") || l.Contains("==") || l.Contains("()") || l.Contains("</") || l.Contains("//")) score++;
        }
        return score >= Math.Max(3, lines.Count * 0.6);
    }

    // CSS-style colours: #rgb, #rgba, #rrggbb, #rrggbbaa, rgb(), rgba().
    public static Color? ParseColor(string s)
    {
        s = s.Trim();
        try
        {
            if (Regex.IsMatch(s, "^#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))
            {
                var h = s[1..];
                if (h.Length <= 4) h = string.Concat(h.Select(c => new string(c, 2)));
                byte B(int i) => Convert.ToByte(h.Substring(i, 2), 16);
                return Color.FromArgb(h.Length == 8 ? B(6) : (byte)255, B(0), B(2), B(4));
            }
            var m = Regex.Match(s, @"^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:[\s,/]+([\d.]+%?))?\s*\)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            byte C(int g) => (byte)Math.Clamp(double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture), 0, 255);
            double a = 1;
            if (m.Groups[4].Success)
            {
                var v = m.Groups[4].Value;
                a = v.EndsWith('%') ? double.Parse(v[..^1], CultureInfo.InvariantCulture) / 100 : double.Parse(v, CultureInfo.InvariantCulture);
            }
            return Color.FromArgb((byte)(Math.Clamp(a, 0, 1) * 255), C(1), C(2), C(3));
        }
        catch (Exception e) when (e is FormatException or OverflowException) { return null; }
    }

    public static string HashText(string s) => "t:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));
    public static string HashFiles(IEnumerable<string> files) =>
        "f:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Join("\n", files).ToLowerInvariant())));
}

// The display order, kept separate from the store so Items stays a plain "newest copy first"
// history. Pinned cards lead; cards used at least FreqThreshold times inside the rolling window
// come next, most uses first; everything else keeps its original time order.
static class Ranking
{
    public static List<ClipItem> Order(IEnumerable<ClipItem> items)
    {
        var all = items.ToList();
        var pinned = all.Where(i => i.IsFront).ToList();
        var rest = all.Where(i => !i.IsFront).ToList();
        int threshold = Theme.FreqThreshold;
        if (threshold <= 0) return pinned.Concat(rest).ToList();

        long cutoff = ClipItem.Cutoff(Theme.FreqWindowDays);
        var scored = rest.Select(i => (item: i, uses: i.CountUses(cutoff))).ToList();
        var hot = scored.Where(x => x.uses >= threshold)
            .OrderByDescending(x => x.uses)
            .ThenByDescending(x => x.item.LastUsedMs)
            .ThenByDescending(x => x.item.Time)
            .Select(x => x.item);
        var cold = scored.Where(x => x.uses < threshold).Select(x => x.item);
        return pinned.Concat(hot).Concat(cold).ToList();
    }
}

// The clipboard history. Items[0] is the most recently copied.
sealed class ClipStore : IDisposable
{
    public static string Root { get; set; } = Settings.DataDir; // --shots points this at sample data
    public static string ImageDir => Path.Combine(Root, "Images");
    static string IndexPath => Path.Combine(Root, "clips.json");

    public ObservableCollection<ClipItem> Items { get; } = new();
    public event Action<ClipItem>? Bumped;  // copied again: moved back to the front
    public event Action<ClipItem>? Updated; // pinned or edited: its card needs redrawing
    public event Action? UsageReset;       // statistics cleared: re-sort right away

    // A rebuildable index, never persisted: clips.json stays the only source of truth. Version
    // changes whenever the item list or any searchable text changes, so the index is rebuilt
    // only then — not on every keystroke in the search box.
    public SearchIndex Index { get; } = new();
    public int Version { get; private set; }

    public List<(ClipItem item, double score)> Search(SearchQuery q)
    {
        Index.Refresh(Items, Version);
        return Index.Search(q, settings().FuzzySearch);
    }

    // Smart tags are derived, never authoritative: clips.json is still the only source of truth.
    // Turning recognition off removes only the tags we generated, leaving any unknown/manual ones.
    void Retag(ClipItem item)
    {
        if (settings().SmartTagging) item.Tags = SmartTags.Analyze(item);
        else if (item.Tags != null)
        {
            var rest = item.Tags.Where(t => !SmartTags.IsKnown(t)).ToList();
            item.Tags = rest.Count > 0 ? rest : null;
        }
    }

    // Recompute tags for history that predates the feature, and right after the setting changes.
    public void RefreshTags()
    {
        foreach (var item in Items) Retag(item);
        Version++;                 // tags are part of the indexed metadata
        ScheduleSave();
    }

    const int MaxUseTimes = 2000;          // safety cap per card
    const int KeepUseDays = 90;            // longest selectable window

    readonly Func<Settings> settings;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly DispatcherTimer sweepTimer = new() { Interval = TimeSpan.FromHours(1) };
    readonly Dictionary<string, DispatcherTimer> pending = new(StringComparer.OrdinalIgnoreCase);
    readonly FileSystemWatcher watcher;

    public ClipStore(Func<Settings> settings)
    {
        this.settings = settings;
        Directory.CreateDirectory(ImageDir);
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        sweepTimer.Tick += (_, _) => Sweep();
        Items.CollectionChanged += (_, _) => Version++; // add / remove / move invalidates the index
        Load();
        ImportOldLine();
        Sweep();
        sweepTimer.Start();

        // Notices edits saved by Paint and images deleted by hand.
        watcher = new FileSystemWatcher(ImageDir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        watcher.Changed += (_, e) => OnDisk(e.Name);
        watcher.Created += (_, e) => OnDisk(e.Name);
        watcher.Deleted += (_, e) => OnDisk(e.Name);
        watcher.Renamed += (_, e) => { OnDisk(e.OldName); OnDisk(e.Name); };
        watcher.EnableRaisingEvents = true;
    }

    public void Add(ClipItem item, BitmapSource? bitmap = null)
    {
        var same = Items.FirstOrDefault(i => i.Hash == item.Hash);
        if (same != null)
        {
            same.Time = DateTime.Now;
            int at = Items.IndexOf(same);
            if (at > 0) Items.Move(at, 0);
            Bumped?.Invoke(same);
            ScheduleSave();
            return;
        }
        // Derived from the paths, so every caller (watcher, screenshots, drag-in) gets a
        // category without having to remember to set it. clips.json remains the only truth.
        if (item.Kind == ClipKind.Files) item.FileCategory = FileCategories.Of(item.Files);
        Retag(item);
        if (bitmap != null)
        {
            var raw = item.OriginalBytes;
            item.OriginalBytes = null; // do not keep a second copy of a large image in memory
            item.Image = item.Id + ImageFormats.Extension(item.ImageFormat);
            try
            {
                if (raw is { Length: > 0 })
                    File.WriteAllBytes(item.ImagePath!, raw);
                else
                {
                    using var fs = File.Create(item.ImagePath!);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(fs);
                }
            }
            catch (IOException) { return; }
            item.Width = bitmap.PixelWidth;
            item.Height = bitmap.PixelHeight;
        }
        item.Time = DateTime.Now;
        Items.Insert(0, item);
        Sweep();
        ScheduleSave();
    }

    public void Remove(ClipItem item)
    {
        Items.Remove(item);
        if (item.ImagePath is { } path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        ScheduleSave();
    }

    public void SetPinned(ClipItem item, bool pinned)
    {
        item.Pinned = pinned;
        Updated?.Invoke(item);
        ScheduleSave();
    }

    // A sticky note is created outside the clipboard watcher, so it can never be swallowed by
    // Hash-based deduplication, and creating one never triggers a history sweep.
    public void AddSticky(ClipItem item)
    {
        item.Kind = ClipKind.Sticky;
        item.Pinned = true;
        item.Time = DateTime.Now;
        item.Hash = "n:" + item.Id;   // a unique namespace: notes never dedupe with copied text
        item.Html = item.Rtf = item.Image = null;
        item.Files = null;
        Retag(item);
        Items.Insert(0, item);
        ScheduleSave();
    }

    // Rebuilds the card (color and text live in the visual tree built by CardView.Dress).
    public void UpdateSticky(ClipItem item, string text, string? color)
    {
        if (!Items.Contains(item) || !item.IsSticky) return;
        item.Text = text;
        item.NoteColor = color;
        item.Html = item.Rtf = null;
        Retag(item);
        Version++;                 // the note's words and tags are indexed
        Updated?.Invoke(item);
        ScheduleSave();
    }

    // One "take" of a card: copy, paste, or a successful drag out. Counting is deliberately
    // independent of the ranking setting, so turning the feature on later still has history.
    // No event is raised here: the new order is picked up the next time the line is revealed,
    // which keeps the card under the pointer (and the Ctrl+1..9 indices) from shifting mid-action.
    public void RecordUse(ClipItem item)
    {
        if (!Items.Contains(item)) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var times = item.UseTimes ??= new List<long>();
        if (times.Count > 0 && now - times[^1] < 500) return; // debounce double clicks
        item.TotalUses++;

        long cutoff = DateTimeOffset.UtcNow.AddDays(-KeepUseDays).ToUnixTimeMilliseconds();
        int stale = 0;
        while (stale < times.Count && times[stale] < cutoff) stale++;
        if (stale > 0) times.RemoveRange(0, stale);
        times.Add(now);
        if (times.Count > MaxUseTimes) times.RemoveRange(0, times.Count - MaxUseTimes);
        ScheduleSave();
    }

    public void ResetUsage()
    {
        foreach (var item in Items) { item.TotalUses = 0; item.UseTimes = null; }
        UsageReset?.Invoke();
        ScheduleSave();
    }

    public void UpdateText(ClipItem item, string text)
    {
        if (text == item.Text) return;
        item.Text = text;
        item.Html = item.Rtf = null;
        item.Kind = Classify.Text(text);
        item.Hash = Classify.HashText(text);
        Retag(item);
        Version++;                 // edited text changes the indexed words and tags
        Updated?.Invoke(item);
        ScheduleSave();
    }

    public void ClearUnpinned()
    {
        foreach (var item in Items.Where(i => !i.IsFront).ToList()) Remove(item);
    }

    // Enforces the item limit and the retention period. Pinned cards and sticky notes are never
    // dropped, and notes don't count towards MaxItems either.
    public void Sweep()
    {
        var s = settings();
        var cutoff = s.RetentionDays > 0 ? DateTime.Now.AddDays(-s.RetentionDays) : DateTime.MinValue;
        int kept = 0;
        foreach (var item in Items.ToList())
        {
            if (item.IsFront) continue;
            if (item.Time < cutoff || ++kept > s.MaxItems) Remove(item);
        }
    }

    void OnDisk(string? name) => dispatcher.BeginInvoke(() =>
    {
        if (name == null || Items.FirstOrDefault(i => string.Equals(i.Image, name, StringComparison.OrdinalIgnoreCase)) is not { } item) return;
        // Editors often save by delete + rename, so wait for the dust to settle before deciding.
        if (!pending.TryGetValue(name, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) => { timer.Stop(); pending.Remove(name); Settle(item); };
            pending[name] = timer;
        }
        timer.Stop();
        timer.Start();
    });

    void Settle(ClipItem item)
    {
        if (!Items.Contains(item)) return;
        if (!File.Exists(item.ImagePath)) { Items.Remove(item); ScheduleSave(); return; }
        if (Thumbs.LoadFull(item.ImagePath!) is not { } bmp) return;
        var hash = ImageHash.Of(bmp);
        if (hash == item.Hash) return; // our own write
        item.Hash = hash;
        item.Width = bmp.PixelWidth;
        item.Height = bmp.PixelHeight;
        Updated?.Invoke(item);
        ScheduleSave();
    }

    void ScheduleSave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    void Load()
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(IndexPath));
            bool enriched = false;
            foreach (var item in list ?? new())
            {
                if (item.Kind == ClipKind.Image && !File.Exists(item.ImagePath)) continue;
                // History saved before v0.5.0 gets recognised too, without changing its order.
                Retag(item);
                // File cards saved before 0.6.0 have no category; derive it on load so the
                // chips and cat: filters work immediately after an upgrade.
                if (item.Kind == ClipKind.Files && item.FileCategory == null)
                {
                    item.FileCategory = FileCategories.Of(item.Files);
                    enriched = true;
                }
                Items.Add(item);
            }
            if (enriched) ScheduleSave();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Root);
            var tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items.ToList()));
            File.Move(tmp, IndexPath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // The screenshot version kept its photos in Line\; bring them over once.
    void ImportOldLine()
    {
        var old = Path.Combine(Root, "Line");
        if (!Directory.Exists(old)) return;
        foreach (var f in new DirectoryInfo(old).GetFiles().OrderBy(f => f.CreationTimeUtc))
        {
            if (Thumbs.LoadFull(f.FullName) is not { } bmp) continue;
            var item = new ClipItem
            {
                Kind = ClipKind.Image, Time = f.CreationTime, Width = bmp.PixelWidth, Height = bmp.PixelHeight, Hash = ImageHash.Of(bmp),
            };
            item.Image = item.Id + f.Extension.ToLowerInvariant();
            try { File.Move(f.FullName, item.ImagePath!); Items.Insert(0, item); }
            catch (IOException) { }
        }
        try { Directory.Delete(old); } catch (IOException) { } // only when nothing is left
        ScheduleSave();
    }

    public void Dispose()
    {
        watcher.Dispose();
        sweepTimer.Stop();
        saveTimer.Stop();
        Save();
    }
}
