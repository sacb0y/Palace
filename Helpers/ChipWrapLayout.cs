using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Palace.Helpers;

/// <summary>
/// Public chip mosaic for Library Tags browse. WASDK 2.4 keeps inbox
/// <c>WrapLayout</c> inaccessible, so LibraryPage cannot use that type.
/// Desired width is the widest laid-out row (capped at finite available) — never
/// a prior arrange width (that ratcheted Tags root past the HWND).
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
        var children = context.Children;
        var sizes = new (double Width, double Height)[children.Count];
        // Content-sized chips: measure unconstrained so Auto columns win (not Stretch / * fill).
        var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(unconstrained);
            var desired = children[i].DesiredSize;
            sizes[i] = (Math.Max(1, desired.Width), Math.Max(1, desired.Height));
        }

        var finiteAvail = ChipWrap.IsFinitePositive(availableSize.Width);
        if (finiteAvail)
        {
            // Re-measure chips wider than the row so long labels wrap/clip within the board.
            for (var i = 0; i < children.Count; i++)
            {
                if (sizes[i].Width <= availableSize.Width)
                {
                    continue;
                }

                children[i].Measure(new Size(availableSize.Width, double.PositiveInfinity));
                var desired = children[i].DesiredSize;
                sizes[i] = (
                    Math.Max(1, Math.Min(availableSize.Width, desired.Width)),
                    Math.Max(1, desired.Height));
            }
        }

        var wrapWidth = finiteAvail
            ? availableSize.Width
            : Math.Max(1, ChipWrap.NaturalContentWidth(sizes, HorizontalSpacing));
        var slots = ChipWrap.Layout(sizes, wrapWidth, HorizontalSpacing, VerticalSpacing);
        var desiredWidth = ChipWrap.MeasureDesiredWidth(
            sizes, availableSize.Width, HorizontalSpacing, VerticalSpacing);
        return new Size(desiredWidth, ChipWrap.ExtentHeight(slots));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        // Do not InvalidateMeasure here — a prior arrange-width ratchet made Measure
        // return the full board width and blew Tags root past the HWND (Wimbly #197).
        var children = context.Children;
        var sizes = new (double Width, double Height)[children.Count];
        for (var i = 0; i < children.Count; i++)
        {
            var desired = children[i].DesiredSize;
            sizes[i] = (Math.Max(1, desired.Width), Math.Max(1, desired.Height));
        }

        var width = ChipWrap.IsFinitePositive(finalSize.Width)
            ? finalSize.Width
            : Math.Max(1, ChipWrap.NaturalContentWidth(sizes, HorizontalSpacing));

        for (var i = 0; i < children.Count; i++)
        {
            sizes[i] = (
                Math.Max(1, Math.Min(width, sizes[i].Width)),
                sizes[i].Height);
        }

        var slots = ChipWrap.Layout(sizes, width, HorizontalSpacing, VerticalSpacing);
        for (var i = 0; i < children.Count && i < slots.Count; i++)
        {
            var slot = slots[i];
            children[i].Arrange(new Rect(slot.X, slot.Y, slot.Width, slot.Height));
        }

        var height = ChipWrap.ExtentHeight(slots);
        return new Size(
            ChipWrap.IsFinitePositive(finalSize.Width) ? finalSize.Width : ChipWrap.ExtentWidth(slots),
            ChipWrap.IsFinitePositive(finalSize.Height) ? finalSize.Height : height);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChipWrapLayout layout)
        {
            layout.InvalidateMeasure();
        }
    }
}
