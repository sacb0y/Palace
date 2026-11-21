using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagLetterFoldTests
{
    [Fact]
    public void AutomationName_MatchesIsiacFormat()
    {
        Assert.Equal("C, 7 tags, collapsed", TagLetterFold.AutomationName("C", 7, expanded: false));
        Assert.Equal("A, 1 tag, expanded", TagLetterFold.AutomationName("A", 1, expanded: true));
    }

    [Fact]
    public void CollapseAll_CollapsesLetters_AndClearsRealizedChips()
    {
        var boardKey = "character";
        var collapsed = new HashSet<string>(StringComparer.Ordinal);
        var letters = new[]
        {
            new LetterStub("A", "alpha", "amber") { IsExpanded = true },
            new LetterStub("B", "bravo") { IsExpanded = true }
        };

        SetAll(letters, expanded: false, collapsed, boardKey);

        Assert.All(letters, letter => Assert.False(letter.IsExpanded));
        Assert.All(letters, letter => Assert.Empty(letter.RealizedChips));
        Assert.Contains(TagLetterFold.CollapseKey(boardKey, "A"), collapsed);
        Assert.Contains(TagLetterFold.CollapseKey(boardKey, "B"), collapsed);
        Assert.Equal("A, 2 tags, collapsed", TagLetterFold.AutomationName("A", 2, false));
    }

    [Fact]
    public void ExpandAll_ExpandsLetters_AndRealizesChips()
    {
        var boardKey = "all";
        var collapsed = new HashSet<string>(StringComparer.Ordinal)
        {
            TagLetterFold.CollapseKey(boardKey, "A"),
            TagLetterFold.CollapseKey(boardKey, "Z")
        };
        var letters = new[]
        {
            new LetterStub("A", "alpha") { IsExpanded = false },
            new LetterStub("Z", "zeta") { IsExpanded = false }
        };

        SetAll(letters, expanded: true, collapsed, boardKey);

        Assert.All(letters, letter => Assert.True(letter.IsExpanded));
        Assert.Single(letters[0].RealizedChips);
        Assert.Single(letters[1].RealizedChips);
        Assert.Empty(collapsed);
        Assert.Equal("Z, 1 tag, expanded", TagLetterFold.AutomationName("Z", 1, true));
    }

    [Fact]
    public void NewSections_DefaultExpanded_UnlessSessionCollapsed()
    {
        var collapsed = new HashSet<string>(StringComparer.Ordinal)
        {
            TagLetterFold.CollapseKey("all", "B")
        };
        Assert.True(TagLetterFold.IsExpandedDefault(collapsed, "all", "A"));
        Assert.False(TagLetterFold.IsExpandedDefault(collapsed, "all", "B"));
        Assert.True(TagLetterFold.IsExpandedDefault(collapsed, "other", "B"));
    }

    [Fact]
    public void ExpandAllBoardGroups_Source_FoldsLettersNotSidebarGroups()
    {
        var root = RepoRoot();
        var cs = File.ReadAllText(Path.Combine(root, "ViewModels", "TagsViewModel.cs"));
        var expand = cs.IndexOf("private void ExpandAllBoardGroups()", StringComparison.Ordinal);
        Assert.True(expand >= 0);
        var collapse = cs.IndexOf("private void CollapseAllBoardGroups()", expand, StringComparison.Ordinal);
        Assert.True(collapse > expand);
        var next = cs.IndexOf("private void SetAllLetterSectionsExpanded", collapse, StringComparison.Ordinal);
        Assert.True(next > collapse);
        var end = cs.IndexOf("public void ToggleLetterSection", next, StringComparison.Ordinal);
        Assert.True(end > next);
        var body = cs[expand..end];
        Assert.Contains("SetAllLetterSectionsExpanded", body, StringComparison.Ordinal);
        Assert.Contains("SelectedBoard.Letters", cs[next..], StringComparison.Ordinal);
        Assert.DoesNotContain("group.IsExpanded", body, StringComparison.Ordinal);
    }

    private static void SetAll(
        IReadOnlyList<LetterStub> letters,
        bool expanded,
        ISet<string> collapsed,
        string boardKey)
    {
        foreach (var letter in letters)
        {
            letter.IsExpanded = expanded;
            TagLetterFold.RememberExpanded(collapsed, boardKey, letter.Letter, expanded);
        }
    }

    private sealed class LetterStub(string letter, params string[] chips)
    {
        public string Letter { get; } = letter;
        public bool IsExpanded { get; set; } = true;
        public List<string> Chips { get; } = [..chips];
        public IEnumerable<string> RealizedChips =>
            TagLetterFold.RealizedChips(IsExpanded, Chips);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }
}
