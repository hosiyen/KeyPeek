namespace KeyPeek.Core;

/// <summary>
/// Keyboard selection over the panel's visible rows, kept pure so the navigation rules are
/// testable without a window.
///
/// The panel is a set of section cards laid out in columns, but for keyboard purposes it is
/// one flat list of entries plus the index where each card starts. Down/Up step one entry,
/// Left/Right jump to the neighbouring card, Home/End go to the ends, PageUp/PageDown move
/// a screenful. Stepping clamps (no wrap-around, which feels wrong in a two-column layout);
/// card jumps wrap, because there is no "past the last card" to clamp to.
/// </summary>
public sealed class ExploreSelection
{
    /// <summary>Rows moved by PageUp/PageDown.</summary>
    public const int PageSize = 10;

    private readonly int[] _cardStarts;

    public int Count { get; }
    public int Index { get; private set; }

    /// <param name="count">Number of selectable entries in the flat list.</param>
    /// <param name="cardStarts">Ascending flat indexes where each section card begins.</param>
    public ExploreSelection(int count, IEnumerable<int>? cardStarts = null)
    {
        Count = Math.Max(0, count);
        _cardStarts = (cardStarts ?? Enumerable.Empty<int>())
            .Where(i => i >= 0 && i < Count)
            .Distinct()
            .OrderBy(i => i)
            .ToArray();
        Index = Count == 0 ? -1 : 0;
    }

    public bool HasSelection => Index >= 0 && Index < Count;

    /// <summary>Applies one navigation key. Returns false when the key isn't a movement key
    /// (Enter) or there is nothing to move through.</summary>
    public bool Apply(int vk)
    {
        if (Count == 0)
            return false;

        switch (vk)
        {
            case ExplorePolicy.VkDown: return Step(1);
            case ExplorePolicy.VkUp: return Step(-1);
            case ExplorePolicy.VkPageDown: return Step(PageSize);
            case ExplorePolicy.VkPageUp: return Step(-PageSize);
            case ExplorePolicy.VkHome: return MoveTo(0);
            case ExplorePolicy.VkEnd: return MoveTo(Count - 1);
            case ExplorePolicy.VkRight: return JumpCard(1);
            case ExplorePolicy.VkLeft: return JumpCard(-1);
            default: return false; // Enter and anything else: not a movement
        }
    }

    private bool Step(int delta) => MoveTo(Math.Clamp(Index + delta, 0, Count - 1));

    /// <summary>Moves to the first entry of the previous/next card, wrapping at both ends.
    /// From mid-card, Left goes to the top of the current card first (like Home in a column).</summary>
    private bool JumpCard(int direction)
    {
        if (_cardStarts.Length == 0)
            return MoveTo(direction > 0 ? Count - 1 : 0);

        int current = CurrentCard();
        if (direction < 0 && Index > _cardStarts[current])
            return MoveTo(_cardStarts[current]); // snap to the top of this card first

        int next = (current + direction + _cardStarts.Length) % _cardStarts.Length;
        return MoveTo(_cardStarts[next]);
    }

    private int CurrentCard()
    {
        int card = 0;
        for (int i = 0; i < _cardStarts.Length; i++)
        {
            if (_cardStarts[i] <= Index)
                card = i;
            else
                break;
        }
        return card;
    }

    private bool MoveTo(int index)
    {
        int clamped = Math.Clamp(index, 0, Count - 1);
        if (clamped == Index)
            return false;
        Index = clamped;
        return true;
    }
}
