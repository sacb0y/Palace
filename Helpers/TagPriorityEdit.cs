namespace Palace.Helpers;

/// <summary>
/// Priority commits from <c>NumberBox.ValueChanged</c>. The box reports
/// <c>NewValue</c> before TwoWay writes <c>SelectedPriority</c>, so a save that
/// reads the property stores the previous priority and the box snaps back.
/// Loading a different tag also raises <c>ValueChanged</c> and must not save.
/// </summary>
public static class TagPriorityEdit
{
    public readonly record struct Commit(bool Save, int Value);

    public static Commit FromBox(double newValue, bool loadingSelection)
    {
        if (loadingSelection || double.IsNaN(newValue) || double.IsInfinity(newValue))
        {
            return new Commit(false, 0);
        }

        var rounded = (int)Math.Round(newValue, MidpointRounding.AwayFromZero);
        if (rounded < 0)
        {
            rounded = 0;
        }

        return new Commit(true, rounded);
    }
}
