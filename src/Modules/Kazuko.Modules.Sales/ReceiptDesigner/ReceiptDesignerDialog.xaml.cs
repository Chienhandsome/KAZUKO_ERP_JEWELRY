using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Rectangle = System.Windows.Shapes.Rectangle;
using Cursors = System.Windows.Input.Cursors;

namespace Kazuko.Modules.Sales.ReceiptDesigner;

public partial class ReceiptDesignerDialog : Window
{
    private readonly ReceiptDesignerViewModel _vm;
    private bool _renderPending;
    private bool _revealSelection;
    private ReceiptDesignerObject? _gesture;
    private string _handle = string.Empty;
    private Point _start;
    private double _left, _top, _width, _height;
    public ReceiptDesignerDialog(ReceiptDesignerViewModel vm)
    {
        InitializeComponent();
        _vm = vm; DataContext = vm;
        FontFamilyCombo.ItemsSource = Fonts.SystemFontFamilies.Select(x => x.Source).OrderBy(x => x).ToArray();
        Loaded += async (_, _) => await vm.InitializeAsync();
        Closing += OnClosing;
        vm.Objects.CollectionChanged += (_, _) => QueueRender();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.DialogResult) && vm.DialogResult is bool result) DialogResult = result;
            if (e.PropertyName == nameof(vm.PreviewImage) && vm.PreviewImage is byte[] bytes) ShowPreview(bytes);
            if (e.PropertyName == nameof(vm.SelectedObject)) _revealSelection = true;
            if (e.PropertyName is nameof(vm.SelectedObject) or nameof(vm.SelectedSection) or nameof(vm.CanvasHeight)) QueueRender();
        };
        vm.Objects.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (ReceiptDesignerObject obj in e.NewItems) obj.PropertyChanged += (_, _) => QueueRender();
        };
        DesignCanvas.MouseMove += OnMouseMove;
        DesignCanvas.MouseLeftButtonUp += (_, _) => EndGesture();
        DesignCanvas.LostMouseCapture += (_, _) => _gesture = null;
        DesignCanvas.PreviewKeyDown += OnCanvasKeyDown;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_vm.IsBusy && DialogResult != true) { e.Cancel = true; return; }
        if (DialogResult != true && _vm.IsDirty && MessageBox.Show(this,
            "Mẫu bill có thay đổi chưa lưu. Bỏ các thay đổi và đóng?", "Sửa mẫu bill",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
    }
    private void QueueRender()
    {
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.BeginInvoke(new Action(() => { _renderPending = false; BuildCanvas(); }));
    }
    private Brush Accent => (Brush)FindResource("PrimaryBrush");
    private void BuildCanvas()
    {
        DesignCanvas.Children.Clear();
        // Fixed white/black colors below represent physical printed paper, like the label designer.
        var tile = new DrawingGroup();
        tile.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 18.8976, 18.8976))));
        for (var i = 0; i < 5; i++)
        {
            var pen = new Pen(i == 0 ? Brushes.LightGray : Brushes.Gainsboro, i == 0 ? .5 : .2);
            tile.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(i * 3.77952, 0), new Point(i * 3.77952, 18.8976))));
            tile.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, i * 3.77952), new Point(18.8976, i * 3.77952))));
        }
        DesignCanvas.Background = new DrawingBrush(tile) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 18.8976, 18.8976), Stretch = Stretch.None };
        foreach (var obj in _vm.Objects.Where(x => !x.IsCell))
        {
            var selected = obj == _vm.SelectedObject;
            var border = new Border { Width = Math.Max(2, obj.Width), Height = Math.Max(2, obj.Height),
                Background = Brushes.Transparent, BorderThickness = new Thickness(selected ? 1 : 0),
                BorderBrush = selected ? Accent : Brushes.Transparent, ClipToBounds = true, ToolTip = obj.Name };
            border.Child = CreateVisual(obj);
            Canvas.SetLeft(border, obj.Left); Canvas.SetTop(border, obj.Top);
            border.MouseLeftButtonDown += (_, e) => StartGesture(obj, string.Empty, e);
            DesignCanvas.Children.Add(border);
        }
        if (_vm.SelectedObject is { CanMove: true } item)
        {
            foreach (var (x, y, handle) in new[] { (0d, 0d, "LT"), (.5, 0d, "T"), (1d, 0d, "RT"),
                (1d, .5, "R"), (1d, 1d, "RB"), (.5, 1d, "B"), (0d, 1d, "LB"), (0d, .5, "L") })
            {
                var rect = new Rectangle { Width = 8, Height = 8, Fill = Brushes.White, Stroke = Accent, StrokeThickness = 1,
                    Cursor = handle is "L" or "R" ? Cursors.SizeWE : handle is "T" or "B" ? Cursors.SizeNS : handle is "LT" or "RB" ? Cursors.SizeNWSE : Cursors.SizeNESW };
                Canvas.SetLeft(rect, item.Left + x * item.Width - 4); Canvas.SetTop(rect, item.Top + y * item.Height - 4);
                rect.MouseLeftButtonDown += (_, e) => StartGesture(item, handle, e);
                DesignCanvas.Children.Add(rect);
            }
        }
        if (_revealSelection && _gesture is null && _vm.SelectedObject is { } selectedItem)
        {
            var target = selectedItem.IsCell
                ? _vm.Objects.FirstOrDefault(x => ReferenceEquals(x.Element, selectedItem.Element.Ancestors("TableObject").FirstOrDefault()))
                : selectedItem;
            if (target is not null)
                DesignCanvas.BringIntoView(new Rect(Math.Max(0, target.Left - 10), Math.Max(0, target.Top - 10), Math.Max(1, target.Width + 20), Math.Max(1, target.Height + 20)));
        }
        _revealSelection = false;
    }
    private FrameworkElement CreateVisual(ReceiptDesignerObject obj)
    {
        if (obj.IsText) return TextVisual(obj);
        if (obj.IsTable)
        {
            var grid = new Grid();
            foreach (var col in obj.Element.Elements("TableColumn")) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, ReceiptDesignerObject.Read(col, "Width", 75.6))) });
            var rows = obj.Element.Elements("TableRow").ToArray();
            foreach (var row in rows) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(1, ReceiptDesignerObject.Read(row, "Height", 18.9))) });
            for (var r = 0; r < rows.Length; r++)
            {
                var c = 0;
                foreach (var cell in rows[r].Elements("TableCell"))
                {
                    if (c >= grid.ColumnDefinitions.Count) break;
                    var item = _vm.Objects.FirstOrDefault(x => ReferenceEquals(x.Element, cell));
                    if (item is null) { c++; continue; }
                    var cellBorder = new Border { BorderBrush = item == _vm.SelectedObject ? Accent : Brushes.Gray,
                        BorderThickness = new Thickness(.5), Child = TextVisual(item), ClipToBounds = true, ToolTip = "Nhấp đúp để sửa ô: " + item.Name };
                    Grid.SetRow(cellBorder, r); Grid.SetColumn(cellBorder, c);
                    Grid.SetColumnSpan(cellBorder, Math.Clamp((int)ReceiptDesignerObject.Read(cell, "ColSpan", 1), 1, grid.ColumnDefinitions.Count - c));
                    Grid.SetRowSpan(cellBorder, Math.Clamp((int)ReceiptDesignerObject.Read(cell, "RowSpan", 1), 1, rows.Length - r));
                    cellBorder.MouseLeftButtonDown += (_, e) => { if (e.ClickCount >= 2) { _vm.SelectedObject = item; e.Handled = true; } };
                    grid.Children.Add(cellBorder); c++;
                }
            }
            return grid;
        }
        if (obj.Element.Name.LocalName == "LineObject")
            return new Line { X2 = obj.Width, Y2 = obj.Height, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false };
        return new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(.5), Child = new TextBlock
            { Text = obj.Element.Name.LocalName == "SubreportObject" ? "Bảng phụ · " + (string?)obj.Element.Attribute("ReportPage") : obj.Name,
                Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap }, IsHitTestVisible = false };
    }
    private static TextBlock TextVisual(ReceiptDesignerObject obj) => new()
    {
        Text = obj.Text, FontFamily = new FontFamily(obj.FontFamilyName), FontSize = obj.FontSize * 96 / 72,
        FontWeight = obj.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = obj.Italic ? FontStyles.Italic : FontStyles.Normal,
        Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false,
        TextAlignment = obj.HorzAlign switch { "Center" => TextAlignment.Center, "Right" => TextAlignment.Right, "Justify" => TextAlignment.Justify, _ => TextAlignment.Left },
        VerticalAlignment = obj.VertAlign switch { "Center" => VerticalAlignment.Center, "Bottom" => VerticalAlignment.Bottom, _ => VerticalAlignment.Top },
        RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(obj.Angle)
    };
    private void StartGesture(ReceiptDesignerObject obj, string handle, MouseButtonEventArgs e)
    {
        if (!_vm.CanEdit || e.ClickCount > 1) return;
        _vm.SelectedObject = obj; _revealSelection = false; _gesture = obj; _handle = handle; _start = e.GetPosition(DesignCanvas);
        _left = obj.Left; _top = obj.Top; _width = obj.Width; _height = obj.Height;
        DesignCanvas.Focus(); DesignCanvas.CaptureMouse(); e.Handled = true;
    }
    private double Snap(double value) => _vm.SnapToGrid ? Math.Round(value / (96 / 25.4)) * (96 / 25.4) : value;
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_gesture is not { } obj || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(DesignCanvas) - _start;
        if (_handle.Length == 0)
        {
            obj.Left = Math.Clamp(Snap(_left + delta.X), 0, Math.Max(0, _vm.CanvasWidth - obj.Width));
            obj.Top = Math.Clamp(Snap(_top + delta.Y), 0, Math.Max(0, _vm.CanvasHeight - obj.Height));
        }
        else
        {
            var left = _handle.Contains('L') ? Math.Clamp(Snap(_left + delta.X), 0, _left + _width - 1) : _left;
            var top = _handle.Contains('T') ? Math.Clamp(Snap(_top + delta.Y), 0, _top + _height - 1) : _top;
            var right = _handle.Contains('R') ? Math.Clamp(Snap(_left + _width + delta.X), _left + 1, Math.Max(_left + 1, _vm.CanvasWidth)) : _left + _width;
            var bottom = _handle.Contains('B') ? Math.Clamp(Snap(_top + _height + delta.Y), _top + 1, Math.Max(_top + 1, _vm.CanvasHeight)) : _top + _height;
            obj.Left = left; obj.Top = top; obj.Width = right - left; obj.Height = bottom - top;
        }
        e.Handled = true;
    }
    private void EndGesture() { _gesture = null; DesignCanvas.ReleaseMouseCapture(); }
    private void OnCanvasKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_vm.SelectedObject is not { CanMove: true } obj || !_vm.CanEdit) return;
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10d : 1d;
        switch (e.Key)
        {
            case Key.Left: obj.Left = Math.Max(0, obj.Left - step); break;
            case Key.Right: obj.Left = Math.Min(Math.Max(0, _vm.CanvasWidth - obj.Width), obj.Left + step); break;
            case Key.Up: obj.Top = Math.Max(0, obj.Top - step); break;
            case Key.Down: obj.Top = Math.Min(Math.Max(0, _vm.CanvasHeight - obj.Height), obj.Top + step); break;
            case Key.Delete: if (_vm.DeleteCommand.CanExecute(null)) _vm.DeleteCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }
    private void ShowPreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = 900 };
        var window = new Window { Title = "Xem trước bill · Bản đang sửa", Owner = this, Width = 980, Height = 720,
            MinWidth = 600, MinHeight = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = image, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        window.ShowDialog();
    }
}
