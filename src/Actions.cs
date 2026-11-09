using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Shengyi;

// Everything you can do with a clip. P2 adds four ways of taking several clips at once:
// merge (one clipboard entry), a paste sequence (one clip per hotkey / timer tick), quick
// insert (Unicode injection, clipboard untouched) and lossless image round-tripping.
sealed class Actions
{
    readonly ClipStore store;
    readonly RevealController reveal;
    readonly Func<Settings> settings;

    // Ctrl+Click selection, shared by every LineWindow because all monitors use one Actions.
    // A list, not a set: the merge output follows the order the cards were picked in.
    readonly List<ClipItem> multi = new();
    readonly Queue<ClipItem> sequence = new();
    readonly DispatcherTimer seqTimer = new();
    IntPtr seqTarget;
    bool seqAuto, seqCopyOnly;

    public event Action? MultiChanged;
    public event Action? SequenceChanged;
    public Action<string>? Notify { get; set; }   // tray balloon, wired by AppController

    public bool SequenceActive => sequence.Count > 0 || seqTimer.IsEnabled;
    public IReadOnlyList<ClipItem> MultiItems => multi;

    public Actions(ClipStore store, RevealController reveal, Func<Settings> settings)
    {
        this.store = store;
        this.reveal = reveal;
        this.settings = settings;
        seqTimer.Tick += (_, _) => SequenceNext();
    }

    // Click / Enter. Copies by default; Shift does the other thing. Settings can swap the two.
    public void Primary(ClipItem item, bool alternate, CardView? view)
    {
        if (settings().PasteOnClick != alternate) Paste(item, plain: false);
        else if (Copy(item, plain: false)) view?.Flash(S.Copied);
    }

