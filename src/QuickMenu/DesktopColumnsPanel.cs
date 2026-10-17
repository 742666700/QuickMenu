using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace QuickMenu;

/// <summary>
/// Lays out library groups in equal-width column slots. A group with more than
/// <see cref="ColumnItemsPanel.RowsBeforeSplit"/> items may take several slots and split into sub-columns;
/// short groups may share one slot, stacked, so the long group gets room. The arrangement with the
/// shortest tallest column wins. Collapses into one stacked column on small displays.
/// </summary>
public sealed class DesktopColumnsPanel : Panel
{
    /// <summary>Header cost relative to one item row, used only to compare arrangements.</summary>
    private const double HeaderRows = 0.7;

    /// <summary>Groups (by child index) stacked in each slot run, and the slots each run takes. 0 slots means full width.</summary>
    public sealed record ColumnPlan(int[][] Stacks, int[] Slots);

    private ColumnPlan _plan = new(Array.Empty<int[]>(), Array.Empty<int>());

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 760 : availableSize.Width;
        var counts = InternalChildren.Cast<UIElement>().Select(child => ((child as FrameworkElement)?.DataContext as CollectionViewGroup)?.ItemCount ?? 1).ToArray();
        _plan = width < 600 ? new ColumnPlan(new[] { Enumerable.Range(0, counts.Length).ToArray() }, new[] { 0 }) : Plan(counts, Math.Max(1, (int)(width / ColumnItemsPanel.DesktopColumnWidth)));
        var slotWidth = width / Math.Max(1, _plan.Slots.Sum());
        double height = 0;
        for (var s = 0; s < _plan.Stacks.Length; s++)
        {
            var slots = _plan.Slots[s];
            double stackHeight = 0;
            foreach (var index in _plan.Stacks[s])
            {
                var child = InternalChildren[index];
                // Full-width stacks let each group pick its own sub-columns from the width.
                child.SetValue(ColumnItemsPanel.ColumnsProperty, slots == 0 ? 0 : Math.Min(slots, ColumnItemsPanel.ColumnsWanted(counts[index])));
                child.Measure(new Size(slots == 0 ? width : slots * slotWidth, double.PositiveInfinity));
                stackHeight += child.DesiredSize.Height;
            }
            height = Math.Max(height, stackHeight);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var slotWidth = finalSize.Width / Math.Max(1, _plan.Slots.Sum());
        double x = 0;
        for (var s = 0; s < _plan.Stacks.Length; s++)
        {
            var stackWidth = _plan.Slots[s] == 0 ? finalSize.Width : _plan.Slots[s] * slotWidth;
            double y = 0;
            foreach (var index in _plan.Stacks[s].Where(index => index < InternalChildren.Count))
            {
                var child = InternalChildren[index];
                child.Arrange(new Rect(x, y, stackWidth, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }
            x += stackWidth;
        }
        return finalSize;
    }

    /// <summary>
    /// Tries every way to stack neighbouring groups and every slot split, keeping group order.
    /// Picks the shortest tallest column; ties prefer groups side by side, then wider columns.
    /// </summary>
    public static ColumnPlan Plan(IReadOnlyList<int> counts, int slots)
    {
        var n = counts.Count;
        if (n == 0) return new ColumnPlan(Array.Empty<int[]>(), Array.Empty<int>());
        ColumnPlan? best = null;
        var bestCost = double.MaxValue;
        for (var breaks = 0; breaks < 1 << (n - 1); breaks++)
        {
            var stacks = new List<int[]>();
            var current = new List<int> { 0 };
            for (var g = 1; g < n; g++)
            {
                if ((breaks >> (g - 1) & 1) == 1) { stacks.Add(current.ToArray()); current = new List<int>(); }
                current.Add(g);
            }
            stacks.Add(current.ToArray());
            foreach (var split in Splits(stacks.Count, slots))
            {
                var tallest = stacks.Select((stack, s) => stack.Sum(g => HeaderRows + Rows(counts[g], Math.Min(split[s], ColumnItemsPanel.ColumnsWanted(counts[g]))))).Max();
                var cost = tallest * 1000 - stacks.Count * 10 + split.Sum();
                if (cost < bestCost) { bestCost = cost; best = new ColumnPlan(stacks.ToArray(), split); }
            }
        }
        return best ?? new ColumnPlan(new[] { Enumerable.Range(0, n).ToArray() }, new[] { 0 });
    }

    /// <summary>Every way to give each of <paramref name="parts"/> runs at least one slot, using at most <paramref name="slots"/>.</summary>
    private static IEnumerable<int[]> Splits(int parts, int slots)
    {
        if (parts == 0) { yield return Array.Empty<int>(); yield break; }
        for (var first = 1; first <= slots - (parts - 1); first++)
            foreach (var rest in Splits(parts - 1, slots - first))
                yield return rest.Prepend(first).ToArray();
    }

    private static int Rows(int count, int columns) => (count + columns - 1) / columns;
}

/// <summary>
/// Items panel that fills top to bottom, then starts the next column once a column would exceed
/// <see cref="RowsBeforeSplit"/> rows. Reading order stays column by column.
/// </summary>
public sealed class ColumnItemsPanel : Panel
{
    public const int RowsBeforeSplit = 4;
    public const double DesktopColumnWidth = 180;

    /// <summary>Column count chosen by the parent group panel; 0 lets this panel decide from its width.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.RegisterAttached("Columns", typeof(int), typeof(ColumnItemsPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static int GetColumns(DependencyObject element) => (int)element.GetValue(ColumnsProperty);
    public static void SetColumns(DependencyObject element, int value) => element.SetValue(ColumnsProperty, value);

    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(nameof(MinColumnWidth), typeof(double), typeof(ColumnItemsPanel),
        new FrameworkPropertyMetadata(DesktopColumnWidth, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }

    public static int ColumnsWanted(int count) => Math.Max(1, (count + RowsBeforeSplit - 1) / RowsBeforeSplit);

    private int _columns = 1, _rows;
    private double _rowHeight;

    private int ColumnCount(double width)
    {
        var count = Math.Max(1, InternalChildren.Count);
        var assigned = GetColumns(this);
        // The group panel already sized this group for its assigned columns.
        if (assigned > 0) return Math.Min(assigned, count);
        return Math.Clamp(ColumnsWanted(count), 1, Math.Max(1, (int)(width / MinColumnWidth)));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 260 : availableSize.Width;
        _columns = ColumnCount(width);
        _rows = (InternalChildren.Count + _columns - 1) / _columns;
        _rowHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(width / _columns, double.PositiveInfinity));
            _rowHeight = Math.Max(_rowHeight, child.DesiredSize.Height);
        }
        return new Size(width, _rowHeight * _rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_rows == 0) return finalSize;
        var columnWidth = finalSize.Width / _columns;
        for (var i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(i / _rows * columnWidth, i % _rows * _rowHeight, columnWidth, _rowHeight));
        return finalSize;
    }
}
