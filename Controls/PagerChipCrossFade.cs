using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Palace.Helpers;

namespace Palace.Controls;

/// <summary>
/// True old→new name/position chip cross-fade at pager commit start
/// (docs/specs/swipe-nav.md B4). Call from ChipCrossFadeRequested before
/// navigation runs; the enqueue runs after the still index updates.
/// </summary>
internal static class PagerChipCrossFade
{
    public static void Begin(
        DispatcherQueue dispatcher,
        FrameworkElement newChip,
        Border oldOverlay,
        TextBlock oldTitle,
        TextBlock oldPosition,
        string captureTitle,
        string capturePosition,
        Action? onCompleted = null)
    {
        oldTitle.Text = captureTitle ?? "";
        oldPosition.Text = capturePosition ?? "";
        oldOverlay.Visibility = Visibility.Visible;
        oldOverlay.Opacity = 1;
        newChip.Visibility = Visibility.Visible;
        newChip.Opacity = 0;

        dispatcher.TryEnqueue(() =>
        {
            var board = new Storyboard();
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(SwipeNav.CommitMs / 2)
            };
            Storyboard.SetTarget(fadeOut, oldOverlay);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            board.Children.Add(fadeOut);

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(SwipeNav.CommitMs / 2)
            };
            Storyboard.SetTarget(fadeIn, newChip);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            board.Children.Add(fadeIn);

            board.Completed += (_, _) =>
            {
                oldOverlay.Visibility = Visibility.Collapsed;
                oldOverlay.Opacity = 0;
                // Collapse ephemeral chip so real chrome (arrows / details) owns the name.
                if (newChip is Border newBorder)
                {
                    newBorder.Visibility = Visibility.Collapsed;
                    newBorder.Opacity = 0;
                }
                else
                {
                    newChip.Opacity = 1;
                }

                onCompleted?.Invoke();
            };
            board.Begin();
        });
    }

    public static Border CreateChip(out TextBlock title, out TextBlock position)
    {
        title = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        position = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.8,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(title);
        stack.Children.Add(position);
        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xE0, 0x1C, 0x1C, 0x1C)),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 16, 0, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            Child = stack
        };
    }
}
