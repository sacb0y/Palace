using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public class ThumbFileReplaceTests
{
    [Fact]
    public void TryMoveOverwrite_ReplacesDestination()
    {
        var dir = NewDir();
        try
        {
            var dest = Path.Combine(dir, "thumb.jpg");
            var source = Path.Combine(dir, "thumb.jpg.part");
            File.WriteAllText(dest, "old");
            File.WriteAllText(source, "new");

            var result = ThumbFileReplace.TryMoveOverwrite(source, dest);

            Assert.Equal(ThumbReplaceResult.Replaced, result);
            Assert.Equal("new", File.ReadAllText(dest));
            Assert.False(File.Exists(source));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryMoveOverwrite_KeepsExistingWhenTheDestinationIsLocked()
    {
        var dir = NewDir();
        try
        {
            var dest = Path.Combine(dir, "thumb.jpg");
            var source = Path.Combine(dir, "thumb.jpg.part");
            File.WriteAllText(dest, "old");
            File.WriteAllText(source, "new");

            var result = ThumbFileReplace.TryMoveOverwrite(
                source,
                dest,
                (_, _) => throw new UnauthorizedAccessException("file in use"));

            Assert.Equal(ThumbReplaceResult.KeptExisting, result);
            Assert.Equal("old", File.ReadAllText(dest));
            Assert.False(File.Exists(source));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryMoveOverwrite_DoesNotThrowWhenAnotherReaderHoldsTheDestination()
    {
        var dir = NewDir();
        try
        {
            var dest = Path.Combine(dir, "thumb.jpg");
            var source = Path.Combine(dir, "thumb.jpg.part");
            File.WriteAllText(dest, "old");
            File.WriteAllText(source, "new");
            using var hold = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.Read);

            var result = ThumbFileReplace.TryMoveOverwrite(source, dest);

            Assert.True(result is ThumbReplaceResult.Replaced or ThumbReplaceResult.KeptExisting);
            Assert.True(File.Exists(dest));
            Assert.False(string.IsNullOrEmpty(File.ReadAllText(dest)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BoxScale_StopsWhenCancelled()
    {
        var src = new byte[32 * 32 * 4];
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => StillDecode.BoxScaleBgra(src, 32, 32, 16, 16, cts.Token));
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-thumb-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
