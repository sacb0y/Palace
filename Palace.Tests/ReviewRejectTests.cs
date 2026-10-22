using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ReviewRejectTests
{
    [Fact]
    public void DestinationDir_IsSiblingRejected()
    {
        var path = Path.Combine("Library", "shots", "a.jpg");
        Assert.Equal(
            Path.Combine("Library", "shots", ReviewReject.FolderName),
            ReviewReject.DestinationDir(path));
        Assert.Equal(
            Path.Combine("Library", "shots"),
            ReviewReject.ParentDir(path));
        Assert.Equal(TouchGestures.RejectedFolderName, ReviewReject.FolderName);
    }

    [Fact]
    public void IsAlreadyRejected_DetectsNested()
    {
        var path = Path.Combine("Library", "shots", "_rejected", "a.jpg");
        Assert.True(ReviewReject.IsAlreadyRejected(path));
        Assert.False(ReviewReject.IsAlreadyRejected(Path.Combine("Library", "shots", "a.jpg")));
        Assert.False(ReviewReject.IsAlreadyRejected(null));
        Assert.Null(ReviewReject.DestinationDir(null));
    }
}
