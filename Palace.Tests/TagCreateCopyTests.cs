using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagCreateCopyTests
{
    [Fact]
    public void Describe_ExistedOnly_IsFriendlyAndNamesTag()
    {
        var result = new TagCreateBatchResult
        {
            Created = 0,
            Existed = 1,
            Tags = [new Tag { Id = "1", Name = "Sky" }]
        };
        Assert.Equal("“Sky” already exists — selected it.", TagCreateCopy.Describe(result));
    }

    [Fact]
    public void Describe_CreatedMixed_KeepsCounts()
    {
        var result = new TagCreateBatchResult { Created = 2, Existed = 1, Tags = [] };
        Assert.Equal("Created 2, already existed 1.", TagCreateCopy.Describe(result));
    }
}
