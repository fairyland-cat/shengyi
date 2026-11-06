using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Shengyi;

sealed class Settings
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "shengyi");
    static string FilePath => Path.Combine(DataDir, "settings.json");

    public bool ClipboardWelcomeShown { get; set; }
    public uint HotkeyModifiers { get; set; } = 0x1 | 0x2; // Alt + Ctrl
    public uint HotkeyKey { get; set; } = 0x56;            // V
    public bool ReplaceWinV { get; set; }
    public bool PasteOnClick { get; set; }
    public bool RevealAtTopEdge { get; set; } = true;
    public int MaxItems { get; set; } = 200;
    public int RetentionDays { get; set; }                 // 0 = keep forever
    public List<string> ExcludedApps { get; set; } = new() { "1Password", "Bitwarden", "KeePass", "KeePassXC" };

    // Search. Both are local-only: no data leaves the machine, and turning them off keeps the
    // old behaviour (plain substring search, no provenance written into clips.json).
    public bool RecordSourceApp { get; set; } = true;  // also stores the clipboard format list
    public bool FuzzySearch { get; set; } = true;      // subsequence fallback when nothing matches
    public bool SmartTagging { get; set; } = true;     // local regex entity tags; nothing leaves the machine

    // P2 · paste modes. Both global shortcuts are OFF by default: an unrequested Ctrl+3 or
    // Ctrl+Alt+Shift+V would otherwise hijack another application's shortcut.
    public bool MergeNumbering { get; set; }                    // "1. ", "2. " in merged text
    public bool QuickInsertEnabled { get; set; }
    public uint QuickInsertModifiers { get; set; } = 0x2;     // Ctrl
    public uint QuickInsertKey { get; set; } = 0x33;          // 3  → Ctrl+3
    public bool QuickInsertCopyOnly { get; set; }             // fallback: copy instead of typing
    public bool SequenceHotkeyEnabled { get; set; }
    public uint SequenceModifiers { get; set; } = 0x7;        // Ctrl+Alt+Shift
    public uint SequenceKey { get; set; } = 0x56;             // V  → Ctrl+Alt+Shift+V
    public int SequenceIntervalMs { get; set; } = 700;        // auto-sequence delay
    public bool SequenceCopyOnly { get; set; }                // fallback for terminals / RDP

    // Appearance. Defaults reproduce the original 绳忆 look exactly.
    public string ThemeName { get; set; } = "soft";        // soft | silver | linen | porcelain | graphite
    public string Accent { get; set; } = "blue";           // blue | terra | mint | navy
    public string CardColor { get; set; } = "auto";        // auto, or a #RRGGBB card colour
    public int RopeSag { get; set; } = 4;                  // 0..48, the rope's dip in DIPs
    public int CardSizePercent { get; set; } = 100;        // 70..150

    // Frequency ordering: after a card is used this many times within the window, it joins the
    // "frequently used" group at the front. 0 switches the whole feature off.
    public int FreqThreshold { get; set; } = 3;           // 0..20
    public int FreqWindowDays { get; set; } = 7;          // 1..90, the rolling window

    // Paper colour used when a new sticky note is created. "auto" follows the active theme.
    public string NoteColor { get; set; } = "auto";

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this))!;
}

static class HotkeyText
{
    // RegisterHotKey modifier bits match WPF's ModifierKeys: Alt=1, Ctrl=2, Shift=4, Win=8.
    public static string Modifiers(uint mods)
    {
        var s = "";
        if ((mods & 0x2) != 0) s += "Ctrl+";
        if ((mods & 0x1) != 0) s += "Alt+";
        if ((mods & 0x4) != 0) s += "Shift+";
        if ((mods & 0x8) != 0) s += "Win+";
        return s;
    }

    // Delegated to the dependency-free parser so the settings dialog, the tray balloon and the
    // README always spell a chord the same way.
    public static string Describe(uint mods, uint vk) => KeyChord.Describe(mods, vk);
}

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(S.AppName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(S.AppName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(S.AppName, false);
        }
    }
}
