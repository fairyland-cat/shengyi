using System;
using System.Collections.Generic;

namespace Shengyi;

// Pure logic behind the two optional paste-mode chords: no WPF and no P/Invoke, so the
// console self-test can compile it. The settings window maps these codes to localised
// strings; MessageWindow.ProbeChord does the real RegisterHotKey check for conflicts
// with other applications.
enum ChordConflict { None, MainHotkey, NewSticky, OtherAux }
enum ChordAdvisory { None, WinReserved, InputMethod }

// Per-chord outcome of MessageWindow.SetAuxHotkeys, so a failure can name the exact
// shortcut instead of showing one vague "a shortcut was taken" balloon.
readonly record struct AuxHotkeyResult(bool NextOk, bool InsertOk);

static class HotkeyCheck
{
    // RegisterHotKey modifier bits: Alt=1, Ctrl=2, Shift=4, Win=8.
    const uint Alt = 0x1, Shift = 0x4, Win = 0x8;
    const uint ModMask = 0xF;

    // The fixed chord that opens a blank sticky note (see MessageWindow.SetNoteHotkey).
    public const uint StickyMods = 0x1 | 0x2;   // Ctrl+Alt
    public const uint StickyKey = 0x4E;         // N

    // MOD_NOREPEAT (0x4000) and any future flag bits do not change a chord's identity:
    // Windows keys hotkeys by (modifiers, virtual key) only.
    static bool Same(uint m1, uint k1, uint m2, uint k2) =>
        (m1 & ModMask) == (m2 & ModMask) && k1 == k2;

    // "self" is 1 for the next-clip chord and 2 for the quick-insert chord. A chord never
    // conflicts with its own previous value, but must not collide with the other enabled
    // one, with the shortcut that opens the line, or with Ctrl+Alt+N.
    public static ChordConflict SelfConflict(
        uint mods, uint vk, int self,
        uint mainMods, uint mainVk,
        bool nextEnabled, uint nextMods, uint nextVk,
        bool insertEnabled, uint insertMods, uint insertVk)
    {
        if (Same(mods, vk, mainMods, mainVk)) return ChordConflict.MainHotkey;
        if (Same(mods, vk, StickyMods, StickyKey)) return ChordConflict.NewSticky;
        if (self != 1 && nextEnabled && Same(mods, vk, nextMods, nextVk)) return ChordConflict.OtherAux;
        if (self != 2 && insertEnabled && Same(mods, vk, insertMods, insertVk)) return ChordConflict.OtherAux;
        return ChordConflict.None;
    }

    // RegisterHotKey can succeed for chords that Windows or an IME later swallows. These
    // are experience-based advisories only: they never block saving.
    public static ChordAdvisory Advisory(uint mods)
    {
        uint m = mods & ModMask;
        if ((m & Win) != 0) return ChordAdvisory.WinReserved;
        // Exactly Alt+Shift (no Ctrl / Win) is the classic input-language / layout switch.
        if ((m & Alt) != 0 && (m & Shift) != 0 && (m & ~(Alt | Shift)) == 0)
            return ChordAdvisory.InputMethod;
        return ChordAdvisory.None;
    }

    // Only ENABLED chords that actually failed are worth reporting: a switched-off
    // shortcut is a choice, not an error, and a successful one must never appear in the
    // warning. The order matches the settings panel (main, sticky, next, insert).
    public static string[] FailedChords(
        bool mainOk, string mainLabel,
        bool stickyOk, string stickyLabel,
        bool nextEnabled, bool nextOk, string nextLabel,
        bool insertEnabled, bool insertOk, string insertLabel)
    {
        var list = new List<string>();
        if (!mainOk) list.Add(mainLabel);
        if (!stickyOk) list.Add(stickyLabel);
        if (nextEnabled && !nextOk) list.Add(nextLabel);
        if (insertEnabled && !insertOk) list.Add(insertLabel);
        return list.ToArray();
    }
}
