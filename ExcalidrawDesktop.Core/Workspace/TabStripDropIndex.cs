namespace ExcalidrawDesktop.Core;

/// <summary>One tab's horizontal extent in the tab strip's coordinate space.</summary>
public readonly record struct TabStripSlot(double Start, double Width)
{
    public double Midpoint => Start + Width / 2;
    public bool IsRealized => Width > 0 && double.IsFinite(Start) && double.IsFinite(Width);
}

/// <summary>Where a dragged tab lands when it is dropped onto a tab strip.</summary>
public static class TabStripDropIndex
{
    /// <summary>
    /// Returns the insertion index for a drop at <paramref name="pointerX"/>.
    /// <paramref name="slots"/> are in logical tab order. The tab inserts before
    /// the first realized slot whose midpoint lies past the pointer in the
    /// strip's own direction, or inserts after the last realized slot. Direction
    /// is inferred from the slots, so a right-to-left strip behaves the same
    /// whether the platform reports mirrored or logical coordinates. Keep an
    /// empty slot for each unrealized tab so returned indices remain collection
    /// indices; its placeholder geometry must not participate in the search.
    /// </summary>
    public static int FromPointer(double pointerX, IReadOnlyList<TabStripSlot> slots)
    {
        var first = -1;
        var last = -1;
        for (var index = 0; index < slots.Count; index++)
        {
            if (!slots[index].IsRealized) continue;
            if (first < 0) first = index;
            last = index;
        }

        if (first < 0)
        {
            return 0;
        }

        var increasing = slots[last].Start >= slots[first].Start;
        for (var index = first; index <= last; index++)
        {
            if (!slots[index].IsRealized) continue;
            var midpoint = slots[index].Midpoint;
            if (increasing ? pointerX < midpoint : pointerX > midpoint)
            {
                return index;
            }
        }

        return last + 1;
    }
}
