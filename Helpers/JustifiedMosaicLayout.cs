using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Palace.Models;
using Windows.Foundation;

namespace Palace.Helpers;

public sealed class JustifiedMosaicLayout : VirtualizingLayout
{
    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight),
        typeof(double),
        typeof(JustifiedMosaicLayout),
        new PropertyMetadata(140d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register(
        nameof(ItemSpacing),
        typeof(double),
        typeof(JustifiedMosaicLayout),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty CaptionHeightProperty = DependencyProperty.Register(
        nameof(CaptionHeight),
        typeof(double),
        typeof(JustifiedMosaicLayout),
        new PropertyMetadata(22d, OnLayoutPropertyChanged));

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public double CaptionHeight
    {
        get => (double)GetValue(CaptionHeightProperty);
        set => SetValue(CaptionHeightProperty, value);
    }

    protected override void InitializeForContextCore(VirtualizingLayoutContext context)
    {
        context.LayoutState ??= new MosaicState();
    }

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        if (context.LayoutState is MosaicState state)
        {
            state.Realized.Clear();
        }

        context.LayoutState = null;
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var state = GetState(context);
        var width = FiniteWidth(availableSize.Width);
        Recalculate(context, state, width);

        var realization = RealizationWindow(context, width);
        RecycleOutside(context, state, realization);

        for (var i = 0; i < state.Rects.Count; i++)
        {
            if (!Intersects(realization, state.Rects[i]))
            {
                continue;
            }

            var element = context.GetOrCreateElementAt(i);
            state.Realized[i] = element;
            var rect = state.Rects[i];
            element.Measure(new Size(rect.Width, rect.Height));
        }

        return new Size(width, state.ExtentHeight);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        var state = GetState(context);
        var width = FiniteWidth(finalSize.Width);
        if (state.Rects.Count != context.ItemCount || Math.Abs(state.LaidOutWidth - width) > 0.5)
        {
            Recalculate(context, state, width);
        }

        var realization = RealizationWindow(context, width);
        for (var i = 0; i < state.Rects.Count; i++)
        {
            if (!Intersects(realization, state.Rects[i]))
            {
                continue;
            }

            var element = context.GetOrCreateElementAt(i);
            state.Realized[i] = element;
            element.Arrange(state.Rects[i]);
        }

        return new Size(width, state.ExtentHeight);
    }

    private void Recalculate(VirtualizingLayoutContext context, MosaicState state, double width)
    {
        state.Rects.Clear();
        state.LaidOutWidth = width;
        state.ExtentHeight = 0;
        var count = context.ItemCount;
        if (count == 0 || width <= 0)
        {
            return;
        }

        var targetHeight = Math.Clamp(RowHeight, 96, 280);
        var spacing = ItemSpacing;
        var caption = CaptionHeight;
        var usable = Math.Max(1, width);
        var row = new List<(int Index, double Aspect)>();
        var rowAspect = 0d;
        var y = 0d;

        void Flush(bool justify)
        {
            if (row.Count == 0)
            {
                return;
            }

            var gaps = spacing * Math.Max(0, row.Count - 1);
            var height = justify
                ? (usable - gaps) / rowAspect
                : targetHeight;
            var natural = height * rowAspect + gaps;
            if (natural > usable)
            {
                height = (usable - gaps) / rowAspect;
            }

            var x = 0d;
            foreach (var (index, aspect) in row)
            {
                var itemWidth = Math.Max(1, height * aspect);
                state.Rects.Add(new Rect(x, y, itemWidth, height + caption));
                x += itemWidth + spacing;
            }

            y += height + caption + spacing;
            row.Clear();
            rowAspect = 0;
        }

        for (var i = 0; i < count; i++)
        {
            var aspect = AspectOf(context, i);
            var nextAspect = rowAspect + aspect;
            var nextWidth = targetHeight * nextAspect + spacing * row.Count;
            if (row.Count > 0 && nextWidth > usable)
            {
                Flush(justify: true);
            }

            row.Add((i, aspect));
            rowAspect += aspect;
        }

        Flush(justify: false);
        state.ExtentHeight = Math.Max(0, y - spacing);
        while (state.Rects.Count < count)
        {
            state.Rects.Add(new Rect(0, state.ExtentHeight, 1, targetHeight + caption));
        }
    }

    private static void RecycleOutside(VirtualizingLayoutContext context, MosaicState state, Rect realization)
    {
        foreach (var index in state.Realized.Keys.ToList())
        {
            if (index < state.Rects.Count && Intersects(realization, state.Rects[index]))
            {
                continue;
            }

            context.RecycleElement(state.Realized[index]);
            state.Realized.Remove(index);
        }
    }

    private static MosaicState GetState(VirtualizingLayoutContext context)
    {
        if (context.LayoutState is MosaicState state)
        {
            return state;
        }

        state = new MosaicState();
        context.LayoutState = state;
        return state;
    }

    private static double FiniteWidth(double width) =>
        double.IsFinite(width) && width > 0 ? width : 640;

    private static Rect RealizationWindow(VirtualizingLayoutContext context, double width)
    {
        var rect = context.RealizationRect;
        if (rect.Height <= 0 || double.IsInfinity(rect.Height) || double.IsNaN(rect.Height))
        {
            return new Rect(0, 0, width, 1200);
        }

        return rect;
    }

    private static bool Intersects(Rect a, Rect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    private static double AspectOf(VirtualizingLayoutContext context, int index)
    {
        try
        {
            if (context.GetItemAt(index) is AssetItem asset)
            {
                return Math.Max(0.15, asset.AspectRatio);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return 1;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JustifiedMosaicLayout layout)
        {
            layout.InvalidateMeasure();
        }
    }

    private sealed class MosaicState
    {
        public List<Rect> Rects { get; } = [];
        public Dictionary<int, UIElement> Realized { get; } = [];
        public double LaidOutWidth { get; set; }
        public double ExtentHeight { get; set; }
    }
}
