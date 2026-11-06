using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Shengyi;

// Parse / render a global hotkey as text, e.g. "Ctrl+3" or "Ctrl+Alt+Shift+V".
// RegisterHotKey modifier bits: Alt=1, Ctrl=2, Shift=4, Win=8.
// Pure string + data logic (no WPF), so the console self-test can compile it; the settings
// window uses its own KeyInterop-based recorder for the primary shortcut, but these two
// auxiliary shortcuts are configured as text to keep the dialog simple.
static class KeyChord
{
    const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Space"] = 0x20, ["Tab"] = 0x09,
        ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Backspace"] = 0x08, ["Delete"] = 0x2E,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
        ["Insert"] = 0x2D, ["`"] = 0xC0, ["-"] = 0xBD, ["="] = 0xBB,
        ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC, [";"] = 0xBA,
        ["'"] = 0xDE, [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF,
    };

    public static bool TryParse(string? text, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false; // a bare key would hijack typing
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Ctrl; break;
                case "alt": mods |= Alt; break;
                case "shift": mods |= Shift; break;
                case "win": case "windows": case "meta": mods |= Win; break;
                default: return false;
            }
        }
        string key = parts[^1];
        if (!TryVk(key, out vk)) return false;
        return mods != 0 && vk != 0;
    }

    static bool TryVk(string key, out uint vk)
    {
        vk = 0;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') { vk = (uint)(c - 'A' + 0x41); return true; }
            if (c >= '0' && c <= '9') { vk = (uint)(c - '0' + 0x30); return true; }
        }
        if (Named.TryGetValue(key, out vk)) return true;
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int f)
            && f >= 1 && f <= 24)
        {
            vk = (uint)(0x6F + f);
            return true;
        }
        return false;
    }

    public static string Modifiers(uint mods)
    {
        var s = new StringBuilder();
        if ((mods & Ctrl) != 0) s.Append("Ctrl+");
        if ((mods & Alt) != 0) s.Append("Alt+");
        if ((mods & Shift) != 0) s.Append("Shift+");
        if ((mods & Win) != 0) s.Append("Win+");
        return s.ToString();
    }

    public static string KeyName(uint vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return ((char)(vk - 0x41 + 'A')).ToString();
        if (vk >= 0x30 && vk <= 0x39) return ((char)(vk - 0x30 + '0')).ToString();
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        foreach (var kv in Named)
            if (kv.Value == vk) return kv.Key;
        return "0x" + vk.ToString("X", CultureInfo.InvariantCulture);
    }

    public static string Describe(uint mods, uint vk) => Modifiers(mods) + KeyName(vk);
}
