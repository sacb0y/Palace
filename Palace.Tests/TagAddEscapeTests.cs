using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagAddEscapeTests
{
    [Fact]
    public void Resolve_PendingOrTyped_Clears()
    {
        Assert.Equal(TagAddEscapeAction.ClearInput, TagAddEscape.Resolve(hasPendingChips: true, hasTypedText: false));
        Assert.Equal(TagAddEscapeAction.ClearInput, TagAddEscape.Resolve(hasPendingChips: false, hasTypedText: true));
        Assert.Equal(TagAddEscapeAction.ClearInput, TagAddEscape.Resolve(hasPendingChips: true, hasTypedText: true));
    }

    [Fact]
    public void Resolve_Empty_Unfocuses() =>
        Assert.Equal(TagAddEscapeAction.Unfocus, TagAddEscape.Resolve(hasPendingChips: false, hasTypedText: false));
}
