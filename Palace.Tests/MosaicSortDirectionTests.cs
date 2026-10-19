using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

/// <summary>Sort field + direction, per-view memory, and every ORDER BY against a real catalog.</summary>
public sealed class MosaicSortDirectionTests
{
    [Fact]
    public void Every_sort_maps_to_a_field_and_direction_and_back()
    {
        foreach (var sort in Enum.GetValues<MosaicSort>())
        {
            var field = MosaicSortOrder.FieldOf(sort);
            Assert.Equal(sort, MosaicSortOrder.Compose(field, MosaicSortOrder.IsDescending(sort)));
            var flipped = MosaicSortOrder.ToggleDirection(sort);
            Assert.NotEqual(sort, flipped);
            Assert.Equal(field, MosaicSortOrder.FieldOf(flipped));
            Assert.Equal(sort, MosaicSortOrder.ToggleDirection(flipped));
        }
    }

    [Fact]
    public void Every_field_and_direction_is_a_distinct_value()
    {
        var all = MosaicSortOrder.FieldChoices
            .SelectMany(c => new[] { MosaicSortOrder.Compose(c.Field, true), MosaicSortOrder.Compose(c.Field, false) })
            .ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(Enum.GetValues<MosaicSort>().Length, all.Count);
    }

    [Theory]
    [InlineData(MosaicSort.DateNewest, MosaicSortField.DateModified, true, "Newest first")]
    [InlineData(MosaicSort.DateOldest, MosaicSortField.DateModified, false, "Oldest first")]
    [InlineData(MosaicSort.Name, MosaicSortField.Name, false, "A to Z")]
    [InlineData(MosaicSort.NameZ, MosaicSortField.Name, true, "Z to A")]
    [InlineData(MosaicSort.Size, MosaicSortField.Size, true, "Largest first")]
    [InlineData(MosaicSort.RatingLowest, MosaicSortField.Rating, false, "Lowest first")]
    [InlineData(MosaicSort.DateTakenNewest, MosaicSortField.DateTaken, true, "Newest first")]
    public void Legacy_and_new_values_keep_their_meaning(MosaicSort sort, MosaicSortField field, bool descending, string label)
    {
        Assert.Equal(field, MosaicSortOrder.FieldOf(sort));
        Assert.Equal(descending, MosaicSortOrder.IsDescending(sort));
        Assert.Equal(label, MosaicSortOrder.DirectionLabel(sort));
    }

    [Fact]
    public void Field_combo_picks_the_natural_direction_and_ignores_unset()
    {
        var nameIndex = MosaicSortOrder.FieldChoices.ToList().FindIndex(c => c.Field == MosaicSortField.Name);
        var sizeIndex = MosaicSortOrder.FieldChoices.ToList().FindIndex(c => c.Field == MosaicSortField.Size);
        Assert.True(MosaicSortOrder.ShouldApplyFieldIndex(nameIndex, MosaicSort.DateNewest, out var next));
        Assert.Equal(MosaicSort.Name, next);
        Assert.True(MosaicSortOrder.ShouldApplyFieldIndex(sizeIndex, MosaicSort.Name, out next));
        Assert.Equal(MosaicSort.Size, next);
        Assert.False(MosaicSortOrder.ShouldApplyFieldIndex(nameIndex, MosaicSort.NameZ, out next));
        Assert.Equal(MosaicSort.NameZ, next);
        Assert.False(MosaicSortOrder.ShouldApplyFieldIndex(-1, MosaicSort.Size, out next));
        Assert.Equal(MosaicSort.Size, next);
        Assert.False(MosaicSortOrder.ShouldApplyFieldIndex(99, MosaicSort.Size, out _));
        Assert.Equal(nameIndex, MosaicSortOrder.FieldIndexOf(MosaicSort.NameZ));
        Assert.Equal(MosaicSortOrder.FieldChoices.Count, MosaicSortOrder.FieldLabels.Count);
    }

