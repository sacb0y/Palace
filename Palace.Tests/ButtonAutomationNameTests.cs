using System.Xml.Linq;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// UIA does not derive a name from icon + TextBlock button content, so Narrator reads those
/// buttons as just "button". Every button whose content is not a plain string must set
/// <c>AutomationProperties.Name</c> (normally the visible label, or a fuller form of it).
/// </summary>
public sealed class ButtonAutomationNameTests
{
    private static readonly HashSet<string> ButtonTags =
    [
        "Button", "ToggleButton", "DropDownButton", "SplitButton", "ToggleSplitButton", "HyperlinkButton", "RepeatButton"
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }

    private static IEnumerable<string> XamlFiles(string root) =>
        Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or "Palace.Tests"));

    private static string? Attr(XElement e, string localName) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    [Fact]
    public void Icon_and_text_buttons_have_an_automation_name()
    {
        var root = RepoRoot();
        var missing = new List<string>();
        var files = XamlFiles(root).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var doc = XDocument.Load(file);
            foreach (var button in doc.Descendants().Where(e => ButtonTags.Contains(e.Name.LocalName)))
            {
                var content = Attr(button, "Content");
                if (content is not null && !content.StartsWith('{'))
                {
                    continue; // plain string content names itself
                }

                var id = Attr(button, "AutomationProperties.AutomationId") ?? Attr(button, "Name") ?? "(no id)";
                var where = $"{Path.GetRelativePath(root, file)}: {button.Name.LocalName} {id}";
                var name = Attr(button, "AutomationProperties.Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    missing.Add(where);
                }
            }
        }

        Assert.True(missing.Count == 0, "Buttons without AutomationProperties.Name:\n" + string.Join("\n", missing));
    }
}
