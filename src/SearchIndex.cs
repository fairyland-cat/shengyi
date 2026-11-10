using System;
using System.Collections.Generic;

namespace Shengyi;

// In-memory full-text index over the clipboard history. No database, no native dependency:
// clips.json stays the single source of truth and this is a rebuildable, disposable index.
//
// Each card is normalised into one lower-cased string containing its visible words, its metadata
// (source app, formats, tags, content type), the full pinyin of any Chinese text, its pinyin
// initials and alternative ü/v spellings. A trigram posting list narrows long queries down to a
// handful of candidates; the final check is always a real substring test, so a match can never be
// lost. Cards longer than GramLimit get a fallback full scan to stay correct.
//
// Rebuilding 2,000 ordinary cards takes a few milliseconds, and only happens when the store
// version changes — typing in the search box never rebuilds anything.
sealed class SearchIndex
{
    const int NormLimit = 20000;   // cap per card: a 1 MB copy must not dominate memory
    const int GramLimit = 2000;    // postings cover the first N chars of the normalised text

    sealed class Entry
    {
        public ClipItem Item = null!;
        public string Norm = "";
    }

    readonly List<Entry> entries = new();
    Dictionary<string, List<int>> postings = new(StringComparer.Ordinal);
    int version = -1;
    bool built;

    public int Count => entries.Count;

    public void Refresh(IReadOnlyList<ClipItem> items, int storeVersion)
    {
        if (built && version == storeVersion) return;
        Rebuild(items);
        version = storeVersion;
        built = true;
    }

