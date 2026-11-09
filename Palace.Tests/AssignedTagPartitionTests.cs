using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class AssignedTagPartitionTests
{
    [Fact]
    public void IsImpliedOnly_TrueOnlyWhenEverySourceIsImplied()
    {
        Assert.True(AssignedTagPartition.IsImpliedOnly([TagSource.Implied]));
        Assert.True(AssignedTagPartition.IsImpliedOnly([TagSource.Implied, TagSource.Implied]));
        Assert.False(AssignedTagPartition.IsImpliedOnly([TagSource.Manual]));
        Assert.False(AssignedTagPartition.IsImpliedOnly([TagSource.Manual, TagSource.Implied]));
        Assert.False(AssignedTagPartition.IsImpliedOnly([TagSource.Prompt, TagSource.Implied]));
        Assert.False(AssignedTagPartition.IsImpliedOnly([]));
        Assert.False(AssignedTagPartition.IsImpliedOnly(null));
    }

    [Fact]
    public void EffectiveSource_MixedManualImplied_PrefersManual()
    {
        // Regression: multi-select Manual+Implied used Sample.Source and could
        // land the chip in the Implied subsection when Sample was Implied first.
        var sources = new HashSet<TagSource> { TagSource.Manual, TagSource.Implied };
        Assert.Equal(
            TagSource.Manual,
            AssignedTagPartition.EffectiveSource(sources, sample: TagSource.Implied));
        Assert.False(AssignedTagPartition.IsImpliedOnly(sources));
    }

    [Fact]
    public void EffectiveSource_SingleKeepsThatSource()
    {
        Assert.Equal(
            TagSource.Implied,
            AssignedTagPartition.EffectiveSource([TagSource.Implied], TagSource.Manual));
        Assert.Equal(
            TagSource.Prompt,
            AssignedTagPartition.EffectiveSource([TagSource.Prompt], TagSource.Manual));
    }

    [Fact]
    public void EffectiveSource_MixedPromptImplied_PrefersPrompt()
    {
        var sources = new HashSet<TagSource> { TagSource.Implied, TagSource.Prompt };
        Assert.Equal(
            TagSource.Prompt,
            AssignedTagPartition.EffectiveSource(sources, TagSource.Implied));
    }
}
