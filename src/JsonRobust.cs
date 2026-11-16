using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Shengyi;

// 0.11.0 · tolerant JSON loading. JsonSerializer.Deserialize<T> throws on the FIRST bad field,
// which used to turn one corrupted value into a completely default settings object or an empty
// history. These helpers apply good fields/items and leave bad ones at their defaults, so a
// single broken value can no longer wipe out everything else. No third-party dependencies.
static class JsonRobust
{
    // Copies every readable property from a JSON object onto target. Returns the number of
    // fields that were present but could not be read (type changed, null for a value type, ...).
    public static int MergeObject<T>(string json, T target)
    {
        ArgumentNullException.ThrowIfNull(target);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("expected a JSON object");

        int bad = 0;
        foreach (var prop in typeof(T).GetProperties())
        {
            if (!prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
            if (!doc.RootElement.TryGetProperty(prop.Name, out var el)) continue;
            try
            {
                object? value = el.Deserialize(prop.PropertyType);
                if (value != null || Nullable.GetUnderlyingType(prop.PropertyType) != null)
                    prop.SetValue(target, value);
                else bad++;
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
            {
                bad++; // keep the field's existing default
            }
        }
        return bad;
    }

    // Reads a JSON array one element at a time; a malformed element is counted and skipped
    // instead of discarding the whole list. Returns (added, skipped).
    public static (int added, int skipped) MergeList<T>(string json, List<T> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("expected a JSON array");

        int added = 0, skipped = 0;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            try
            {
                var item = el.Deserialize<T>();
                if (item != null) { target.Add(item); added++; }
                else skipped++;
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
            {
                skipped++;
            }
        }
        return (added, skipped);
    }
}