    void Rebuild(IReadOnlyList<ClipItem> items)
    {
        entries.Clear();
        postings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int idx = 0; idx < items.Count; idx++)
        {
            var item = items[idx];
            string body = item.BodyText();
            if (body.Length > NormLimit) body = body[..NormLimit];
            var py = Pinyin.Transcribe(body);
            string norm = (body + " " + item.MetaText() + " " + py.Full + " "
                           + py.Initials + " " + py.Variants).ToLowerInvariant();
            entries.Add(new Entry { Item = item, Norm = norm });

            string head = norm.Length > GramLimit ? norm[..GramLimit] : norm;
            foreach (var gram in Grams(head))
            {
                if (!postings.TryGetValue(gram, out var list)) postings[gram] = list = new List<int>();
                list.Add(idx); // Grams() de-duplicates, so each index appears at most once per gram
            }
        }
    }

    // Returns the cards that satisfy the query, with a relevance score. Score 0 means "matched
    // by filters only" — the caller then keeps the usual frequency order.
    public List<(ClipItem item, double score)> Search(SearchQuery q, bool fuzzy)
    {
        var allowed = new HashSet<int>();
        DateTime from = q.WithinDays > 0 ? DateTime.Now.AddDays(-q.WithinDays) : DateTime.MinValue;
        for (int i = 0; i < entries.Count; i++) if (Pass(entries[i].Item, q, from)) allowed.Add(i);

        // Exact / pinyin matches first. Fuzzy matching is a fallback for the whole query only —
        // never for one word inside an AND, which would let a nonsense word silently drop out.
        var result = Run(q, allowed, false);
        if (result.Count == 0 && fuzzy && q.HasTerms) result = Run(q, allowed, true);
        return result;
    }

    List<(ClipItem item, double score)> Run(SearchQuery q, HashSet<int> allowed, bool fuzzy)
    {
        var result = new List<(ClipItem, double)>();
        if (!q.HasTerms)
        {
            foreach (int i in allowed) result.Add((entries[i].Item, 0));
            return result;
        }

        Dictionary<int, double>? overall = null;
        foreach (var term in q.Terms)
        {
            var hits = MatchTerm(term, allowed, fuzzy);
            if (hits.Count == 0) return result; // words are ANDed
            if (overall == null) { overall = hits; continue; }
            var merged = new Dictionary<int, double>();
            foreach (var kv in overall)
                if (hits.TryGetValue(kv.Key, out double next)) merged[kv.Key] = Math.Min(kv.Value, next) + 1;
            overall = merged;
            if (overall.Count == 0) return result;
        }
        if (overall == null) return result;
        foreach (var kv in overall) result.Add((entries[kv.Key].Item, kv.Value));
        return result;
    }

    static bool Pass(ClipItem i, SearchQuery q, DateTime from)
    {
        if (q.Kinds.Count > 0 && !q.Kinds.Contains(i.Kind)) return false;
        if (q.PinnedOnly && !i.Pinned) return false;
        if (q.Apps.Count > 0 && (i.SourceApp == null || !q.Apps.Contains(i.SourceApp))) return false;
        if (q.Tags.Count > 0 && (i.Tags == null || !q.Tags.Overlaps(i.Tags))) return false;
        if (q.Categories.Count > 0 && (i.FileCategory == null || !q.Categories.Contains(i.FileCategory))) return false;
        if (q.TaggedOnly && i.Tags is not { Count: > 0 }) return false;
        if (q.WithinDays > 0 && i.Time < from) return false;
        return true;
    }

    Dictionary<int, double> MatchTerm(string term, HashSet<int> allowed, bool fuzzy)
    {
        var hits = new Dictionary<int, double>();
        var candidates = Candidates(term);
        if (candidates == null) // short word: a full scan is cheaper than posting lookups
        {
            foreach (int idx in allowed)
            {
                double s = Score(entries[idx].Norm, term);
                if (s > 0) hits[idx] = s;
            }
        }
        else
        {
            var set = new HashSet<int>(candidates);
            foreach (int idx in allowed)
            {
                var norm = entries[idx].Norm;
                // Postings cover all of a short card; anything longer needs the fallback check.
                if (!set.Contains(idx) && norm.Length <= GramLimit) continue;
                double s = Score(norm, term);
                if (s > 0) hits[idx] = s;
            }
        }

        // Fuzzy matching is a deliberate fallback: only when an exact/pinyin match found nothing
        // anywhere, and only as an ordered subsequence (prefix + gaps), never an edit-distance
        // free-for-all that would bury good results under noise.
        if (hits.Count == 0 && fuzzy && term.Length >= 2)
            foreach (int idx in allowed)
                if (IsSubsequence(entries[idx].Norm, term)) hits[idx] = 25;
        return hits;
    }

    static double Score(string norm, string term)
    {
        int at = norm.IndexOf(term, StringComparison.Ordinal);
        return at < 0 ? 0 : 100 + Math.Max(0, 30 - Math.Min(30, at / 20));
    }

    List<int>? Candidates(string term)
    {
        if (term.Length < 3) return null;
        List<int>? acc = null;
        foreach (var gram in Grams(term))
        {
            if (!postings.TryGetValue(gram, out var list)) return new List<int>();
            acc = acc == null ? new List<int>(list) : Intersect(acc, list);
            if (acc.Count == 0) return acc;
        }
        return acc;
    }

    // Both lists are sorted ascending because Rebuild walks indices in order.
    static List<int> Intersect(List<int> a, List<int> b)
    {
        var result = new List<int>(Math.Min(a.Count, b.Count));
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (a[i] == b[j]) { result.Add(a[i]); i++; j++; }
            else if (a[i] < b[j]) i++;
            else j++;
        }
        return result;
    }

    static IEnumerable<string> Grams(string s)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + 3 <= s.Length; i++)
        {
            string g = s.Substring(i, 3);
            if (seen.Add(g)) yield return g;
        }
    }

    // Ordered-subsequence match with a span limit: the matched letters must be reasonably close
    // together, otherwise any long pinyin string can be made to "contain" something like zzzzz.
    static bool IsSubsequence(string hay, string needle)
    {
        int j = 0, start = -1;
        for (int i = 0; i < hay.Length && j < needle.Length; i++)
        {
            if (hay[i] != needle[j]) continue;
            if (start < 0) start = i;
            if (++j == needle.Length)
                return i - start + 1 <= needle.Length * 3 + 6;
        }
        return false;
    }
}
