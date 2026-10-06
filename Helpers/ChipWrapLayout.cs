using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Palace.Helpers;

/// <summary>
/// Public chip mosaic for Library Tags browse. WASDK 2.4 keeps inbox
/// <c>WrapLayout</c> inaccessible, so LibraryPage cannot use that type.
/// </summary>
public sealed class ChipWrapLayout : NonVirtualizingLayout
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(ChipWrapLayout),
        new PropertyMetadata(6d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(ChipWrapLayout),
        new PropertyMetadata(6d, OnLayoutPropertyChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : 260;
        var children = context.Children;
        var sizes = new (double Width, double Height)[children.Count];
        var constraint = new Size(width, double.PositiveInfinity);
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(constraint);
            var desired = children[i].DesiredSize;
            sizes[i] = (desired.Width, desired.Height);
        }

        var slots = ChipWrap.Layout(sizes, width, HorizontalSpacing, VerticalSpacing);
        return new Size(width, ChipWrap.ExtentHeight(slots));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var width = double.IsFinite(finalSize.Width) && finalSize.Width > 0
            ? finalSize.Width
            : 260;
        var children = context.Children;
        var sizes = new (double Width, double Height)[children.Count];
        for (var i = 0; i < children.Count; i++)
        {
            var desired = children[i].DesiredSize;
            sizes[i] = (desired.Width, desired.Height);
        }

        var slots = ChipWrap.Layout(sizes, width, HorizontalSpacing, VerticalSpacing);
        for (var i = 0; i < children.Count && i < slots.Count; i++)
        {
            var slot = slots[i];
            children[i].Arrange(new Rect(slot.X, slot.Y, slot.Width, slot.Height));
        }

        return new Size(width, ChipWrap.ExtentHeight(slots));
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChipWrapLayout layout)
        {
            layout.InvalidateMeasure();
        }
    }
}
