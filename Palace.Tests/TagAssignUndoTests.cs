using Palace.Helpers;
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
        Assert.Empty(TagAssignUndo.Decode(null));
    }
}