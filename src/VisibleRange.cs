using System;

namespace Shengyi;

// 0.11.0 · the hanging line keeps an xs[] array of strictly increasing card positions. Per-frame
// work (realize, rope gradient) only needs cards intersecting the viewport, but used to scan all
// items — O(n) every frame with up to MaxItems cards. These pure helpers turn that into a binary
// search + a walk over the visible slice. No WPF dependency so _verify can assert the boundaries.
static class VisibleRange
{
    // First index whose position + its own width reaches 'from'. Positions are ascending and
    // width>0, so a card ending before 'from' has every earlier card ending before it too.
    // 'maxWidth' is the largest card width at the current scale; passing it makes the lower bound
    // conservative (never skips a card that might still overlap) without scanning widths.
    public static int FirstAtOrAfter(double[] xs, int count, double from, double maxWidth)
    {
        if (count <= 0 || xs.Length == 0) return 0;
        double edge = from - maxWidth;
        int lo = 0, hi = count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (xs[mid] < edge) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    // First index whose position is strictly greater than 'to'. Cards at or before 'to' are
    // still potentially visible; the first past it (and everything after) is off-screen.
    public static int LastAtOrBefore(double[] xs, int count, double to)
    {
        if (count <= 0 || xs.Length == 0) return 0;
        int lo = 0, hi = count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (xs[mid] <= to) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}
