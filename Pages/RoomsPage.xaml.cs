using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;

namespace Palace.Pages;

public sealed partial class RoomsPage : Page
{
    private int _thumbEpoch;

    public RoomsViewModel ViewModel => AppServices.Rooms;

    public RoomsPage()
    {
        InitializeComponent();
        ViewModel.Sections.CollectionChanged += (_, _) => ScheduleRoomThumbs();
        Loaded += async (_, _) =>
        {
            await ErrorReporter.RunAsync("Load rooms", null, ViewModel.RefreshAsync);
            ScheduleRoomThumbs();
        };
    }

    private void ScheduleRoomThumbs()
    {
        var epoch = Interlocked.Increment(ref _thumbEpoch);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _ = DecodeRoomThumbsAsync(epoch);
        });
    }

    private async Task DecodeRoomThumbsAsync(int epoch)
    {
        if (epoch != _thumbEpoch)
        {
            return;
        }

        var scale = 1.0;
        try
        {
            scale = XamlRoot?.RasterizationScale ?? 1;
        }
        catch
        {
            // XamlRoot is unavailable until the page is in the tree.
        }

        var (boxWidth, boxHeight) = ThumbScheduler.DecodePixels(140, 140, scale);
        var items = ViewModel.Sections.SelectMany(section => section.Items).ToList();
        using var gate = new SemaphoreSlim(ThumbScheduler.EffectiveConcurrencyCap(Environment.ProcessorCount));
        var loads = new List<Task>(items.Count);
        foreach (var item in items)
        {
            if (item.ThumbImage is not null || string.IsNullOrEmpty(item.ThumbPath))
            {
                continue;
            }

            loads.Add(LoadOneAsync(item));
        }

        await Task.WhenAll(loads);

        async Task LoadOneAsync(AssetItem item)
        {
            await gate.WaitAsync();
            try
            {
                if (epoch != _thumbEpoch || item.ThumbImage is not null)
                {
                    return;
                }

                var image = await LibraryPage.DecodeCappedSourceAsync(item.ThumbPath, boxWidth, boxHeight, CancellationToken.None);
                if (image is null || epoch != _thumbEpoch)
                {
                    return;
                }

                await UiDispatch.RunAsync(() =>
                {
                    if (epoch == _thumbEpoch && item.ThumbImage is null)
                    {
                        item.ThumbImage = image;
                    }
                });
            }
            finally
            {
                gate.Release();
            }
        }
    }

    public static IRelayCommand<AssetItem> GetRemovePinCommand() => AppServices.Rooms.RemovePinItemCommand;

    protected override async void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ErrorReporter.RunAsync("Save room order", null, ViewModel.PersistOrderAsync);
    }
}
