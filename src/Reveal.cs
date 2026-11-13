using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Shengyi;

// Decides when the line comes out. Resting the pointer on the top edge shows it on that monitor
// (mouse mode); moving away or clicking elsewhere puts it back. The shortcut opens keyboard mode,
// which stays until Esc, a pick, or focus moves elsewhere.
sealed class RevealController
{
    static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(320), Linger = TimeSpan.FromMilliseconds(400);
    const double CloseButtonCorner = 140; // DIPs at the top-right left alone for maximized windows' ✕
    const double EdgeDepth = 5;           // DIPs of hot strip along the very top, scaled to physical pixels

    readonly Func<IReadOnlyList<LineWindow>> windows;
    readonly Func<bool> edgeEnabled;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    bool armed = true, lastButton, lastAtEdge;
    DateTime? edgeSince, outsideSince;
    LineWindow? edgeWindow;

    // While > 0 (dragging, context menu open) the line stays out no matter what.
    public int HoldOpen { get; set; }

    // The window that had focus when keyboard mode opened; pasting goes back there.
    public IntPtr PasteTarget { get; private set; }

    // Quick insert prefers the first text result of an open, filtered line over "latest text".
    public LineWindow? ActiveKeyboardWindow => windows().FirstOrDefault(w => w.IsOut && w.KeyboardMode);

    public RevealController(Func<IReadOnlyList<LineWindow>> windows, Func<bool> edgeEnabled)
    {
        this.windows = windows;
        this.edgeEnabled = edgeEnabled;
        timer.Tick += (_, _) => Tick();
    }

    public void Start() => timer.Start();
    public void Stop() => timer.Stop();

    public void Toggle()
    {
        var open = windows().FirstOrDefault(w => w.IsOut);
        if (open is { KeyboardMode: true }) { RetractAll(); return; }
        var fg = Native.GetForegroundWindow();
        PasteTarget = Native.IsOwn(fg) ? IntPtr.Zero : fg;
        if (open == null)
        {
            Native.GetCursorPos(out var p);
            open = windows().FirstOrDefault(w => w.Screen.Bounds.Contains(p.X, p.Y)) ?? windows().FirstOrDefault();
        }
        open?.Reveal(keyboard: true);
    }

    public void RetractAll()
    {
        armed = false; // don't pop straight back out if the pointer is still on the edge
        outsideSince = null;
        // Leaving keyboard mode: hand focus back to where it came from.
        bool restore = windows().Any(w => w.IsOut && w.KeyboardMode) && Native.IsOwn(Native.GetForegroundWindow());
        if (restore && PasteTarget != IntPtr.Zero && Native.IsWindow(PasteTarget)) Native.ForceForeground(PasteTarget);
        foreach (var w in windows()) w.Retract();
    }

