using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class ScanFastPathTests
{
    private static readonly DateTime Written = new(2026, 10, 18, 16, 30, 0, DateTimeKind.Utc);

    private static Asset Indexed() => new()
    {
        Path = @"C:\art\a.png",
        FileSize = 1234,
        DateModified = ScanFastPath.Stamp(Written),
        ContentHash = "abc123",
        Width = 640,
        Height = 480,
        IndexVersion = ScanFastPath.IndexVersion
    };

    [Fact]
    public void Stamp_matches_what_the_scan_has_always_stored() =>
        Assert.Equal(new FileInfo(typeof(ScanFastPathTests).Assembly.Location).LastWriteTimeUtc.ToString("O"),
            ScanFastPath.Stamp(new FileInfo(typeof(ScanFastPathTests).Assembly.Location).LastWriteTimeUtc));

    [Fact]
    public void Unchanged_indexed_file_takes_the_fast_path() =>
        Assert.True(ScanFastPath.IsUnchanged(Indexed(), 1234, Written, onlineOnly: false));

    [Fact]
    public void New_file_does_not() =>
        Assert.False(ScanFastPath.IsUnchanged(null, 1234, Written, onlineOnly: false));

    public static TheoryData<string> Changes => new()
    {
        "size", "time", "version", "orphan", "online", "was-online", "stub-hash", "no-hash", "no-size"
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Any_change_or_stale_row_takes_the_full_path(string change)
    {
        var asset = Indexed();
        long length = 1234;
        var written = Written;
        var onlineOnly = false;
        switch (change)
        {
            case "size": length = 1235; break;
            case "time": written = Written.AddTicks(1); break;
            case "version": asset.IndexVersion = 0; break;
            case "orphan": asset.IsOrphan = true; break;
            case "online": onlineOnly = true; break;
            case "was-online": asset.IsOnlineOnly = true; break;
            case "stub-hash": asset.ContentHash = Palace.Services.HashService.CloudStubHash(asset.Path, 1234, Written); break;
            case "no-hash": asset.ContentHash = null; break;
            case "no-size": asset.Width = null; break;
        }

        Assert.False(ScanFastPath.IsUnchanged(asset, length, written, onlineOnly));
    }

    [Fact]
    public void Progress_is_throttled()
    {
        long last = 0;
        Assert.True(ScanFastPath.ShouldReport(ref last, 1000));
        Assert.False(ScanFastPath.ShouldReport(ref last, 1000 + ScanFastPath.ProgressIntervalMs - 1));
        Assert.True(ScanFastPath.ShouldReport(ref last, 1000 + ScanFastPath.ProgressIntervalMs));
    }

    [Fact]
    public async Task Catalog_gains_an_IndexVersion_column_defaulting_to_zero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-fastpath-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new PalaceDb(Path.Combine(dir, "palace.db"));
            var dflt = await db.ReadAsync(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT dflt_value FROM pragma_table_info('Asset') WHERE name = 'IndexVersion'";
                return cmd.ExecuteScalar() as string;
            });
            Assert.Equal("0", dflt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
