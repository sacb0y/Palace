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
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty HeaderHeightProperty = DependencyProperty.Register(
        nameof(HeaderHeight),
        typeof(double),
        typeof(JustifiedMosaicLayout),
        new PropertyMetadata(GalleryMedia.FolderHeaderHeight, OnLayoutPropertyChanged));

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

    public double HeaderHeight
    {
        get => (double)GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    /// <summary>Width used by the last arrange / measure recalculation.</summary>
    public double LastLaidOutWidth { get; private set; }

    /// <summary>
    /// ItemsView ActualWidth from the page. Used when measure available width is
    /// infinite/tiny so packing uses the real mosaic column, not a stale last-good.
    /// </summary>
    public double HostWidth { get; private set; }

    /// <summary>Pinnable (non-packed) header slots from the last arrange, sorted by Y.</summary>
    public IReadOnlyList<MosaicHeaderSlot> LastPinnableHeaders { get; private set; } = [];

    /// <summary>
    /// WASDK 2.4 keeps Layout.InvalidateMeasure protected.
    /// LibraryPage calls this instead of reaching into Layout.
    /// </summary>
    public void Relayout() => InvalidateMeasure();

    /// <summary>Stamp the host ActualWidth and invalidate when it changes.</summary>
    public void SetHostWidth(double width)
    {
        if (double.IsFinite(width) && width > 0 && Math.Abs(HostWidth - width) > 0.5)
        {
            HostWidth = width;
            InvalidateMeasure();
        }
        else if (!double.IsFinite(HostWidth) || HostWidth <= 0)
        {
            HostWidth = double.IsFinite(width) && width > 0 ? width : 0;
        }
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
        LastPinnableHeaders = [];
        LastLaidOutWidth = 0;
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var state = GetState(context);
        var width = ResolveWidth(availableSize.Width);
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
        var width = ResolveWidth(finalSize.Width);
        if (state.Rects.Count != context.ItemCount || Math.Abs(state.LaidOutWidth - width) > 0.5)
        {
            Recalculate(context, state, width);
        }

        // Mutate IsPacked only here — Measure must not flip templates mid-pass.
        StampPackedFlags(context, state.Slots);

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
        state.Slots.Clear();
        state.LaidOutWidth = width;
        state.ExtentHeight = 0;
        LastLaidOutWidth = width;
        var count = context.ItemCount;
        if (count == 0 || width <= 0)
        {
            LastPinnableHeaders = [];
            return;
        }

        var specs = new MosaicSpec[count];
        for (var i = 0; i < count; i++)
        {
            specs[i] = SpecOf(context, i);
        }

        var slots = MosaicRows.Layout(
            specs,
            width,
            RowHeight,
            ItemSpacing,
            HeaderHeight,
            CaptionHeight);
        foreach (var slot in slots)
        {
            state.Slots.Add(slot);
            state.Rects.Add(new Rect(slot.X, slot.Y, slot.Width, slot.Height));
        }

        while (state.Rects.Count < count)
        {
            state.Slots.Add(new MosaicSlot(0, state.ExtentHeight, 1, HeaderHeight));
            state.Rects.Add(new Rect(0, state.ExtentHeight, 1, HeaderHeight));
        }

        var headers = MosaicRows.CollectHeaderSlots(
            state.Slots,
            i => SpecOf(context, i).Kind == MosaicSlotKind.FullHeader);
        LastPinnableHeaders = MosaicRows.PinnableHeaderSlots(headers);

        state.ExtentHeight = MosaicRows.ExtentHeight(state.Slots, ItemSpacing);
        if (state.Rects.Count > 0)
        {
            var last = state.Rects[^1];
            state.ExtentHeight = Math.Max(state.ExtentHeight, last.Y + last.Height);
        }
    }

    /// <summary>
    /// Stamp <see cref="AssetItem.IsPacked"/> only from Arrange (not Measure) so a
    /// PropertyChanged → remeasure loop cannot flicker across the pack threshold.
    /// </summary>
    private static void StampPackedFlags(VirtualizingLayoutContext context, IReadOnlyList<MosaicSlot> slots)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            try
            {
                if (context.GetItemAt(i) is AssetItem { IsFolderHeader: true } header)
                {
                    var packed = slots[i].IsPacked;
                    if (header.IsPacked != packed)
                    {
                        header.IsPacked = packed;
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
            }
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

    /// <summary>
    /// Prefer a usable finite width; ignore tiny probe passes that collapse the
    /// grid to a sliver and prevent packed mini-headers from appearing.
    /// </summary>
    private double ResolveWidth(double available) =>
        MosaicLayoutWidth.Resolve(available, LastLaidOutWidth, HostWidth);

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

    private static MosaicSpec SpecOf(VirtualizingLayoutContext context, int index)
    {
        try
        {
            if (context.GetItemAt(index) is AssetItem asset)
            {
                if (asset.IsFolderHeader)
                {
                    return new MosaicSpec(
                        MosaicSlotKind.FullHeader,
                        Aspect: 0,
                        Collapsed: asset.IsFolderCollapsed,
                        MiniWidth: MosaicRows.MiniHeaderWidth(
                            asset.FolderGroupTitle,
                            asset.FolderItemCount));
                }

                return new MosaicSpec(
                    MosaicSlotKind.Asset,
                    MosaicRows.ClampTileAspect(asset.AspectRatio));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return new MosaicSpec(MosaicSlotKind.Asset, 1);
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
        public List<MosaicSlot> Slots { get; } = [];
        public Dictionary<int, UIElement> Realized { get; } = [];
        public double LaidOutWidth { get; set; }
        public double ExtentHeight { get; set; }
    }
}
