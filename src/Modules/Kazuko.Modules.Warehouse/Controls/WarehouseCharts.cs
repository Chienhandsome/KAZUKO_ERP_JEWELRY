using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kazuko.Modules.Warehouse.Controls;

public sealed class DonutChart : Canvas
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(DonutChart),
            new PropertyMetadata(null, OnItemsChanged));

    public static readonly DependencyProperty CenterTextProperty =
        DependencyProperty.Register(
            nameof(CenterText),
            typeof(string),
            typeof(DonutChart),
            new PropertyMetadata(string.Empty, OnItemsChanged));

    public static readonly DependencyProperty CenterSubTextProperty =
        DependencyProperty.Register(
            nameof(CenterSubText),
            typeof(string),
            typeof(DonutChart),
            new PropertyMetadata(string.Empty, OnItemsChanged));

    private INotifyCollectionChanged? _listening;

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public string CenterSubText
    {
        get => (string)GetValue(CenterSubTextProperty);
        set => SetValue(CenterSubTextProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (DonutChart)d;
        if (e.OldValue is INotifyCollectionChanged oldNotify)
            oldNotify.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newNotify)
        {
            chart._listening = newNotify;
            newNotify.CollectionChanged += chart.OnCollectionChanged;
        }
        chart.Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        var slices = ReadSlices();
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 1 || slices.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(CenterText))
                DrawCenterLabel(ActualWidth / 2, ActualHeight / 2);
            return;
        }

        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var outer = size * 0.42;
        var inner = outer * 0.58;
        var total = slices.Sum(x => x.Value);
        if (total <= 0)
        {
            DrawCenterLabel(cx, cy);
            return;
        }

        var angle = -90.0;
        foreach (var slice in slices)
        {
            var sweep = (double)(slice.Value / total) * 360.0;
            if (sweep <= 0.01)
                continue;
            Children.Add(CreateArc(cx, cy, outer, inner, angle, sweep, ResolveBrush(slice.BrushKey)));
            angle += sweep;
        }

        DrawCenterLabel(cx, cy);
    }

    private void DrawCenterLabel(double cx, double cy)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = CenterText,
            FontWeight = FontWeights.Bold,
            FontSize = TryFindResource("FontSize.Body") as double? ?? 14,
            Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        if (!string.IsNullOrWhiteSpace(CenterSubText))
        {
            panel.Children.Add(new TextBlock
            {
                Text = CenterSubText,
                FontSize = TryFindResource("FontSize.Caption") as double? ?? 12,
                Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
        }

        Children.Add(panel);
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(panel, cx - panel.DesiredSize.Width / 2);
        SetTop(panel, cy - panel.DesiredSize.Height / 2);
    }

    private List<ChartSlice> ReadSlices()
    {
        var list = new List<ChartSlice>();
        if (ItemsSource is null)
            return list;
        foreach (var item in ItemsSource)
        {
            if (item is ChartSliceViewModel vm)
                list.Add(new ChartSlice(vm.Label, vm.Value, vm.BrushKey));
        }
        return list;
    }

    private Brush ResolveBrush(string brushKey) =>
        TryFindResource(brushKey) as Brush
        ?? TryFindResource("PrimaryBrush") as Brush
        ?? Brushes.Orange;

    private static Path CreateArc(double cx, double cy, double outer, double inner, double startAngle, double sweep, Brush brush)
    {
        var startOuter = PointOnCircle(cx, cy, outer, startAngle);
        var endOuter = PointOnCircle(cx, cy, outer, startAngle + sweep);
        var startInner = PointOnCircle(cx, cy, inner, startAngle);
        var endInner = PointOnCircle(cx, cy, inner, startAngle + sweep);
        var large = sweep > 180;

        var figure = new PathFigure { StartPoint = startOuter, IsClosed = true };
        figure.Segments.Add(new ArcSegment(endOuter, new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(endInner, true));
        figure.Segments.Add(new ArcSegment(startInner, new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Path
        {
            Data = geometry,
            Fill = brush,
            Stroke = brush,
            StrokeThickness = 0.5
        };
    }

    private static Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        return new Point(cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad));
    }

    private readonly record struct ChartSlice(string Label, decimal Value, string BrushKey);
}

public sealed class BarChart : Canvas
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(BarChart),
            new PropertyMetadata(null, OnItemsChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (BarChart)d;
        if (e.OldValue is INotifyCollectionChanged oldNotify)
            oldNotify.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newNotify)
            newNotify.CollectionChanged += chart.OnCollectionChanged;
        chart.Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        var items = new List<ChartSliceViewModel>();
        if (ItemsSource is not null)
        {
            foreach (var item in ItemsSource)
            {
                if (item is ChartSliceViewModel vm)
                    items.Add(vm);
            }
        }

        if (items.Count == 0 || ActualWidth <= 1 || ActualHeight <= 1)
            return;

        var max = items.Max(x => x.Value);
        if (max <= 0)
            max = 1;

        var left = 8.0;
        var bottom = 28.0;
        var top = 12.0;
        var chartHeight = Math.Max(20, ActualHeight - top - bottom);
        var slot = (ActualWidth - left * 2) / items.Count;
        var barWidth = Math.Max(12, slot * 0.45);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var height = chartHeight * (double)(item.Value / max);
            var x = left + i * slot + (slot - barWidth) / 2;
            var y = top + chartHeight - height;
            var brush = TryFindResource(item.BrushKey) as Brush
                        ?? TryFindResource("PrimaryBrush") as Brush
                        ?? Brushes.Orange;

            Children.Add(new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(2, height),
                Fill = brush,
                RadiusX = 4,
                RadiusY = 4
            });
            SetLeft(Children[^1], x);
            SetTop(Children[^1], y);

            var label = new TextBlock
            {
                Text = item.Label,
                FontSize = TryFindResource("FontSize.Caption") as double? ?? 11,
                Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
                TextAlignment = TextAlignment.Center,
                Width = slot
            };
            Children.Add(label);
            SetLeft(label, left + i * slot);
            SetTop(label, ActualHeight - 22);

            var valueLabel = new TextBlock
            {
                Text = FormatMillion(item.Value),
                FontSize = TryFindResource("FontSize.Caption") as double? ?? 11,
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.Black,
                TextAlignment = TextAlignment.Center,
                Width = slot
            };
            Children.Add(valueLabel);
            SetLeft(valueLabel, left + i * slot);
            SetTop(valueLabel, Math.Max(0, y - 16));
        }
    }

    private static string FormatMillion(decimal value)
    {
        var millions = value / 1_000_000m;
        // Keep one decimal so small stock values do not collapse to "0".
        return millions.ToString("N1", CultureInfo.GetCultureInfo("vi-VN"));
    }
}
