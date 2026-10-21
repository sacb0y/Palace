using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ReviewHotkeysTests
{
    [Fact]
    public void FromKey_X_K_S()
    {
        Assert.Equal(TouchGestures.SwipeAction.Reject, ReviewHotkeys.FromKey(ReviewHotkeys.KeyLetterX));
        Assert.Equal(TouchGestures.SwipeAction.Keep, ReviewHotkeys.FromKey(ReviewHotkeys.KeyLetterK));
        Assert.Equal(TouchGestures.SwipeAction.Skip, ReviewHotkeys.FromKey(ReviewHotkeys.KeyLetterS));
        Assert.Equal(TouchGestures.SwipeAction.None, ReviewHotkeys.FromKey(ReviewHotkeys.KeyLetterX, controlDown: true));
        Assert.Equal(TouchGestures.SwipeAction.None, ReviewHotkeys.FromKey(65));
        Assert.True(ReviewHotkeys.IsDecisionKey(ReviewHotkeys.KeyLetterS));
        Assert.False(ReviewHotkeys.IsDecisionKey(ReviewHotkeys.KeyLetterS, controlDown: true));
        Assert.True(ReviewHotkeys.IsUndoChord(controlDown: true, ReviewHotkeys.KeyLetterZ));
        Assert.False(ReviewHotkeys.IsUndoChord(controlDown: false, ReviewHotkeys.KeyLetterZ));
    }
}
