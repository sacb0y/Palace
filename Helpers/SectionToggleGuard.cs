namespace Palace.Helpers;

/// <summary>
/// Section-collapse republish guard depth + generation. Off WinUI for Palace.Tests.
/// Generation never resets; Loaded stamps a mark so stale Ended after leave/return
/// is ignored without skipping decrements for overlapping in-flight toggles.
/// </summary>
public sealed class SectionToggleGuard
{
    public int Generation { get; private set; }
    public int Depth { get; private set; }
    /// <summary>Ended with <c>generation &lt;= ResetMark</c> is stale (pre-Loaded).</summary>
    public int ResetMark { get; private set; }

    /// <summary>Arm one republish; returns the generation for the matching Ended.</summary>
    public int Start()
    {
        Generation++;
        Depth++;
        return Generation;
    }

    /// <summary>
    /// Leave/return (NavigationCache): clear depth and mark the current generation
    /// so in-flight Ended callbacks from before Loaded cannot touch a new toggle.
    /// Does not reset <see cref="Generation"/>.
    /// </summary>
    public void OnLoaded()
    {
        Depth = 0;
        ResetMark = Generation;
    }

    /// <summary>
    /// Finish one republish. Stale (<paramref name="generation"/> ≤ mark) is ignored.
    /// Otherwise always decrements depth when &gt; 0 (overlapping toggles must not stick).
    /// </summary>
    /// <param name="shouldRestore">
    /// True when this end brought depth to 0 (Focus + highlight restore).
    /// </param>
    /// <returns>False when the callback was ignored as stale.</returns>
    public bool TryEnd(int generation, out bool shouldRestore)
    {
        shouldRestore = false;
        if (generation <= ResetMark)
        {
            return false;
        }

        if (Depth <= 0)
        {
            return true;
        }

        shouldRestore = Depth == 1;
        Depth--;
        return true;
    }
}
