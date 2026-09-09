using CommunityToolkit.Mvvm.ComponentModel;

namespace Palace.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Palace";
}
