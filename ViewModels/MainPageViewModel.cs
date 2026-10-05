using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Models;
using Palace.Services;

namespace Palace.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private bool _loading;

    public MainPageViewModel()
    {
        AppServices.Library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.IsBusy))
            {
                OnPropertyChanged(nameof(IsLibraryBusy));
            }
        };
    }

    public bool IsLibraryBusy => AppServices.Library.IsBusy;

    public ObservableCollection<Project> Projects { get; } = [];

    [ObservableProperty]
    public partial Project? SelectedProject { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Palace";

    public Func<string, string, string, Task<string?>>? RequestProjectName { get; set; }

    public async Task LoadAsync()
    {
        _loading = true;
        var currentId = AppServices.CurrentProject.Id;
        Projects.Clear();
        foreach (var project in await AppServices.Catalog.GetProjectsAsync())
        {
            Projects.Add(project);
        }

        SelectedProject = Projects.FirstOrDefault(p => p.Id == currentId) ?? Projects.FirstOrDefault();
        _loading = false;
    }

    partial void OnSelectedProjectChanged(Project? value)
    {
        if (_loading || value is null || value.Id == AppServices.CurrentProject.Id)
        {
            return;
        }

        _ = AppServices.SetCurrentProjectAsync(value);
    }

    [RelayCommand]
    private Task NewProjectAsync() =>
        ErrorReporter.RunAsync("New project", null, async () =>
        {
            if (RequestProjectName is null)
            {
                return;
            }

            var name = await RequestProjectName("New project", "Create", "");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var project = await AppServices.Catalog.CreateProjectAsync(name);
            Projects.Add(project);
            SelectedProject = project;
        });

    [RelayCommand]
    private Task RenameProjectAsync() =>
        ErrorReporter.RunAsync("Rename project", null, async () =>
        {
            if (RequestProjectName is null || SelectedProject is null)
            {
                return;
            }

            var name = await RequestProjectName("Rename project", "Rename", SelectedProject.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await AppServices.Catalog.RenameProjectAsync(SelectedProject.Id, name);
            var id = SelectedProject.Id;
            await LoadAsync();
            var renamed = Projects.FirstOrDefault(p => p.Id == id);
            if (renamed is not null)
            {
                AppServices.CurrentProject.Name = renamed.Name;
                _loading = true;
                SelectedProject = renamed;
                _loading = false;
            }
        });
}
