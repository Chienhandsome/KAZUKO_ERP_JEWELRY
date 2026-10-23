using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO;
using System.Windows.Media.Imaging;
using FastReport;
using FastReport.Barcode;
using Kazuko.Modules.Warehouse;
using Kazuko.Modules.Warehouse.Printing;
using Border = System.Windows.Controls.Border;

namespace Kazuko.Modules.Warehouse.LabelDesigner;

/// <summary>
/// Code-behind cho designer: vẽ object lên canvas, kéo di chuyển, kéo góc resize.
/// </summary>
public partial class LabelDesignerDialog : Window
{
    private const double HandleSize = 10.0;

    private readonly Dictionary<LabelDesignerObject, Border> _visuals = new();
    private readonly List<Rectangle> _handles = new();

    private LabelDesignerViewModel? ViewModel => DataContext as LabelDesignerViewModel;

    private LabelDesignerObject? _dragObject;
    private ResizeHandleKind _resizeHandle;
    private Point _dragStartScreen;
    private double _dragStartLeft;
    private double _dragStartTop;
    private double _dragStartWidth;
    private double _dragStartHeight;
    private bool _isDragging;
    private bool _isResizing;

    public LabelDesignerDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // Danh sách font hệ thống cho ComboBox.
        FontFamilyCombo.ItemsSource = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(n => n)
            .ToList();
    }

    public LabelDesignerDialog(LabelDesignerViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LabelDesignerViewModel.DialogResult) &&
                viewModel.DialogResult is not null)
                DialogResult = viewModel.DialogResult;
        };
    }

    private double Scale => LabelDesignerViewModel.CanvasScale;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
            return;
        await vm.InitializeAsync();
        ApplyDesignGrid();
        BuildCanvas();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LabelDesignerViewModel.SelectedObject))
                RefreshSelection();
            if (args.PropertyName == nameof(LabelDesignerViewModel.Report))
                BuildCanvas();
        };
    }

    #region Dựng visual

    private void ApplyDesignGrid()
    {
        var vm = ViewModel;
        if (vm is null)
            return;
        DesignCanvas.Background = LabelDesignerGrid.CreatePaperBrush(
            vm.CanvasWidth,
            vm.CanvasHeight,
            Scale,
            widthMm: (int)FinishedGoodsLabelPrintService.PaperWidthMm,
            heightMm: (int)FinishedGoodsLabelPrintService.PaperHeightMm);
    }

    private void BuildCanvas()
    {
        var vm = ViewModel;
        if (vm is null)
            return;

        DesignCanvas.Children.Clear();
        _visuals.Clear();
        _handles.Clear();

        foreach (var obj in vm.Objects)
        {
            var border = new Border
            {
                Background = Brushes.Transparent, // để bắt hit-test
                BorderThickness = new Thickness(1),
                Tag = obj,
            };
            border.Child = obj.IsText ? CreateTextVisual(obj) : CreateBarcodeVisual(obj);
            border.MouseLeftButtonDown += OnObjectMouseDown;

            PositionVisual(obj, border);
            DesignCanvas.Children.Add(border);
            _visuals[obj] = border;

            // Theo dõi thay đổi thuộc tính để vẽ lại.
            obj.PropertyChanged += (_, args) =>
            {
                RefreshVisual(obj);
                if (args.PropertyName == nameof(LabelDesignerObject.BarcodeKind))
                    RevealVisual(border);
            };
        }

        RefreshSelection();
    }

    private FrameworkElement CreateTextVisual(LabelDesignerObject obj)
    {
        var src = (TextObject)obj.Source;
        var tb = new TextBlock
        {
            Text = obj.Text,
            FontFamily = new FontFamily(obj.FontFamilyName),
            FontSize = obj.FontSize * 96.0 / 72.0 * Scale, // pt → px màn hình
            FontWeight = obj.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = obj.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = new SolidColorBrush(ToMediaColor(src.TextColor)),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
        };
        tb.SetValue(TextBlock.TextAlignmentProperty, ToTextAlignment(obj.HorzAlign));
        // Căn dọc: dùng VerticalAlignment trong Border.
        tb.VerticalAlignment = obj.VertAlign switch
        {
            VertAlign.Center => VerticalAlignment.Center,
            VertAlign.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Top,
        };
        if (obj.Angle != 0)
        {
            tb.RenderTransformOrigin = new Point(0.5, 0.5);
            tb.RenderTransform = new RotateTransform(obj.Angle);
        }
        tb.IsHitTestVisible = false;
        return tb;
    }

    private FrameworkElement CreateBarcodeVisual(LabelDesignerObject obj)
    {
        if (((BarcodeObject)obj.Source).Barcode is BarcodeQR)
        {
            using var sample = new BarcodeObject();
            sample.Assign(obj.Source);
            sample.Left = sample.Top = 0;
            sample.Text = "B600022";
            sample.DataColumn = sample.Expression = string.Empty;
            using var bitmap = new System.Drawing.Bitmap(
                Math.Max(1, (int)Math.Ceiling(obj.Width * Scale)), Math.Max(1, (int)Math.Ceiling(obj.Height * Scale)));
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.White);
                graphics.ScaleTransform((float)Scale, (float)Scale);
                VectorLabelRenderer.DrawNativeBarcode(graphics, sample);
            }
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.StreamSource = stream;
            source.EndInit();
            source.Freeze();
            return new Image { Source = source, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        }
        var panel = new StackPanel { IsHitTestVisible = false };

        // Vẽ pattern mã vạch mẫu bằng Code128Encoder.
        var bars = new Canvas
        {
            Width = obj.Width * Scale,
            Height = Math.Max(10, obj.Height * Scale - 16),
            Background = Brushes.White,
            ClipToBounds = true,
        };
        try
        {
            int[] widths = Code128Encoder.Encode("B600022");
            int total = Code128Encoder.QuietZoneModules * 2 + widths.Sum();
            double moduleW = bars.Width / total;
            double cx = Code128Encoder.QuietZoneModules * moduleW;
            for (int i = 0; i < widths.Length; i++)
            {
                double bw = widths[i] * moduleW;
                if ((i & 1) == 0)
                {
                    bars.Children.Add(new Rectangle
                    {
                        Fill = Brushes.Black,
                        Width = Math.Max(1, bw),
                        Height = bars.Height,
                    });
                    Canvas.SetLeft(bars.Children[^1], cx);
                }
                cx += bw;
            }
        }
        catch
        {
            // Nếu encode lỗi, để nền trắng.
        }
        panel.Children.Add(bars);

        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(obj.DataColumn) ? "[?]" : $"[{obj.DataColumn}]",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.Gray,
        });
        return panel;
    }

    private static System.Windows.Media.Color ToMediaColor(System.Drawing.Color c) =>
        System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);

    private static TextAlignment ToTextAlignment(HorzAlign align) => align switch
    {
        HorzAlign.Center => TextAlignment.Center,
        HorzAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    private void PositionVisual(LabelDesignerObject obj, Border border)
    {
        double left = obj.Left * Scale;
        double top = obj.Top * Scale;
        double width = Math.Max(8, obj.Width * Scale);
        double height = Math.Max(8, obj.Height * Scale);
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        border.Width = width;
        border.Height = height;
    }

    private void RefreshVisual(LabelDesignerObject obj)
    {
        if (!_visuals.TryGetValue(obj, out var border))
            return;
        // Dựng lại nội dung (text/font/căn lề có thể đổi).
        border.Child = obj.IsText ? CreateTextVisual(obj) : CreateBarcodeVisual(obj);
        PositionVisual(obj, border);
        if (ViewModel?.SelectedObject == obj)
            ShowHandles(border);
    }

    #endregion

    #region Chọn + handles

    private void RefreshSelection()
    {
        var vm = ViewModel;
        var accent = (TryFindResource("PrimaryBrush") as Brush) ?? Brushes.Orange;
        foreach (var (obj, border) in _visuals)
            border.BorderBrush = obj == vm?.SelectedObject ? accent : Brushes.Transparent;

        ClearHandles();

        if (vm?.SelectedObject is not null &&
            _visuals.TryGetValue(vm.SelectedObject, out var selected))
        {
            ShowHandles(selected);
            RevealVisual(selected);
        }
    }

    private void ClearHandles()
    {
        foreach (var h in _handles)
            DesignCanvas.Children.Remove(h);
        _handles.Clear();
    }

    private void RevealVisual(Border border)
    {
        // Position/size changes are arranged before asking the scroll viewer to reveal them.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (_isDragging || _isResizing) return;
            border.BringIntoView(new Rect(-HandleSize, -HandleSize,
                border.Width + HandleSize * 2, border.Height + HandleSize * 2));
        }));
    }

    private void ShowHandles(Border border)
    {
        ClearHandles();
        double left = Canvas.GetLeft(border);
        double top = Canvas.GetTop(border);
        double w = border.Width;
        double h = border.Height;
        var accent = (TryFindResource("PrimaryBrush") as Brush) ?? Brushes.Orange;

        var positions = new (double x, double y, ResizeHandleKind kind)[]
        {
            (left - HandleSize / 2, top - HandleSize / 2, ResizeHandleKind.TopLeft),
            (left + w / 2 - HandleSize / 2, top - HandleSize / 2, ResizeHandleKind.Top),
            (left + w - HandleSize / 2, top - HandleSize / 2, ResizeHandleKind.TopRight),
            (left + w - HandleSize / 2, top + h / 2 - HandleSize / 2, ResizeHandleKind.Right),
            (left + w - HandleSize / 2, top + h - HandleSize / 2, ResizeHandleKind.BottomRight),
            (left + w / 2 - HandleSize / 2, top + h - HandleSize / 2, ResizeHandleKind.Bottom),
            (left - HandleSize / 2, top + h - HandleSize / 2, ResizeHandleKind.BottomLeft),
            (left - HandleSize / 2, top + h / 2 - HandleSize / 2, ResizeHandleKind.Left),
        };

        foreach (var (x, y, kind) in positions)
        {
            var rect = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = accent,
                StrokeThickness = 1.5,
                Tag = kind,
                Cursor = GetHandleCursor(kind),
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            Canvas.SetZIndex(rect, 100);
            rect.MouseLeftButtonDown += OnHandleMouseDown;
            DesignCanvas.Children.Add(rect);
            _handles.Add(rect);
        }
    }

    private static Cursor GetHandleCursor(ResizeHandleKind kind) => kind switch
    {
        ResizeHandleKind.TopLeft or ResizeHandleKind.BottomRight => Cursors.SizeNWSE,
        ResizeHandleKind.TopRight or ResizeHandleKind.BottomLeft => Cursors.SizeNESW,
        ResizeHandleKind.Top or ResizeHandleKind.Bottom => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    #endregion

    #region Kéo di chuyển / resize

    private void OnObjectMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not LabelDesignerObject obj)
            return;
        var vm = ViewModel;
        if (vm is null)
            return;

        vm.SelectedObject = obj;
        _dragObject = obj;
        _dragStartScreen = e.GetPosition(DesignCanvas);
        _dragStartLeft = Canvas.GetLeft(border);
        _dragStartTop = Canvas.GetTop(border);
        _isDragging = true;
        DesignCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not ResizeHandleKind kind)
            return;
        var vm = ViewModel;
        if (vm?.SelectedObject is null ||
            !_visuals.TryGetValue(vm.SelectedObject, out var border))
            return;

        _dragObject = vm.SelectedObject;
        _resizeHandle = kind;
        _dragStartScreen = e.GetPosition(DesignCanvas);
        _dragStartLeft = Canvas.GetLeft(border);
        _dragStartTop = Canvas.GetTop(border);
        _dragStartWidth = border.Width;
        _dragStartHeight = border.Height;
        _isResizing = true;
        DesignCanvas.CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragObject is null || (!_isDragging && !_isResizing))
            return;

        var pos = e.GetPosition(DesignCanvas);
        double dx = pos.X - _dragStartScreen.X;
        double dy = pos.Y - _dragStartScreen.Y;

        if (_isDragging)
        {
            // Đổi px màn hình → px FastReport rồi gán vào object.
            var left = SnapScreen(Math.Max(0, _dragStartLeft + dx));
            var top = SnapScreen(Math.Max(0, _dragStartTop + dy));
            _dragObject.Left = (float)(left / Scale);
            _dragObject.Top = (float)(top / Scale);
        }
        else if (_isResizing)
        {
            var left = _dragStartLeft;
            var top = _dragStartTop;
            var right = _dragStartLeft + _dragStartWidth;
            var bottom = _dragStartTop + _dragStartHeight;

            switch (_resizeHandle)
            {
                case ResizeHandleKind.TopLeft:
                    left = SnapScreen(_dragStartLeft + dx);
                    top = SnapScreen(_dragStartTop + dy);
                    break;
                case ResizeHandleKind.Top:
                    top = SnapScreen(_dragStartTop + dy);
                    break;
                case ResizeHandleKind.TopRight:
                    top = SnapScreen(_dragStartTop + dy);
                    right = SnapScreen(_dragStartLeft + _dragStartWidth + dx);
                    break;
                case ResizeHandleKind.Right:
                    right = SnapScreen(_dragStartLeft + _dragStartWidth + dx);
                    break;
                case ResizeHandleKind.BottomRight:
                    right = SnapScreen(_dragStartLeft + _dragStartWidth + dx);
                    bottom = SnapScreen(_dragStartTop + _dragStartHeight + dy);
                    break;
                case ResizeHandleKind.Bottom:
                    bottom = SnapScreen(_dragStartTop + _dragStartHeight + dy);
                    break;
                case ResizeHandleKind.BottomLeft:
                    left = SnapScreen(_dragStartLeft + dx);
                    bottom = SnapScreen(_dragStartTop + _dragStartHeight + dy);
                    break;
                case ResizeHandleKind.Left:
                    left = SnapScreen(_dragStartLeft + dx);
                    break;
            }

            var newWidth = right - left;
            var newHeight = bottom - top;
            if (newWidth >= 8 && newHeight >= 8)
            {
                _dragObject.Left = (float)Math.Max(0, left / Scale);
                _dragObject.Top = (float)Math.Max(0, top / Scale);
                _dragObject.Width = (float)(newWidth / Scale);
                _dragObject.Height = (float)(newHeight / Scale);
            }
        }
    }

    private double SnapScreen(double screenPx) =>
        LabelDesignerGrid.SnapScreen(screenPx, Scale, SnapToGridCheck.IsChecked == true);

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _isDragging = false;
        _isResizing = false;
        _dragObject = null;
        if (DesignCanvas.IsMouseCaptured)
            DesignCanvas.ReleaseMouseCapture();
    }

    private enum ResizeHandleKind
    {
        TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left,
    }

    #endregion
}
