using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class ScanNoticeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-scanwalk-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                foreach (var d in Directory.EnumerateDirectories(_dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Append(_dir))
                {
                    File.SetUnixFileMode(d, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }

                var locked = Path.Combine(_dir, "locked");
                if (Directory.Exists(locked))
                {
                    File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }

            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Clean_report_has_no_notice() =>
        Assert.Null(ScanNotice.Describe(new ScanReport { Added = 3, Updated = 2, Orphaned = 1 }));

    [Fact]
    public void Skipped_formats_are_grouped_by_extension_most_common_first()
    {
        var report = new ScanReport();
        report.NoteSkipped(@"C:\art\a.xyz");
        report.NoteSkipped(@"C:\art\b.XYZ");
        report.NoteSkipped(@"C:\art\c.abc");
        report.NoteSkipped(@"C:\art\README");
        Assert.Equal(4, report.Skipped);
        Assert.Equal(
            "Skipped 4 files in formats Palace can't read yet (.xyz \u00d72, no extension, .abc).",
            ScanNotice.Describe(report));
    }

    [Fact]
    public void Undecodable_and_failed_files_name_examples_and_count_the_rest()
    {
        var report = new ScanReport();
        foreach (var n in new[] { "a.png", "b.png", "c.png", "d.png", "e.png" })
        {
            report.NoteUndecodable(Path.Combine("lib", n));
        }

        report.NoteError(Path.Combine("lib", "x.jpg"), new IOException("The process cannot access the file.\r\nMore detail"));
        var text = ScanNotice.Describe(report)!;
        Assert.Contains("5 files couldn't be decoded and show a placeholder (a.png, b.png, c.png and 2 more).", text);
        Assert.Contains("1 file couldn't be indexed (x.jpg: The process cannot access the file).", text);
        Assert.Equal(1, report.Errors);
    }

    [Fact]
    public void Inaccessible_folders_are_reported_separately_from_file_errors()
    {
        var report = new ScanReport();
        report.NoteInaccessibleFolders(2);
        Assert.Equal(2, report.Errors);
        Assert.Equal("2 folders couldn't be opened (no access or they went away).", ScanNotice.Describe(report));
    }

    [Fact]
    public void Add_merges_counts_and_caps_examples()
    {
        var total = new ScanReport { Added = 1 };
        var part = new ScanReport { Added = 2, Updated = 1, Orphaned = 3 };
        part.NoteSkipped("a.txt");
        for (var i = 0; i < ScanReport.MaxListed + 5; i++)
        {
            part.NoteUndecodable($"{i}.png");
        }

        total.NoteSkipped("b.TXT");
        total.Add(part);
        Assert.Equal(3, total.Added);
        Assert.Equal(1, total.Updated);
        Assert.Equal(3, total.Orphaned);
        Assert.Equal(2, total.SkippedByExtension[".txt"]);
        Assert.Equal(ScanReport.MaxListed + 5, total.Undecodable);
        Assert.Equal(ScanReport.MaxListed, total.UndecodablePaths.Count);
    }

    [Theory]
    [InlineData("Thumbs.db", true)]
    [InlineData("desktop.ini", true)]
    [InlineData(".DS_Store", true)]
    [InlineData("._photo.png", true)]
    [InlineData("~$notes.docx", true)]
    [InlineData("workflow.json", false)]
    [InlineData("scene.blend", false)]
    public void Os_bookkeeping_files_are_not_worth_a_notice(string name, bool ignorable) =>
        Assert.Equal(ignorable, ScanNotice.IsIgnorable(Path.Combine("lib", name), FileAttributes.Normal));

    [Fact]
    public void Hidden_or_system_files_are_ignorable() =>
        Assert.True(ScanNotice.IsIgnorable("lib/x.bin", FileAttributes.Hidden));

    [Fact]
    public void ScanWalk_lists_the_same_files_as_EnumerateFiles()
    {
        Touch("a.png");
        Touch("sub/b.jpg");
        Touch("sub/deeper/c.txt");
        Touch("sub/deeper/noext");
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));

        var expected = Directory.EnumerateFiles(_dir, "*.*", SearchOption.AllDirectories).Order().ToList();
        var inaccessible = -1;
        var actual = ScanWalk.Files(_dir, n => inaccessible = n).Select(e => e.Path).Order().ToList();
        Assert.Equal(expected, actual);
        Assert.Equal(0, inaccessible);
    }

    [Fact]
    public void ScanWalk_skips_and_counts_a_folder_it_cannot_open()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Needs an ACL deny; covered by the Unix permission case.
        }

        Touch("a.png");
        Touch("locked/hidden.png");
        Touch("open/b.png");
        var locked = Path.Combine(_dir, "locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        if (Directory.EnumerateFileSystemEntries(locked, "*", new EnumerationOptions { IgnoreInaccessible = true }).Any())
        {
            return; // Running as root: permissions are not enforced.
        }

        // EnumerateFiles would throw out of the whole scan here.
        Assert.Throws<UnauthorizedAccessException>(() => Directory.EnumerateFiles(_dir, "*.*", SearchOption.AllDirectories).ToList());

        var inaccessible = 0;
        var names = ScanWalk.Files(_dir, n => inaccessible = n).Select(e => Path.GetFileName(e.Path)).Order().ToList();
        Assert.Equal(["a.png", "b.png"], names);
        Assert.Equal(1, inaccessible);
    }

    private void Touch(string relative)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }
}