    void Tick()
    {
        if (!Native.GetCursorPos(out var p)) return;
        bool button = Native.KeyDown(0x01) || Native.KeyDown(0x02) || Native.KeyDown(0x04);
        bool click = button && !lastButton;
        lastButton = button;
        var now = DateTime.UtcNow;

        var open = windows().FirstOrDefault(w => w.IsOut);
        if (open != null)
        {
            if (HoldOpen > 0) { outsideSince = null; return; }
            if (open.KeyboardMode) return; // closes on Esc, a pick, or losing focus
            if (FullScreen.Covers(open.Screen))
            {
                Diag.Throttled("reveal:retract:fullscreen", "reveal", "open line retracted by fullscreen");
                RetractAll(); return;
            }
            bool inside = open.HoverZoneContains(p);
            if (click && !inside) { RetractAll(); return; }
            if (inside) { outsideSince = null; return; }
            outsideSince ??= now;
            if (now - outsideSince >= Linger) RetractAll();
            return;
        }

        var here = windows().FirstOrDefault(w => w.Screen.Bounds.Contains(p.X, p.Y));
        bool atEdge = here != null
            && p.Y <= here.Screen.Bounds.Top + Math.Max(2, EdgeDepth * here.Scale)
            && p.X < here.Screen.Bounds.Right - CloseButtonCorner * here.Scale;
        // The explicit null test also tells the compiler that `here` is non-null below.
        if (!atEdge || here is null)
        {
            armed = true; edgeSince = null;
            if (lastAtEdge) { Diag.Debug("reveal", "left top-edge strip"); lastAtEdge = false; }
            return;
        }
        // Only state changes, never the 30 ms tick itself. That would be ~2.8M lines a day.
        if (!lastAtEdge)
        {
            Diag.Debug("reveal", "entered top-edge strip",
                ("screen", here.Screen.DeviceName), ("x", p.X), ("y", p.Y));
            lastAtEdge = true;
        }
        bool allowed = edgeEnabled() && armed && !button && !FullScreen.Covers(here.Screen);
        if (!allowed)
        {
            string reason = !edgeEnabled() ? "setting_off"
                : !armed ? "armed_false" : button ? "mouse_button" : "fullscreen";
            Diag.Throttled(DiagLevel.Info, "reveal:veto:" + reason, 60, "reveal",
                "edge reveal vetoed", ("reason", reason), ("x", p.X), ("y", p.Y),
                ("bounds", here.Screen.Bounds.ToString()), ("scale", here.Scale));
            edgeSince = null; return;
        }
        if (edgeWindow != here || edgeSince == null) { edgeWindow = here; edgeSince = now; return; }
        if (now - edgeSince >= Dwell)
        {
            edgeSince = null;
            PasteTarget = IntPtr.Zero;
            Diag.Info("reveal", "dwell reached, showing line",
                ("screen", here.Screen.DeviceName), ("dwell_ms", Dwell.TotalMilliseconds),
                ("scale", here.Scale));
            here.Reveal(keyboard: false);
        }
    }
}

// The line stays away while something is full screen (games, videos, presentations).
static class FullScreen
{
    static int lastQuns = -1;

    public static bool Covers(Forms.Screen screen)
    {
        // SHQueryUserNotificationState is system-wide, not per monitor. Only QUNS_RUNNING_D3D_FULL_SCREEN
        // (3) is a reliable "get out of the way" signal — a genuine exclusive-fullscreen game or video.
        // The older QUNS_BUSY (2) and QUNS_PRESENTATION_MODE (4) are reported spuriously by some GPU
        // drivers, presentation utilities and laptops that were once docked to a projector, which would
        // suppress the top-edge reveal everywhere for no reason (the classic "it stopped working on my
        // ThinkPad" symptom). A real full-screen window — video player, PowerPoint slide show — is still
        // caught by the foreground-rect check below.
        int hr = Native.SHQueryUserNotificationState(out var state);
        if (hr == 0 && state == 3)
        {
            Diag.Throttled(DiagLevel.Info, "reveal:quns:3", 60, "reveal",
                "fullscreen suppression by QUNS", ("quns", state),
                ("hint", "QUNS_RUNNING_D3D_FULL_SCREEN"));
            lastQuns = state;
            return true;
        }
        if (hr == 0 && state != lastQuns)
        {
            Diag.Debug("reveal", "QUNS state changed", ("from", lastQuns), ("to", state), ("hr", hr));
            lastQuns = state;
        }
        // Record the exact states that used to be misread as fullscreen. This line is what makes
        // "top edge stopped working on my laptop" a one-glance diagnosis.
        if (hr == 0 && state is 2 or 4)
            Diag.Throttled(DiagLevel.Info, "reveal:quns:ignored:" + state, 60, "reveal",
                "QUNS busy/presentation ignored (not a suppression reason)", ("quns", state),
                ("hint", "0.9.2 and earlier suppressed these spuriously on docked laptops"));

        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Native.GetShellWindow() || fg == Native.GetDesktopWindow()) return false;
        if (Native.IsOwn(fg)) return false;
        var cls = new StringBuilder(64);
        Native.GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;
        if (Native.IsZoomed(fg)) return false; // maximized isn't full screen
        if (!Native.GetWindowRect(fg, out var r)) return false;
        var b = screen.Bounds;
        bool covers = r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        if (covers)
            Diag.Throttled(DiagLevel.Info, "reveal:fgrext", 60, "reveal",
                "fullscreen suppression by foreground rect",
                ("class", cls.ToString()), ("rect", $"{r.Left},{r.Top},{r.Right},{r.Bottom}"),
                ("bounds", b.ToString()));
        return covers;
    }
}
