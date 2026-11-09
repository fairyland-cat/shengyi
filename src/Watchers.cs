using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Shengyi;

// Hidden message-only window: receives the global hotkey and clipboard notifications.
sealed class MessageWindow : IDisposable
{
    const int HotkeyId = 1, NoteHotkeyId = 2, NextHotkeyId = 3, InsertHotkeyId = 4, ProbeHotkeyId = 5;
    const uint NoRepeat = 0x4000;   // MOD_NOREPEAT
    const uint NoteMods = 0x1 | 0x2;
    const uint NoteVk = 0x4E;
    readonly HwndSource source;

    // Mirror of what is registered right now, so ProbeChord can release everything and
    // faithfully put it back. ClearHotkey() is also used while the settings window records
    // a new primary chord; that suspended state must NOT be resurrected by a probe.
    bool mainOn, noteOn, nextOn, insertOn;
    uint mainMods, mainVk, nextMods, nextVk, insertMods, insertVk;

    public event Action? Hotkey;
    public event Action? NoteHotkey;
    public event Action? NextHotkey;      // paste the next clip in a sequence
    public event Action? InsertHotkey;    // quick insert without touching the clipboard
    public event Action? ClipboardChanged;

    public MessageWindow()
    {
        source = new HwndSource(new HwndSourceParameters("shengyiMessages") { ParentWindow = new IntPtr(-3) });
        source.AddHook(Hook);
        Native.AddClipboardFormatListener(source.Handle);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam == NoteHotkeyId) { NoteHotkey?.Invoke(); handled = true; }
        else if (msg == Native.WM_HOTKEY && wParam == HotkeyId) { Hotkey?.Invoke(); handled = true; }
        else if (msg == Native.WM_HOTKEY && wParam == NextHotkeyId) { NextHotkey?.Invoke(); handled = true; }
        else if (msg == Native.WM_HOTKEY && wParam == InsertHotkeyId) { InsertHotkey?.Invoke(); handled = true; }
        else if (msg == Native.WM_CLIPBOARDUPDATE) ClipboardChanged?.Invoke();
        return IntPtr.Zero;
    }

    public bool SetHotkey(uint mods, uint vk)
    {
        ClearHotkey();
        mainMods = mods; mainVk = vk;
        mainOn = Native.RegisterHotKey(source.Handle, HotkeyId, mods | NoRepeat, vk);
        return mainOn;
    }

    public void ClearHotkey()
    {
        Native.UnregisterHotKey(source.Handle, HotkeyId);
        mainOn = false;
    }

    // A second, fixed chord for "new sticky note" — notes are for catching a thought at any
    // moment, including while the line is hidden and has no keyboard focus.
    public bool SetNoteHotkey()
    {
        noteOn = Native.RegisterHotKey(source.Handle, NoteHotkeyId, NoteMods | NoRepeat, NoteVk);
        return noteOn;
    }

    // The two P2 shortcuts are optional and configured in Settings. MOD_NOREPEAT keeps a
    // held key from firing a whole sequence at once. Returning a per-chord result lets the
    // tray name the exact shortcut that lost a race for a chord.
    public AuxHotkeyResult SetAuxHotkeys(bool nextOnReq, uint nMods, uint nVk, bool insertOnReq, uint iMods, uint iVk)
    {
        ClearAuxHotkeys();
        nextMods = nMods; nextVk = nVk; insertMods = iMods; insertVk = iVk;
        if (nextOnReq) nextOn = Native.RegisterHotKey(source.Handle, NextHotkeyId, nextMods | NoRepeat, nextVk);
        if (insertOnReq) insertOn = Native.RegisterHotKey(source.Handle, InsertHotkeyId, insertMods | NoRepeat, insertVk);
        return new AuxHotkeyResult(!nextOnReq || nextOn, !insertOnReq || insertOn);
    }

    public void ClearAuxHotkeys()
    {
        Native.UnregisterHotKey(source.Handle, NextHotkeyId);
        Native.UnregisterHotKey(source.Handle, InsertHotkeyId);
        nextOn = insertOn = false;
    }

    // Windows has no "is this chord free?" API: the only authoritative answer is to try
    // RegisterHotKey (failure = ERROR_HOTKEY_ALREADY_REGISTERED, 1409). Our own four
    // chords would always look taken, so release them for the duration of the probe.
    // Verified on Windows 11: UnregisterHotKey followed by re-registering the same id and
    // chord succeeds, and MOD_NOREPEAT does not change a chord's identity.
    public bool ProbeChord(uint mods, uint vk)
    {
        bool hadMain = mainOn, hadNote = noteOn, hadNext = nextOn, hadInsert = insertOn;
        var hwnd = source.Handle;
        foreach (int id in new[] { HotkeyId, NoteHotkeyId, NextHotkeyId, InsertHotkeyId })
            Native.UnregisterHotKey(hwnd, id);

        bool free = Native.RegisterHotKey(hwnd, ProbeHotkeyId, mods | NoRepeat, vk);
        if (free) Native.UnregisterHotKey(hwnd, ProbeHotkeyId);

        if (hadMain) mainOn = Native.RegisterHotKey(hwnd, HotkeyId, mainMods | NoRepeat, mainVk);
        if (hadNote) noteOn = Native.RegisterHotKey(hwnd, NoteHotkeyId, NoteMods | NoRepeat, NoteVk);
        if (hadNext) nextOn = Native.RegisterHotKey(hwnd, NextHotkeyId, nextMods | NoRepeat, nextVk);
        if (hadInsert) insertOn = Native.RegisterHotKey(hwnd, InsertHotkeyId, insertMods | NoRepeat, insertVk);
        return free;
    }

    public void Dispose()
    {
        ClearHotkey();
        Native.UnregisterHotKey(source.Handle, NoteHotkeyId);
        noteOn = false;
        ClearAuxHotkeys();
        Native.RemoveClipboardFormatListener(source.Handle);
        source.Dispose();
    }
}

