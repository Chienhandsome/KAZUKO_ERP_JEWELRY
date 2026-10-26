using System.Globalization;
using System.Xml.Linq;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Sales.ReceiptDesigner;

/// <summary>Edits only layout/text attributes; preserves bindings, tables and other FRX metadata.</summary>
public sealed class ReceiptDesignerObject(XElement element) : ObservableObject
{
    public XElement Element { get; } = element;
    public string Name => (string?)Element.Attribute("Name") ?? Element.Name.LocalName;
    public bool IsCell => Element.Name.LocalName == "TableCell";
    public bool IsText => Element.Name.LocalName is "TextObject" or "TableCell";
    public bool IsTable => Element.Name.LocalName == "TableObject";
    public bool CanMove => !IsCell;
    public string DisplayName => IsCell ? $"    Ô · {Name} · {Text}" : $"{Name} · {Element.Name.LocalName.Replace("Object", "")}";
    public string Hint => IsCell ? "Ô thuộc bảng: sửa chữ và định dạng tại đây; chọn bảng để kéo hoặc đổi kích thước cả khối."
        : "Kéo để di chuyển; kéo các điểm ở viền để đổi kích thước. X/Y tính từ góc trái vùng nội dung.";

    internal static double Read(XElement node, string name, double fallback = 0) =>
        double.TryParse((string?)node.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) ? value : fallback;
    private void Write(string name, double value) => Write(name, value.ToString("0.####", CultureInfo.InvariantCulture));
    private void Write(string name, string value)
    {
        if ((string?)Element.Attribute(name) == value) return;
        Element.SetAttributeValue(name, value);
        OnPropertyChanged(string.Empty);
    }
    public double Left { get => Read(Element, "Left"); set { if (CanMove && double.IsFinite(value)) Write("Left", Math.Max(0, value)); } }
    public double Top { get => Read(Element, "Top"); set { if (CanMove && double.IsFinite(value)) Write("Top", Math.Max(0, value)); } }
    public double Width
    {
        get => IsTable ? Element.Elements("TableColumn").Sum(x => Read(x, "Width", 75.6)) : Read(Element, "Width", 75.6);
        set
        {
            if (!CanMove || !double.IsFinite(value) || value < 1) return;
            if (IsTable) ScaleChildren("TableColumn", "Width", value / Math.Max(1, Width), 75.6);
            Write("Width", value);
        }
    }
    public double Height
    {
        get => IsTable ? Element.Elements("TableRow").Sum(x => Read(x, "Height", 18.9)) : Read(Element, "Height", 18.9);
        set
        {
            if (!CanMove || !double.IsFinite(value) || value < 1) return;
            if (IsTable) ScaleChildren("TableRow", "Height", value / Math.Max(1, Height), 18.9);
            Write("Height", value);
        }
    }
    private void ScaleChildren(string type, string attribute, double factor, double fallback)
    {
        foreach (var child in Element.Elements(type))
            child.SetAttributeValue(attribute, (Read(child, attribute, fallback) * factor).ToString("0.####", CultureInfo.InvariantCulture));
    }
    public double LeftMm { get => Left * 25.4 / 96; set => Left = value * 96 / 25.4; }
    public double TopMm { get => Top * 25.4 / 96; set => Top = value * 96 / 25.4; }
    public double WidthMm { get => Width * 25.4 / 96; set => Width = value * 96 / 25.4; }
    public double HeightMm { get => Height * 25.4 / 96; set => Height = value * 96 / 25.4; }
    public string Text { get => (string?)Element.Attribute("Text") ?? string.Empty; set { if (IsText) Write("Text", value); } }
    private System.Drawing.Font Font
    {
        get
        {
            try { return (System.Drawing.Font?)new System.Drawing.FontConverter().ConvertFromInvariantString((string?)Element.Attribute("Font") ?? "Arial, 9pt") ?? new("Arial", 9); }
            catch (ArgumentException) { return new("Arial", 9); }
        }
    }
    private void SetFont(string? name = null, float? size = null, bool? bold = null, bool? italic = null)
    {
        using var old = Font;
        var style = old.Style;
        if (bold is bool b) style = b ? style | System.Drawing.FontStyle.Bold : style & ~System.Drawing.FontStyle.Bold;
        if (italic is bool i) style = i ? style | System.Drawing.FontStyle.Italic : style & ~System.Drawing.FontStyle.Italic;
        using var font = new System.Drawing.Font(name ?? old.Name, size ?? old.Size, style, System.Drawing.GraphicsUnit.Point);
        Write("Font", new System.Drawing.FontConverter().ConvertToInvariantString(font) ?? "Arial, 9pt");
    }
    public string FontFamilyName { get { using var f = Font; return f.Name; } set { if (!string.IsNullOrWhiteSpace(value)) SetFont(name: value); } }
    public double FontSize { get { using var f = Font; return f.Size; } set { if (double.IsFinite(value) && value is >= 4 and <= 72) SetFont(size: (float)value); } }
    public bool Bold { get { using var f = Font; return f.Bold; } set => SetFont(bold: value); }
    public bool Italic { get { using var f = Font; return f.Italic; } set => SetFont(italic: value); }
    public string HorzAlign { get => (string?)Element.Attribute("HorzAlign") ?? "Left"; set { if (value is "Left" or "Center" or "Right" or "Justify") Write("HorzAlign", value); } }
    public string VertAlign { get => (string?)Element.Attribute("VertAlign") ?? "Top"; set { if (value is "Top" or "Center" or "Bottom") Write("VertAlign", value); } }
    public double Angle { get => Read(Element, "Angle"); set { if (double.IsFinite(value) && value is >= -360 and <= 360) Write("Angle", value); } }
}

public sealed record ReceiptDesignerSection(XElement Element, double Width, string DisplayName)
{
    public double Height => Math.Max(40, ReceiptDesignerObject.Read(Element, "Height", 100));
    public override string ToString() => DisplayName;
}