    [Fact]
    public void Each_view_remembers_its_own_sort_and_falls_back_to_the_shared_key()
    {
        var store = new Dictionary<string, object?> { [MosaicSortOrder.SettingsKey("p")] = "Size" };
        object? Read(string key) => store.TryGetValue(key, out var v) ? v : null;
        Assert.Equal(MosaicSort.Size, MosaicSortOrder.Load(Read, "p", MosaicSortView.Folders));
        Assert.Equal(MosaicSort.Size, MosaicSortOrder.Load(Read, "p", MosaicSortView.Tags));
        store[MosaicSortOrder.SettingsKey("p", MosaicSortView.Tags)] = MosaicSortOrder.Persist(MosaicSort.DateTakenOldest);
        Assert.Equal(MosaicSort.DateTakenOldest, MosaicSortOrder.Load(Read, "p", MosaicSortView.Tags));
        Assert.Equal(MosaicSort.Size, MosaicSortOrder.Load(Read, "p", MosaicSortView.Folders));
        Assert.Equal(MosaicSortOrder.Default, MosaicSortOrder.Load(Read, "other", MosaicSortView.TagBrowse));
        Assert.NotEqual(MosaicSortOrder.SettingsKey("p", MosaicSortView.Folders), MosaicSortOrder.SettingsKey("p", MosaicSortView.TagBrowse));
    }

    [Fact]
    public void Date_taken_format_is_sortable_text()
    {
        Assert.Equal("2024-03-05T07:08:09", DateTakenFormat.Format(new DateTime(2024, 3, 5, 7, 8, 9)));
        Assert.Null(DateTakenFormat.Format(new DateTime(1, 1, 1)));
        Assert.True(DateTakenFormat.MayHaveExif(@"C:\a\b.JPG"));
        Assert.True(DateTakenFormat.MayHaveExif("x.heic"));
        Assert.False(DateTakenFormat.MayHaveExif("x.mp4"));
        Assert.False(DateTakenFormat.MayHaveExif("x.psd"));
    }

    [Fact]
    public async Task Every_order_runs_on_the_catalog_and_date_taken_falls_back_to_modified()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-sort-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new PalaceDb(Path.Combine(dir, "palace.db"));
            await db.WriteAsync(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO SourceFolder (Id, Path) VALUES ('s', 'C:\src');
                    INSERT INTO Asset (Id, SourceFolderId, Path, FileName, Kind, DateAdded, DateModified, FileSize, Rating, DateTaken)
                    VALUES ('old', 's', 'C:\src\b.jpg', 'b.jpg', 'Image', '2026-01-03', '2026-01-01T00:00:00.0000000Z', 10, 5, '2019-06-01T10:00:00'),
                           ('none', 's', 'C:\src\a.png', 'a.png', 'Image', '2026-01-02', '2025-01-01T00:00:00.0000000Z', NULL, 0, NULL),
                           ('new', 's', 'C:\src\c.jpg', 'c.jpg', 'Video', '2026-01-01', '2020-01-01T00:00:00.0000000Z', 30, 2, '2024-06-01T10:00:00');
                    """;
                cmd.ExecuteNonQuery();
            });

            async Task<List<string>> Order(MosaicSort sort) => await db.ReadAsync(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id FROM Asset " + MosaicSortOrder.OrderBySql(sort);
                using var reader = cmd.ExecuteReader();
                var ids = new List<string>();
                while (reader.Read())
                {
                    ids.Add(reader.GetString(0));
                }

                return ids;
            });

            foreach (var sort in Enum.GetValues<MosaicSort>())
            {
                Assert.Equal(3, (await Order(sort)).Count);
            }

            Assert.Equal(["none", "new", "old"], await Order(MosaicSort.DateTakenNewest));
            Assert.Equal(["old", "new", "none"], await Order(MosaicSort.DateTakenOldest));
            Assert.Equal(["new", "old", "none"], await Order(MosaicSort.Size));
            Assert.Equal(["old", "new", "none"], await Order(MosaicSort.SizeSmallest));
            Assert.Equal(["old", "new", "none"], await Order(MosaicSort.Rating));
            Assert.Equal(["new", "old", "none"], await Order(MosaicSort.RatingLowest));
            Assert.Equal(["new", "none", "old"], await Order(MosaicSort.DateAddedOldest));
            Assert.Equal(["new", "none", "old"], await Order(MosaicSort.TypeZ));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
