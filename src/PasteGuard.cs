using System;

namespace Shengyi;

// 0.11.0 · pure decisions for paste delivery. Kept dependency-free so _verify can assert the
// UIPI/focus fallback and the sequence cancellation rules without WPF or real keystrokes.
static class PasteGuard
{
    // Ctrl+V was sent as four events (Ctrl down, V down, V up, Ctrl up). A partial count means
    // injection was refused mid-way; zero means the target is a higher-integrity window (UIPI).
    public const int CtrlVEvents = 4;

    public static bool NeedCopyOnlyFallback(uint injected, bool foregroundMatched, bool ownStill)
        => injected < CtrlVEvents || !foregroundMatched || ownStill;

    // A paste that was queued before Esc / a new sequence must never fire afterwards. The token
    // is bumped on both StopSequence and StartSequence; an in-flight delivery checks it after
    // its 90 ms settle delay.
    public static bool TokenStillValid(int captured, int current) => captured == current;
}
