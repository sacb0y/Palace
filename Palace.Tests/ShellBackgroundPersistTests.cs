using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ShellBackgroundPersistTests
{
    [Fact]
    public void ShouldFlush_AfterDebounceWindow()
    {
        Assert.Equal(300, ShellBackgroundPersist.DebounceMs);
        Assert.Equal(300, ShellBackground.PersistDebounceMs);
        Assert.False(ShellBackgroundPersist.ShouldFlush(0));
        Assert.False(ShellBackgroundPersist.ShouldFlush(299));
        Assert.True(ShellBackgroundPersist.ShouldFlush(300));
        Assert.True(ShellBackgroundPersist.ShouldFlush(900));
        Assert.Equal(ShellBackgroundPersist.DebounceMs, ShellBackgroundPersist.RestartWaitMs());
    }
}
