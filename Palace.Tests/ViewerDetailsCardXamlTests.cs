using System.Xml.Linq;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Guards the Details card hit-test contract: a False on <c>GrdCardHost</c> makes every
/// button and expander ignore pointers (WinUI does not let a child True override).
/// </summary>
public sealed class ViewerDetailsCardXamlTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }

    private static string? Attr(XElement e, string localName) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    [Fact]
    public void Root_StartsHitTestInvisible_UntilOpen()
    {
        var path = Path.Combine(RepoRoot(), "Controls", "ViewerDetailsCard.xaml");
        Assert.True(File.Exists(path), path);
        var doc = XDocument.Load(path);
        var root = doc.Root;
        Assert.NotNull(root);
        Assert.Equal("False", Attr(root!, "IsHitTestVisible"), StringComparer.Ordinal);
    }

    [Fact]
    public void GrdCardHost_IsHitTestVisible_True()
    {
        var path = Path.Combine(RepoRoot(), "Controls", "ViewerDetailsCard.xaml");
        Assert.True(File.Exists(path), path);
        var doc = XDocument.Load(path);
        var host = doc.Descendants()
            .FirstOrDefault(e => Attr(e, "Name") == "GrdCardHost");
        Assert.NotNull(host);
        var hit = Attr(host!, "IsHitTestVisible");
        Assert.True(
            hit is null or "True",
            "GrdCardHost must stay hit-testable (got IsHitTestVisible=" + (hit ?? "<unset>") + ")");
    }

    [Fact]
    public void ClosedCard_DoesNotStealSwipeUnderInfo()
    {
        Assert.False(ViewerDetailsLayout.CardHitTestVisible(isOpen: false));
        Assert.True(ViewerDetailsLayout.CardHitTestVisible(isOpen: true));
    }

    [Fact]
    public void ActionButtons_BindGalleryCommands_OrCloseClick()
    {
        var path = Path.Combine(RepoRoot(), "Controls", "ViewerDetailsCard.xaml");
        var doc = XDocument.Load(path);
        var buttons = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .ToList();
        Assert.NotEmpty(buttons);

        string[] requiredIds =
        [
            "BtnViewerDetailsCopyName",
            "BtnViewerDetailsClose",
            "BtnViewerDetailsCopyPath",
            "BtnViewerDetailsShowFolder",
            "BtnViewerDetailsFooterCopyPath",
            "BtnViewerDetailsFooterExplorer",
            "BtnViewerDetailsFooterOpenWith",
        ];

        foreach (var id in requiredIds)
        {
            var button = buttons.FirstOrDefault(b => Attr(b, "AutomationProperties.AutomationId") == id);
            Assert.NotNull(button);
            if (id == "BtnViewerDetailsClose")
            {
                Assert.False(string.IsNullOrWhiteSpace(Attr(button!, "Click")));
                continue;
            }

            var command = Attr(button!, "Command");
            Assert.False(string.IsNullOrWhiteSpace(command), id + " missing Command");
            Assert.Contains("Gallery.", command!, StringComparison.Ordinal);
        }
    }
}
