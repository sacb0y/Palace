using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using Palace.Services.Cloud;

namespace Palace.Pages;

public sealed partial class CloudFolderPickerDialog : ContentDialog
{
    private readonly ICloudLibrary _library;
    private readonly List<CloudEntry> _stack = [];

    public CloudFolderPickerDialog(ICloudLibrary library, string? projectName = null)
    {
        _library = library;
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            Title = "Pick a cloud folder for " + projectName;
        }

        Opened += async (_, _) => await LoadChildrenAsync();
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    public ObservableCollection<CloudEntry> Folders { get; } = [];

    public CloudEntry? Result { get; private set; }

    private async void BtnCloudFolderUp_Click(object sender, RoutedEventArgs e)
    {
        if (_stack.Count == 0)
        {
            return;
        }

        _stack.RemoveAt(_stack.Count - 1);
        await LoadChildrenAsync();
    }

    private async void LstCloudFolders_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CloudEntry folder)
        {
            return;
        }

        _stack.Add(folder);
        await LoadChildrenAsync();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = CurrentFolder();
    }

    private CloudEntry CurrentFolder()
    {
        if (_stack.Count == 0)
        {
            return new CloudEntry
            {
                Id = CloudSourcePath.NormalizeRootItemId("") ?? "",
                Name = "Root",
                DisplayPath = CloudSourcePath.FolderDisplay(null),
                IsFolder = true
            };
        }

        var names = string.Join('/', _stack.Select(s => s.Name));
        var current = _stack[^1];
        return new CloudEntry
        {
            Id = CloudSourcePath.NormalizeRootItemId(current.Id) ?? current.Id,
            Name = current.Name,
            DisplayPath = CloudSourcePath.FolderDisplay(names),
            IsFolder = true
        };
    }

    private async Task LoadChildrenAsync()
    {
        Folders.Clear();
        TxtCloudFolderPath.Text = _stack.Count == 0 ? "Root" : string.Join(" / ", _stack.Select(s => s.Name));
        BtnCloudFolderUp.IsEnabled = _stack.Count > 0;
        var parentId = _stack.Count == 0 ? null : _stack[^1].Id;
        if (parentId == "")
        {
            parentId = null;
        }

        IReadOnlyList<CloudEntry> children;
        try
        {
            children = await _library.ListChildrenAsync(parentId);
        }
        catch (CloudAuthException ex)
        {
            TxtCloudFolderPath.Text = ex.Message;
            return;
        }

        foreach (var child in children.Where(c => c.IsFolder).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            Folders.Add(child);
        }
    }
}
