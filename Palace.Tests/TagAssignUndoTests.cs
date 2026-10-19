using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagAssignUndoTests
{
    [Fact]
    public void ImpliedAdded_lists_only_tags_the_assignment_introduced()
    {
        var added = TagAssignUndo.ImpliedAdded(
            presentBefore: ["already"],
            presentAfter: ["scarf", "winter", "already"],
            assignedTagId: "scarf");
        Assert.Equal(["winter"], added);
    }

    [Fact]
    public void Encode_round_trips_implied_ids()
    {
        var encoded = TagAssignUndo.Encode(["winter", "wool"]);
        Assert.Equal(["winter", "wool"], TagAssignUndo.Decode(encoded));
        Assert.True(TagAssignUndo.TouchesAssignedTag(encoded));
        Assert.Empty(TagAssignUndo.Decode(null));
    }

    [Fact]
    public void PlanAssign_journals_only_items_whose_tags_changed()
    {
        // One of four already has PrGamma. Filling the rest must not journal that item.
        var plans = new[]
        {
            TagAssignUndo.PlanAssign(["prgamma"], ["prgamma"], "prgamma"),
            TagAssignUndo.PlanAssign([], ["prgamma"], "prgamma"),
            TagAssignUndo.PlanAssign([], ["prgamma"], "prgamma"),
            TagAssignUndo.PlanAssign([], ["prgamma", "winter"], "prgamma")
        };

        Assert.Equal(3, plans.Count(plan => plan.Journal));
        Assert.False(plans[0].Journal);
        Assert.True(plans[1].TouchAssignedTag);
        Assert.Equal(["winter"], plans[3].ImpliedIds);
    }

    [Fact]
    public void PlanAssign_does_not_undo_a_tag_the_asset_already_had()
    {
        var change = TagAssignUndo.PlanAssign(["prgamma"], ["prgamma", "winter"], "prgamma");
        Assert.True(change.Journal);
        Assert.False(change.TouchAssignedTag);
        var encoded = TagAssignUndo.Encode(change.ImpliedIds, change.TouchAssignedTag);
        Assert.False(TagAssignUndo.TouchesAssignedTag(encoded));
        Assert.Equal(["winter"], TagAssignUndo.Decode(encoded));
    }

    [Fact]
    public void PlanRemove_skips_items_that_lack_the_tag_and_keeps_swept_implied()
    {
        var missing = TagAssignUndo.PlanRemove([], [], "prgamma");
        Assert.False(missing.Journal);

        var swept = TagAssignUndo.PlanRemove(
            [("prgamma", TagSource.Manual), ("winter", TagSource.Implied), ("kept", TagSource.Manual)],
            [("kept", TagSource.Manual)],
            "prgamma");
        Assert.True(swept.Journal);
        Assert.True(swept.TouchAssignedTag);
        Assert.Equal(["winter"], swept.ImpliedIds);
    }
}