using System.ComponentModel;
using FastReport;
using FastReport.Barcode;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Warehouse.LabelDesigner;

/// <summary>
/// Wrapper chỉnh sửa cho một TextObject hoặc BarcodeObject trong mẫu tem.
/// Mọi setter ghi thẳng vào object FastReport gốc và báo PropertyChanged
/// để canvas + panel thuộc tính cập nhật theo.
/// </summary>
public sealed class LabelDesignerObject : ObservableObject
{
    private readonly ReportComponentBase _source;
    private readonly TextObject? _text;
    private readonly BarcodeObject? _barcode;

    public LabelDesignerObject(ReportComponentBase source)
    {
        _source = source;
        _text = source as TextObject;
        _barcode = source as BarcodeObject;
    }

    /// <summary>Object FastReport gốc (để code-behind truy xuất khi cần).</summary>
    public ReportComponentBase Source => _source;

    public bool IsText => _text is not null;

    public bool IsBarcode => _barcode is not null;

    public string KindName => IsText ? "Chữ" : "Mã vạch";

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(_source.Name) ? _source.Name : KindName;

    // ---- Vị trí / kích thước (đơn vị px 96dpi của FastReport) ----

    public float Left
    {
        get => _source.Left;
        set
        {
            if (_source.Left != value)
            {
                _source.Left = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LeftMm));
            }
        }
    }

    public float Top
    {
        get => _source.Top;
        set
        {
            if (_source.Top != value)
            {
                _source.Top = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TopMm));
            }
        }
    }

    public float Width
    {
        get => _source.Width;
        set
        {
            if (value > 0 && _source.Width != value)
            {
                _source.Width = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WidthMm));
            }
        }
    }

    public float Height
    {
        get => _source.Height;
        set
        {
            if (value > 0 && _source.Height != value)
            {
                _source.Height = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeightMm));
            }
        }
    }

    // ---- Hiển thị mm cho panel thuộc tính (1px 96dpi = 25.4/96 mm) ----

    private const float MmPerPx = 25.4f / 96f;

    public double LeftMm
    {
        get => Left * MmPerPx;
        set => Left = (float)(value / MmPerPx);
    }

    public double TopMm
    {
        get => Top * MmPerPx;
        set => Top = (float)(value / MmPerPx);
    }

    public double WidthMm
    {
        get => Width * MmPerPx;
        set => Width = (float)(value / MmPerPx);
    }

    public double HeightMm
    {
        get => Height * MmPerPx;
        set => Height = (float)(value / MmPerPx);
    }

    // ---- Thuộc tính TextObject ----

    public string Text
    {
        get => _text?.Text ?? string.Empty;
        set
        {
            if (_text is not null && _text.Text != value)
            {
                _text.Text = value;
                OnPropertyChanged();
            }
        }
    }

    public string FontFamilyName
    {
        get => _text?.Font.Name ?? "Arial";
        set
        {
            if (_text is not null && _text.Font.Name != value)
            {
                var old = _text.Font;
                _text.Font = new System.Drawing.Font(value, old.Size, old.Style, System.Drawing.GraphicsUnit.Point);
                OnPropertyChanged();
            }
        }
    }

    public float FontSize
    {
        get => _text?.Font.Size ?? 8f;
        set
        {
            if (_text is not null && value >= 4 && value <= 72 && _text.Font.Size != value)
            {
                var old = _text.Font;
                _text.Font = new System.Drawing.Font(old.Name, value, old.Style, System.Drawing.GraphicsUnit.Point);
                OnPropertyChanged();
            }
        }
    }

    public bool Bold
    {
        get => _text?.Font.Bold ?? false;
        set
        {
            if (_text is not null)
                SetFontStyle(System.Drawing.FontStyle.Bold, value);
        }
    }

    public bool Italic
    {
        get => _text?.Font.Italic ?? false;
        set
        {
            if (_text is not null)
                SetFontStyle(System.Drawing.FontStyle.Italic, value);
        }
    }

    private void SetFontStyle(System.Drawing.FontStyle flag, bool on)
    {
        if (_text is null)
            return;
        var old = _text.Font;
        var style = on ? old.Style | flag : old.Style & ~flag;
        if (style != old.Style)
        {
            _text.Font = new System.Drawing.Font(old.Name, old.Size, style, System.Drawing.GraphicsUnit.Point);
            OnPropertyChanged(nameof(Bold));
            OnPropertyChanged(nameof(Italic));
        }
    }

    public HorzAlign HorzAlign
    {
        get => _text?.HorzAlign ?? HorzAlign.Left;
        set
        {
            if (_text is not null && _text.HorzAlign != value)
            {
                _text.HorzAlign = value;
                OnPropertyChanged();
            }
        }
    }

    public VertAlign VertAlign
    {
        get => _text?.VertAlign ?? VertAlign.Top;
        set
        {
            if (_text is not null && _text.VertAlign != value)
            {
                _text.VertAlign = value;
                OnPropertyChanged();
            }
        }
    }

    public int Angle
    {
        get => _text?.Angle ?? 0;
        set
        {
            if (_text is not null && _text.Angle != value)
            {
                _text.Angle = value;
                OnPropertyChanged();
            }
        }
    }

    // ---- Thuộc tính BarcodeObject ----

    public string DataColumn
    {
        get => _barcode?.DataColumn ?? string.Empty;
        set
        {
            if (_barcode is not null && _barcode.DataColumn != value)
            {
                _barcode.DataColumn = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Đọc lại toàn bộ từ object gốc (sau khi load hoặc undo).</summary>
    public void RefreshAll()
    {
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(LeftMm));
        OnPropertyChanged(nameof(TopMm));
        OnPropertyChanged(nameof(WidthMm));
        OnPropertyChanged(nameof(HeightMm));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(FontFamilyName));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(Bold));
        OnPropertyChanged(nameof(Italic));
        OnPropertyChanged(nameof(HorzAlign));
        OnPropertyChanged(nameof(VertAlign));
        OnPropertyChanged(nameof(Angle));
        OnPropertyChanged(nameof(DataColumn));
    }
}
