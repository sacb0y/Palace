using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicSortOrderTests
{
    [Fact]
    public void Parse_Unknown_UsesDateNewest()
    {
        Assert.Equal(MosaicSort.DateNewest, MosaicSortOrder.Parse(null));
        Assert.Equal(MosaicSort.DateNewest, MosaicSortOrder.Parse(""));
        Assert.Equal(MosaicSort.DateNewest, MosaicSortOrder.Parse("nope"));
        Assert.Equal(MosaicSort.DateNewest, MosaicSortOrder.Parse(99));
        Assert.Equal(MosaicSort.DateNewest, MosaicSortOrder.Default);
    }

    [Theory]
    [InlineData("DateNewest", MosaicSort.DateNewest)]
    [InlineData("name", MosaicSort.Name)]
    [InlineData("NameZ", MosaicSort.NameZ)]
    [InlineData("SIZE", MosaicSort.Size)]
    [InlineData("Rating", MosaicSort.Rating)]
    [InlineData("Type", MosaicSort.Type)]
    [InlineData("DateOldest", MosaicSort.DateOldest)]
    [InlineData("DateAdded", MosaicSort.DateAdded)]
    public void Parse_NamedValues(string stored, MosaicSort expected) =>
        Assert.Equal(expected, MosaicSortOrder.Parse(stored));

    [Fact]
    public void Parse_EnumAndInt()
    {
        Assert.Equal(MosaicSort.Name, MosaicSortOrder.Parse(MosaicSort.Name));
        Assert.Equal(MosaicSort.Size, MosaicSortOrder.Parse((int)MosaicSort.Size));
    }

    [Fact]
    public void Persist_RoundTrips()
    {
        foreach (var choice in MosaicSortOrder.Choices)
        {
            Assert.Equal(choice.Key, MosaicSortOrder.Parse(MosaicSortOrder.Persist(choice.Key)));
        }
    }

    [Fact]
    public void Index_RoundTripsEveryChoice()
    {
        Assert.Equal(MosaicSortOrder.Choices.Count, MosaicSortOrder.Labels.Count);
        for (var i = 0; i < MosaicSortOrder.Choices.Count; i++)
        {
            var key = MosaicSortOrder.FromIndex(i);
            Assert.Equal(MosaicSortOrder.Choices[i].Key, key);
            Assert.Equal(i, MosaicSortOrder.IndexOf(key));
            Assert.Equal(MosaicSortOrder.Choices[i].Label, MosaicSortOrder.Label(key));
        }

        Assert.Equal(MosaicSort.DateNewest, MosaicSortOrder.FromIndex(-1));
        Assert.Equal(MosaicSort.DateNewest, MosaicSortOrder.FromIndex(80));
        Assert.Equal(0, MosaicSortOrder.IndexOf((MosaicSort)99));
    }

    [Fact]
    public void SettingsKey_IsPerProject()
    {
        Assert.Equal("MosaicSort_abc", MosaicSortOrder.SettingsKey("abc"));
        Assert.NotEqual(MosaicSortOrder.SettingsKey("a"), MosaicSortOrder.SettingsKey("b"));
    }

    [Fact]
    public void Sql_IsAllowlisted_NoUserText()
    {
        foreach (var choice in MosaicSortOrder.Choices)
        {
            var sql = MosaicSortOrder.Sql(choice.Key);
            Assert.False(string.IsNullOrWhiteSpace(sql));
            Assert.DoesNotContain(';', sql);
            Assert.DoesNotContain("--", sql, StringComparison.Ordinal);
            Assert.StartsWith("ORDER BY ", MosaicSortOrder.OrderBySql(choice.Key), StringComparison.Ordinal);
            Assert.Equal("ORDER BY " + sql, MosaicSortOrder.OrderBySql(choice.Key));
        }

        Assert.Contains("DateModified", MosaicSortOrder.Sql(MosaicSort.DateNewest), StringComparison.Ordinal);
        Assert.Contains("DateAdded", MosaicSortOrder.Sql(MosaicSort.DateAdded), StringComparison.Ordinal);
        Assert.Contains("FileName", MosaicSortOrder.Sql(MosaicSort.Name), StringComparison.Ordinal);
        Assert.Contains("FileSize", MosaicSortOrder.Sql(MosaicSort.Size), StringComparison.Ordinal);
        Assert.Contains("Rating", MosaicSortOrder.Sql(MosaicSort.Rating), StringComparison.Ordinal);
        Assert.Contains("Kind", MosaicSortOrder.Sql(MosaicSort.Type), StringComparison.Ordinal);
        Assert.Contains("DESC", MosaicSortOrder.Sql(MosaicSort.NameZ), StringComparison.Ordinal);
        Assert.Contains("ASC", MosaicSortOrder.Sql(MosaicSort.DateOldest), StringComparison.Ordinal);
        Assert.Equal(MosaicSortOrder.Sql(MosaicSort.DateNewest), MosaicSortOrder.Sql((MosaicSort)99));
    }

    [Fact]
    public void CatalogDefault_StaysName() =>
        Assert.Equal(MosaicSort.Name, MosaicSortOrder.CatalogDefault);
}