    public bool Copy(ClipItem item, bool plain)
    {
        // WinForms' clipboard, not WPF's: WPF's SetDataObject fails on some machines (CLIPBRD_E_CANT_OPEN / E_NOTIMPL).
        var data = new Forms.DataObject();
        data.SetData(ClipboardWatcher.SelfFormat, new MemoryStream(new byte[] { 1 })); // so we don't record it again
        switch (item.Kind)
        {
            case ClipKind.Image:
                byte[] bytes;
                try { bytes = File.ReadAllBytes(item.ImagePath!); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
                bool placed = false;
                // CF_BITMAP is the widely understood flavour; some formats (WebP) have no GDI+
                // decoder, in which case the original format below is the only representation.
                try
                {
                    using var ms = new MemoryStream(bytes);
                    using var bmp = new System.Drawing.Bitmap(ms);
                    data.SetImage(bmp);
                    placed = true;
                }
                catch (Exception e) when (e is ArgumentException or ExternalException or IOException) { }
                string? write = ImageFormats.WriteFormat(item.ImageFormat);
                if (write != null)
                {
                    data.SetData(write, false, new MemoryStream(bytes));
                    placed = true;
                }
                if (!placed) return false;
                break;
            case ClipKind.Files:
                var files = new StringCollection();
                foreach (var f in item.Files ?? new())
                    if (File.Exists(f) || Directory.Exists(f)) files.Add(f);
                if (files.Count == 0) return false;
                data.SetFileDropList(files);
                break;
            default:
                data.SetText(string.IsNullOrEmpty(item.Text) ? " " : item.Text, Forms.TextDataFormat.UnicodeText);
                if (!plain && !string.IsNullOrEmpty(item.Html)) data.SetText(item.Html, Forms.TextDataFormat.Html);
                if (!plain && !string.IsNullOrEmpty(item.Rtf)) data.SetText(item.Rtf, Forms.TextDataFormat.Rtf);
                break;
        }
        try
        {
            Forms.Clipboard.SetDataObject(data, true, 10, 50);
            // Paste(), the item menu and "Save and copy" all come through here, so one call site
            // covers every take without counting a paste twice.
            store.RecordUse(item);
            return true;
        }
        catch (ExternalException) { return false; }
    }

    IntPtr TargetWindow() => reveal.PasteTarget != IntPtr.Zero ? reveal.PasteTarget : Native.GetForegroundWindow();

    // Puts the clip on the clipboard, returns focus to the app you were in and presses Ctrl+V there.
    public async void Paste(ClipItem item, bool plain)
    {
        var target = TargetWindow();
        if (!Copy(item, plain)) return;
        reveal.RetractAll();
        await PasteInto(target);
    }

    // Shared by a single paste, merge paste and each step of a sequence.
    public async Task PasteInto(IntPtr target)
    {
        if (target == IntPtr.Zero || Native.IsOwn(target)) return;
        Native.ForceForeground(target);
        await Task.Delay(90);
        // Shift is usually still held (Shift+click, Shift+Enter); Ctrl+Shift+V would mean something else.
        foreach (ushort vk in new ushort[] { 0x10, 0x11 })
            if (Native.KeyDown(vk)) Native.SendKeys((vk, true));
        Native.SendKeys((0x11, false), (0x56, false), (0x56, true), (0x11, true));
    }

    // ---- multi selection -----------------------------------------------------

    public bool IsMulti(ClipItem item) => multi.Contains(item);

    public void ToggleMulti(ClipItem item)
    {
        if (!multi.Remove(item)) multi.Add(item);
        MultiChanged?.Invoke();
    }

    public void ClearMulti()
    {
        if (multi.Count == 0) return;
        multi.Clear();
        MultiChanged?.Invoke();
    }

    // ---- merge paste ---------------------------------------------------------

    public void Merge(bool paste)
    {
        if (multi.Count < 2) { Notify?.Invoke(S.MergeNeedTwo); return; }
        var items = multi.ToList();
        var result = MergeText.Build(items, settings().MergeNumbering);
        if (result.Used == 0) { Notify?.Invoke(S.MergeNoText); return; }

        var data = new Forms.DataObject();
        data.SetData(ClipboardWatcher.SelfFormat, new MemoryStream(new byte[] { 1 }));
        data.SetText(result.Text, Forms.TextDataFormat.UnicodeText);
        try { Forms.Clipboard.SetDataObject(data, true, 10, 50); }
        catch (ExternalException) { return; }

        foreach (var item in items) store.RecordUse(item);
        var target = TargetWindow();
        ClearMulti();
        reveal.RetractAll();
        if (paste) _ = PasteInto(target);
        else Notify?.Invoke(string.Format(S.MergeCopied, result.Used, result.Skipped));
    }

    // ---- paste sequence ------------------------------------------------------

    // Manual mode pastes the first clip immediately and waits for the "next" hotkey; auto mode
    // walks the queue on a timer. Esc cancels either. Set "copy only" in Settings as a fallback
    // for terminals / remote desktops where simulated Ctrl+V is unreliable.
    public void StartSequence(IReadOnlyList<ClipItem> items, bool auto)
    {
        if (items.Count == 0) return;
        // Manual mode advances with a global hotkey. Refusing to start is clearer than a queue
        // that silently cannot be advanced because the user never enabled the shortcut.
        if (!auto && !settings().SequenceHotkeyEnabled) { Notify?.Invoke(S.SequenceNeedHotkey); return; }
        seqTimer.Stop();
        sequence.Clear();
        foreach (var item in items) sequence.Enqueue(item);
        seqAuto = auto;
        seqCopyOnly = settings().SequenceCopyOnly;
        seqTarget = TargetWindow();
        ClearMulti();
        reveal.RetractAll();

        if (auto)
        {
            seqTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings().SequenceIntervalMs, 150, 10000));
            seqTimer.Start();
        }
        SequenceChanged?.Invoke();
        // Manual mode has already sent clip #1 below. Say so explicitly: the chord only
        // ADVANCES a queue, it never starts one. Auto mode needs no chord at all.
        Notify?.Invoke(auto
            ? string.Format(S.SequenceStartedAuto, sequence.Count,
                Math.Clamp(settings().SequenceIntervalMs, 150, 10000))
            : string.Format(S.SequenceStartedManual, sequence.Count,
                KeyChord.Describe(settings().SequenceModifiers, settings().SequenceKey)));
        if (!auto) SequenceNext(); // first clip goes out immediately; the hotkey advances the rest
    }

    public void SequenceNext()
    {
        if (sequence.Count == 0)
        {
            // The normal end of a queue goes through StopSequence() right after the last
            // dequeue, so reaching here means the chord was pressed with nothing running.
            // Explain how to start one instead of silently doing nothing (the old behaviour).
            if (!seqTimer.IsEnabled) Notify?.Invoke(S.SequenceNoQueue);
            StopSequence(notify: false);
            return;
        }
        var item = sequence.Dequeue();
        bool copied = Copy(item, false);
        if (copied && !seqCopyOnly) _ = PasteInto(seqTarget);
        if (sequence.Count == 0)
        {
            StopSequence(notify: false);
            Notify?.Invoke(S.SequenceFinished); // "finished", not the Esc "stopped" message
        }
        else if (!seqAuto) SequenceChanged?.Invoke();
    }

    public void StopSequence(bool notify = true)
    {
        bool wasActive = SequenceActive;
        seqTimer.Stop();
        sequence.Clear();
        SequenceChanged?.Invoke();
        if (notify && wasActive) Notify?.Invoke(S.SequenceStopped);
    }

    // ---- quick insert --------------------------------------------------------

    // Injects Unicode characters straight into the focused control. The clipboard is never read
    // or written, so whatever the user copied before stays there.
    public async void QuickInsert(ClipItem? chosen = null)
    {
        var fg = Native.GetForegroundWindow();
        bool fromLine = Native.IsOwn(fg);
        IntPtr target = fromLine ? reveal.PasteTarget : IntPtr.Zero;

        // Pick BEFORE retracting: while the line is out, an active search box should win over
        // "latest text". RetractAll() clears IsOut and would lose that context.
        var item = chosen ?? PickInsertItem();
        if (fromLine && (target == IntPtr.Zero || !Native.IsWindow(target)))
        { Notify?.Invoke(S.QuickInsertNoTarget); return; }
        if (item == null || string.IsNullOrEmpty(item.Text)) { Notify?.Invoke(S.QuickInsertNothing); return; }
        if (settings().QuickInsertCopyOnly)
        {
            if (Copy(item, true)) Notify?.Invoke(S.QuickInsertCopied);
            return;
        }
        if (fromLine) reveal.RetractAll(); // hands focus back to the window we were typing in
        store.RecordUse(item);

        // Same 90 ms settling that PasteInto() uses. Without it, a chord pressed while the
        // line is out types into the closing line window (or nowhere). The wait also gives
        // the user time to release the hotkey's own modifiers.
        if (target != IntPtr.Zero) Native.ForceForeground(target);
        await Task.Delay(90);
        if (Native.IsOwn(Native.GetForegroundWindow()))
        { Notify?.Invoke(S.QuickInsertNoTarget); return; }
        // A still-held Ctrl/Alt/Shift would turn the injected characters into shortcuts.
        // PasteInto() releases the same keys before simulating Ctrl+V.
        foreach (ushort vk in new ushort[] { 0x10, 0x11, 0x12 })
            if (Native.KeyDown(vk)) Native.SendKeys((vk, true));
        Native.SendUnicode(item.Text!);
    }

    ClipItem? PickInsertItem()
    {
        var win = reveal.ActiveKeyboardWindow;
        if (win is { KeyboardMode: true } && win.Query.Trim().Length > 0)
        {
            var top = win.TopTextResult;
            if (top != null) return top;
        }
        return store.Items.FirstOrDefault(i => (i.IsText || i.IsSticky) && !string.IsNullOrWhiteSpace(i.Text));
    }

    // ---- the rest of the item actions ----------------------------------------

    // Double click.
    public void Open(ClipItem item)
    {
        switch (item.Kind)
        {
            case ClipKind.Image:
                reveal.RetractAll();
                Shell(item.ImagePath!);
                break;
            case ClipKind.Link:
                reveal.RetractAll();
                Shell(item.Text!.Trim());
                break;
            case ClipKind.Files:
                if (item.Files?.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p)) is { } f)
                {
                    reveal.RetractAll();
                    Shell(f);
                }
                break;
            default:
                Edit(item);
                break;
        }
    }

    // Press and hold: Paint for images, the text editor for text, the note editor for stickies.
    public void Edit(ClipItem item)
    {
        reveal.RetractAll();
        if (item.Kind == ClipKind.Image)
        {
            try { Process.Start(new ProcessStartInfo("mspaint.exe", $"\"{item.ImagePath}\"") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { Shell(item.ImagePath!, "edit"); }
        }
        else if (item.Kind == ClipKind.Files) ShowInFolder(item);
        else if (item.IsSticky) Front(new StickyEditWindow(item, store, this, false));
        else Front(new TextEditWindow(item, store, this));
    }

    // A fresh note only exists in memory until it is saved, so cancelling leaves no empty card.
    public void NewSticky()
    {
        reveal.RetractAll();
        var item = new ClipItem { Kind = ClipKind.Sticky, Text = "", NoteColor = settings().NoteColor };
        Front(new StickyEditWindow(item, store, this, true));
    }

    public void QuickLook(ClipItem item)
    {
        reveal.RetractAll();
        Front(new QuickLookWindow(item, Forms.Screen.FromPoint(Forms.Cursor.Position)));
    }

    // Notes are permanent by design, so losing one to a stray click on the little ✕ is worth
    // one confirmation. History cards keep the old one-click behaviour.
    public void Remove(ClipItem item)
    {
        if (item.IsSticky && store.Items.Contains(item)
            && MessageBox.Show(S.DeleteStickyConfirm, S.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        multi.Remove(item);
        store.Remove(item);
    }

    public void TogglePin(ClipItem item) => store.SetPinned(item, !item.Pinned);

    public void ShowInFolder(ClipItem item)
    {
        var path = item.Kind == ClipKind.Image ? item.ImagePath : item.Files?.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
        if (path == null) return;
        reveal.RetractAll();
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    public void SaveAs(ClipItem item)
    {
        reveal.RetractAll();
        bool image = item.Kind == ClipKind.Image;
        string ext = image ? Path.GetExtension(item.ImagePath ?? "") : "";
        if (ext.Length < 2) ext = ".png";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"Clip {item.Time:yyyy-MM-dd HH.mm.ss}",
            DefaultExt = image ? ext : ".txt",
            Filter = image ? "Image|*" + ext : "Text|*.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (image) File.Copy(item.ImagePath!, dlg.FileName, true);
            else File.WriteAllText(dlg.FileName, item.Text ?? "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // Text drags as text; images drag as a file with a readable name (plus the bitmap); files as themselves.
    // Always a copy: the clip stays on the line.
    public void DragOut(ClipItem item, CardView view)
    {
        var data = new DataObject();
        switch (item.Kind)
        {
            case ClipKind.Image:
                if (DragCopy(item) is { } file) data.SetFileDropList(new StringCollection { file });
                if (Thumbs.LoadFull(item.ImagePath!) is { } bmp) data.SetImage(bmp);
                break;
            case ClipKind.Files:
                var files = new StringCollection();
                foreach (var f in item.Files ?? new())
                    if (File.Exists(f) || Directory.Exists(f)) files.Add(f);
                if (files.Count == 0) return;
                data.SetFileDropList(files);
                break;
            default:
                data.SetText(item.Text ?? "");
                break;
        }
        reveal.HoldOpen++;
        try
        {
            // A cancelled drag is not a take.
            if (DragDrop.DoDragDrop(view, data, DragDropEffects.Copy) != DragDropEffects.None)
                store.RecordUse(item);
        }
        finally { reveal.HoldOpen--; }
    }

    static string? DragCopy(ClipItem item)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "shengyi");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, $"Image {item.Time:yyyy-MM-dd HH.mm.ss}{Path.GetExtension(item.ImagePath)}");
            File.Copy(item.ImagePath!, dest, true);
            return dest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public void ShowMenu(ClipItem item, CardView view)
    {
        var menu = new ContextMenu { PlacementTarget = view, Placement = PlacementMode.MousePoint };
        void Add(string text, Action action)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        Add(S.Copy, () => { if (Copy(item, false)) view.Flash(S.Copied); });
        Add(S.Paste, () => Paste(item, false));
        if (item.IsRich) Add(S.PastePlain, () => Paste(item, true));
        Add(S.QuickInsertThis, () => QuickInsert(item));

        if (multi.Count >= 2)
        {
            menu.Items.Add(new Separator());
            Add(string.Format(S.MergeCopyMany, multi.Count), () => Merge(false));
            Add(string.Format(S.MergePasteMany, multi.Count), () => Merge(true));
            Add(string.Format(S.SequenceManualMany, multi.Count), () => StartSequence(MultiItems.ToList(), false));
            Add(string.Format(S.SequenceAutoMany, multi.Count), () => StartSequence(MultiItems.ToList(), true));
            Add(S.MultiClear, () => ClearMulti());
        }
        menu.Items.Add(new Separator());
        if (item.IsSticky)
        {
            Add(S.EditSticky, () => Edit(item));
            var colors = new MenuItem { Header = S.NoteColorMenu };
            for (int i = 0; i < Theme.NotePresets.Length; i++)
            {
                int index = i;
                var mi = new MenuItem { Header = S.NoteColorNames[index] };
                mi.Click += (_, _) => store.UpdateSticky(item, item.Text ?? "", Theme.NotePresets[index]);
                colors.Items.Add(mi);
            }
            menu.Items.Add(colors);
            Add(S.QuickLook, () => QuickLook(item));
            Add(S.SaveAs, () => SaveAs(item));
        }
        else
        {
            Add(item.Pinned ? S.Unpin : S.Pin, () => TogglePin(item));
            if (item.Kind == ClipKind.Image) Add(S.EditImage, () => Edit(item));
            else if (item.IsText) Add(S.Edit, () => Edit(item));
            if (item.Kind == ClipKind.Link) Add(S.OpenLink, () => Open(item));
            else if (item.Kind is ClipKind.Image or ClipKind.Files) Add(S.Open, () => Open(item));
            Add(S.QuickLook, () => QuickLook(item));
            if (item.Kind is ClipKind.Image or ClipKind.Files) Add(S.ShowInFolder, () => ShowInFolder(item));
            if (item.Kind != ClipKind.Files) Add(S.SaveAs, () => SaveAs(item));
        }
        menu.Items.Add(new Separator());
        Add(item.IsSticky ? S.DeleteSticky : S.Remove, () => Remove(item));

        reveal.HoldOpen++;
        menu.Closed += (_, _) => reveal.HoldOpen--;
        menu.IsOpen = true;
    }

    static void Front(Window w)
    {
        w.Show();
        Native.ForceForeground(new WindowInteropHelper(w).Handle);
        w.Activate();
    }

    static void Shell(string path, string? verb = null)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = verb ?? "" }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
