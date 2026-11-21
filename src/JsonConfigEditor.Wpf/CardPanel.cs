using System.Windows;
using System.Windows.Controls;

namespace JsonConfigEditor.Wpf;

/// <summary>
/// Karten in N gleich breiten Spalten. Anders als UniformGrid ist jede Zeile nur so hoch wie ihre
/// höchste Karte, und ausgeblendete Karten (Collapsed) hinterlassen keine Lücke.
/// </summary>
public sealed class CardPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(CardPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure), v => (int)v >= 1);

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private IEnumerable<UIElement> Visible => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed);

    protected override Size MeasureOverride(Size available)
    {
        var cols = Math.Max(1, Columns);
        var colWidth = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width / cols;
        double height = 0, rowHeight = 0, maxRowWidth = 0;
        var i = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(colWidth, double.PositiveInfinity));
        }
        foreach (var child in Visible)
        {
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            maxRowWidth = Math.Max(maxRowWidth, child.DesiredSize.Width * cols);
            if (++i % cols == 0) { height += rowHeight; rowHeight = 0; }
        }
        height += rowHeight;
        var width = double.IsInfinity(available.Width) ? maxRowWidth : available.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cols = Math.Max(1, Columns);
        var colWidth = final.Width / cols;
        var visible = Visible.ToList();
        double y = 0;
        for (var row = 0; row * cols < visible.Count; row++)
        {
            var items = visible.Skip(row * cols).Take(cols).ToList();
            var h = items.Max(c => c.DesiredSize.Height);
            for (var c = 0; c < items.Count; c++)
                items[c].Arrange(new Rect(c * colWidth, y, colWidth, h));
            y += h;
        }
        foreach (UIElement child in InternalChildren)
            if (child.Visibility == Visibility.Collapsed) child.Arrange(new Rect());
        return final;
    }
}
