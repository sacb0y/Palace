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

    /// <summary>Last finite arrange width — used when Measure gets infinity (StackPanel).</summary>
    private double _arrangeWidth;

    /// <summary>Width used for the last MeasureOverride extent (detect arrange mismatch).</summary>
    private double _measuredWidth;

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
        var width = ResolveWidth(availableSize.Width);
        _measuredWidth = width;
        var children = context.Children;
        var sizes = new (double Width, double Height)[children.Count];
        // Content-sized chips: measure unconstrained so Auto columns win (not Stretch / * fill).
        var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(unconstrained);
            var desired = children[i].DesiredSize;
            var w = Math.Max(1, desired.Width);
            if (w > width)
            {
                // Re-measure at row width so long labels can wrap/clip within the board.
                children[i].Measure(new Size(width, double.PositiveInfinity));
                desired = children[i].DesiredSize;
                w = Math.Max(1, Math.Min(width, desired.Width));
            }

            sizes[i] = (w, Math.Max(1, desired.Height));
        }

        var slots = ChipWrap.Layout(sizes, width, HorizontalSpacing, VerticalSpacing);
        return new Size(width, ChipWrap.ExtentHeight(slots));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var width = ResolveWidth(finalSize.Width);
        if (WidthChanged(width, _arrangeWidth) || WidthChanged(width, _measuredWidth))
        {
            _arrangeWidth = width;
            InvalidateMeasure();
        }
        else
        {
            _arrangeWidth = width;
        }

        var children = context.Children;
        var sizes = new (double Width, double Height)[children.Count];
        for (var i = 0; i < children.Count; i++)
        {
            var desired = children[i].DesiredSize;
            sizes[i] = (
                Math.Max(1, Math.Min(width, desired.Width)),
                Math.Max(1, desired.Height));
        }

        var slots = ChipWrap.Layout(sizes, width, HorizontalSpacing, VerticalSpacing);
        for (var i = 0; i < children.Count && i < slots.Count; i++)
        {
            var slot = slots[i];
            children[i].Arrange(new Rect(slot.X, slot.Y, slot.Width, slot.Height));
        }

        return new Size(width, ChipWrap.ExtentHeight(slots));
    }

    private double ResolveWidth(double candidate)
    {
        if (double.IsFinite(candidate) && candidate > 0)
        {
            return candidate;
        }

        if (double.IsFinite(_arrangeWidth) && _arrangeWidth > 0)
        {
            return _arrangeWidth;
        }

        return 260;
    }

    private static bool WidthChanged(double a, double b) =>
        double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) > 0.5;

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChipWrapLayout layout)
        {
            layout.InvalidateMeasure();
        }
    }
}
