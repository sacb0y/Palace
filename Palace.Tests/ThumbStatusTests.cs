using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ThumbStatusTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-thumbstatus-" + Guid.NewGuid().ToString("N"));

    public ThumbStatusTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Thumb() => Path.Combine(_dir, "abc.jpg");

    [Fact]
    public void Legacy_cache_entry_without_sidecar_is_unknown() =>
        Assert.Null(ThumbStatus.Read(Thumb()));

    [Fact]
    public void Placeholder_status_survives_a_cache_read()
    {
        ThumbStatus.Write(Thumb(), isPlaceholder: true);
        Assert.True(ThumbStatus.Read(Thumb()));
    }

    [Fact]
    public void Real_thumbnail_overwrites_placeholder_status()
    {
        ThumbStatus.Write(Thumb(), isPlaceholder: true);
        ThumbStatus.Write(Thumb(), isPlaceholder: false);
        Assert.False(ThumbStatus.Read(Thumb()));
    }

    [Fact]
    public void Delete_makes_status_unknown_again()
    {
        ThumbStatus.Write(Thumb(), isPlaceholder: true);
        ThumbStatus.Delete(Thumb());
        Assert.Null(ThumbStatus.Read(Thumb()));
    }

    [Fact]
    public void Garbage_sidecar_reads_as_unknown()
    {
        File.WriteAllText(ThumbStatus.PathFor(Thumb()), "x");
        Assert.Null(ThumbStatus.Read(Thumb()));
    }
}
