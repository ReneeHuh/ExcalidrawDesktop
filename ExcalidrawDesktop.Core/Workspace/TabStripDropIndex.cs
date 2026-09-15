namespace ExcalidrawDesktop.Core;

/// <summary>One tab's horizontal extent in the tab strip's coordinate space.</summary>
public readonly record struct TabStripSlot(double Start, double Width)
{
    public double Midpoint => Start + Width / 2;
}

/// <summary>Where a dragged tab lands when it is dropped onto a tab strip.</summary>
public static class TabStripDropIndex
{
    /// <summary>
    /// Returns the insertion index for a drop at <paramref name="pointerX"/>.
    /// <paramref name="slots"/> are in logical tab order. The tab inserts before
    /// the first slot whose midpoint lies past the pointer in the strip's own
    /// direction, or appends when the pointer is past every midpoint. Direction
    /// is inferred from the slots, so a right-to-left strip behaves the same
    /// whether the platform reports mirrored or logical coordinates.
    /// </summary>
    public static int FromPointer(double pointerX, IReadOnlyList<TabStripSlot> slots)
    {
        if (slots.Count == 0)
        {
            return 0;
        }

        var increasing = slots.Count < 2 || slots[^1].Start >= slots[0].Start;
        for (var index = 0; index < slots.Count; index++)
        {
            var midpoint = slots[index].Midpoint;
            if (increasing ? pointerX < midpoint : pointerX > midpoint)
            {
                return index;
            }
        }

        return slots.Count;
    }
}