// Win+V is reserved by Windows, so RegisterHotKey can't have it; a low-level hook takes it instead.
// Only installed while the setting is on.
sealed class KeyboardHook : IDisposable
{
    const int VK_V = 0x56, VK_ESCAPE = 0x1B;
    readonly Native.HookProc proc;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    IntPtr hook;
    bool swallowing;

    public event Action? WinV;
    public event Action? Escape;

    // Only while a paste sequence is running. Esc is swallowed so it cancels the sequence instead
    // of reaching the target app; the hook is enabled on demand by AppController.
    public bool EscapeEnabled { get; set; }

    public KeyboardHook() => proc = Callback;

    public bool Enabled
    {
        get => hook != IntPtr.Zero;
        set
        {
            if (value == Enabled) return;
            if (value) hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
            else
            {
                Native.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            bool injected = (k.flags & 0x10) != 0;
            if (EscapeEnabled && k.vkCode == VK_ESCAPE && !injected)
            {
                if ((int)wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
                    dispatcher.BeginInvoke(() => Escape?.Invoke());
                return new IntPtr(1); // swallow both press and release
            }
            if (k.vkCode == VK_V && !injected)
            {
                bool down = (int)wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                if (down && (swallowing || WinOnly()))
                {
                    if (!swallowing)
                    {
                        swallowing = true;
                        dispatcher.BeginInvoke(Fire);
                    }
                    return new IntPtr(1);
                }
                if (!down && swallowing)
                {
                    swallowing = false;
                    return new IntPtr(1);
                }
            }
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    static bool WinOnly() => (Native.KeyDown(0x5B) || Native.KeyDown(0x5C))
        && !Native.KeyDown(0x10) && !Native.KeyDown(0x11) && !Native.KeyDown(0x12);

    void Fire()
    {
        // A stray key between Win down and Win up keeps the Start menu from opening on release.
        Native.SendKeys((0xE8, false), (0xE8, true));
        WinV?.Invoke();
    }

    public void Dispose() => Enabled = false;
}

// Records everything copied: text (with its formatting), links, colours, images and files.
sealed class ClipboardWatcher
{
    public const string SelfFormat = "shengyi.Self";
    static readonly uint SelfId = Native.RegisterClipboardFormat(SelfFormat);
    static readonly uint ExcludeFormat = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    static readonly uint ViewerIgnoreFormat = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
    static readonly uint HistoryFormat = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
    const int MaxText = 1_000_000, MaxRich = 4_000_000, MaxImageBytes = 32_000_000;

    readonly Func<Settings> settings;
    readonly Action<ClipItem, BitmapSource?> onClip;
    readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(150) };
    string? owner;

    public DateTime PausedUntil { get; set; } = DateTime.MinValue;
    public bool Paused => DateTime.Now < PausedUntil;

    public ClipboardWatcher(Func<Settings> settings, Action<ClipItem, BitmapSource?> onClip)
    {
        this.settings = settings;
        this.onClip = onClip;
        settle.Tick += (_, _) => { settle.Stop(); Read(); };
    }

    // Wait a moment so the owner finishes putting all its formats on the clipboard.
    public void Notify()
    {
        if (Paused) return;
        owner = Native.ProcessName(Native.GetClipboardOwner()) ?? Native.ProcessName(Native.GetForegroundWindow());
        settle.Stop();
        settle.Start();
    }

    bool Excluded(string? app)
    {
        if (app == null) return false;
        foreach (var raw in settings().ExcludedApps)
        {
            var name = raw.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.Length > 0 && string.Equals(name, app, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    void Read()
    {
        // Our own copies, and entries that password managers mark private.
        if (Native.IsClipboardFormatAvailable(SelfId) || Native.IsClipboardFormatAvailable(ExcludeFormat)
            || Native.IsClipboardFormatAvailable(ViewerIgnoreFormat) || Excluded(owner)) return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { Capture(); return; }
            catch (Exception e) when (e is ExternalException or IOException)
            {
                Thread.Sleep(60); // someone else has the clipboard open
            }
        }
    }

    void Capture()
    {
        if (Native.IsClipboardFormatAvailable(HistoryFormat)
            && Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream h
            && h.Length >= 4 && new BinaryReader(h).ReadInt32() == 0) return;

        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList().Cast<string>().Where(f => !string.IsNullOrEmpty(f)).ToList();
            if (files.Count > 0) onClip(Enrich(new ClipItem { Kind = ClipKind.Files, Files = files, Hash = Classify.HashFiles(files) }), null);
            return;
        }
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxText) return;
            onClip(Enrich(new ClipItem
            {
                Kind = Classify.Text(text),
                Text = text,
                Html = Rich(TextDataFormat.Html),
                Rtf = Rich(TextDataFormat.Rtf),
                Hash = Classify.HashText(text),
            }), null);
            return;
        }
        if ((Clipboard.ContainsImage() || HasRawImage()) && ReadImage() is { } image)
            onClip(Enrich(new ClipItem
            {
                Kind = ClipKind.Image,
                Hash = ImageHash.Of(image.Bitmap),
                ImageFormat = image.FormatId,
                OriginalBytes = image.Bytes,
            }), image.Bitmap);
    }

    // Attach the provenance that makes "只搜浏览器里复制的内容" possible. The owner was already
    // sampled in Notify(); the format list is best-effort and never blocks a capture.
    ClipItem Enrich(ClipItem item)
    {
        // Provenance is optional for privacy. The file category is deliberately NOT set here:
        // ClipStore.Add derives it for every caller, including screenshots and old history.
        if (!settings().RecordSourceApp) return item;
        item.SourceApp = owner;
        item.Formats = ReadFormats();
        return item;
    }

    static List<string>? ReadFormats()
    {
        try
        {
            var names = Clipboard.GetDataObject()?.GetFormats(false) ?? Array.Empty<string>();
            var list = names.Where(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 64)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToList();
            return list.Count == 0 ? null : list;
        }
        catch (Exception e) when (e is ExternalException or IOException or UnauthorizedAccessException) { return null; }
    }

    static string? Rich(TextDataFormat format)
    {
        if (!Clipboard.ContainsText(format)) return null;
        var s = Clipboard.GetText(format);
        return string.IsNullOrEmpty(s) || s.Length > MaxRich ? null : s;
    }

    // Some apps publish PNG/JFIF/GIF without a CF_BITMAP, so ContainsImage() alone is not enough.
    static bool HasRawImage()
    {
        try
        {
            var formats = Clipboard.GetDataObject()?.GetFormats(false) ?? Array.Empty<string>();
            foreach (var f in formats)
                if (ImageFormats.TryMatch(f, out _)) return true;
        }
        catch (ExternalException) { }
        return false;
    }

    // The decoded bitmap is for the card and the fingerprint; the raw bytes (when the source app
    // supplied a real image file) are what we store and later put back on the clipboard.
    sealed class CapturedImage
    {
        public BitmapSource Bitmap = null!;
        public byte[]? Bytes;
        public string? FormatId;
    }

    static CapturedImage? ReadImage()
    {
        try
        {
            var formats = Clipboard.GetDataObject()?.GetFormats(false) ?? Array.Empty<string>();
            string? id = ImageFormats.Best(formats, out string? readFormat);
            if (id != null && readFormat != null && Clipboard.GetData(readFormat) is MemoryStream stream)
            {
                var bytes = stream.ToArray();
                if (bytes.Length > 0 && bytes.Length <= MaxImageBytes
                    && Decode(new MemoryStream(bytes)) is { } exact)
                    return new CapturedImage { Bitmap = exact, Bytes = bytes, FormatId = id };
            }
        }
        catch (Exception e) when (e is ExternalException or IOException or UnauthorizedAccessException)
        {
            // fall through to the GDI+ conversion
        }

        // CF_DIB and friends have no file header, or the format is one WPF cannot decode. A
        // lossless PNG conversion keeps the pixels (and still restores the image on paste).
        try
        {
            using var img = Forms.Clipboard.GetImage();
            if (img == null) return null;
            var ms = new MemoryStream();
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            if (Decode(ms) is not { } converted) return null;
            return new CapturedImage { Bitmap = converted, Bytes = null, FormatId = null };
        }
        catch (Exception e) when (e is ExternalException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    static BitmapSource? Decode(Stream s)
    {
        try
        {
            s.Position = 0;
            var frame = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
