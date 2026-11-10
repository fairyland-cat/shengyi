using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Shengyi;

// 0.8.0 · The smart-order rolling window is measured in MINUTES, not days.
// Deliberately free of WPF / WinForms (like CardTints.cs) so _verify can assert the
// clamping, the legacy conversion and the cutoff arithmetic without a UI framework.
static class FreqWindow
{
    public const int MinutesPerDay = 24 * 60;
    public const int MinMinutes = 1;

    // ClipStore only keeps use stamps for the last 90 days (KeepUseDays), so a longer
    // window could never be satisfied. 90 × 1440 = 129600 minutes.
    public const int MaxMinutes = 90 * MinutesPerDay;

    // Same effective window as the old 7-day default. New installs and old installs with
    // no settings file keep exactly the behaviour they had in 0.7.1.
    public const int DefaultMinutes = 7 * MinutesPerDay;

    public static int Clamp(int minutes) => Math.Clamp(minutes, MinMinutes, MaxMinutes);

    // settings.json written by 0.7.1 and earlier stored 1..90 days. Convert rather than
    // reinterpret: a legacy "7" must mean 7 days (10080 minutes), never 7 minutes.
    public static int FromLegacyDays(int days) =>
        (int)Math.Clamp((long)days * MinutesPerDay, MinMinutes, MaxMinutes);

    // settings.json migration. The new key always wins; a legacy-only "FreqWindowDays"
    // is converted; malformed / unrelated JSON leaves the caller's value untouched.
    // Pure so _verify can exercise the real 0.7.1 file shape without touching disk.
    public static int FromLegacyJson(string json, int currentMinutes)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return currentMinutes;
            if (root.TryGetProperty("FreqWindowMinutes", out var modern) && modern.TryGetInt32(out int minutes))
                return Clamp(minutes);
            if (root.TryGetProperty("FreqWindowDays", out var legacy) && legacy.TryGetInt32(out int days))
                return FromLegacyDays(days);
            return currentMinutes;
        }
        catch { return currentMinutes; }
    }

    public static long CutoffUtcMs(int minutes, long nowUtcMs) =>
        nowUtcMs - (long)Clamp(minutes) * 60_000L;

    public static long CutoffUtcMs(int minutes) =>
        CutoffUtcMs(minutes, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    // Stamps are oldest first, so the first stale one ends the backwards scan.
    public static int CountUses(IReadOnlyList<long>? times, long cutoffUtcMs)
    {
        if (times is not { Count: > 0 }) return 0;
        int n = 0;
        for (int i = times.Count - 1; i >= 0 && times[i] >= cutoffUtcMs; i--) n++;
        return n;
    }
}
