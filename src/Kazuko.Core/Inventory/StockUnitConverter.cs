namespace Kazuko.Core.Inventory;

/// <summary>Standard stock units and gold conversion (1 CHI = 3.75 g, 1 LUONG = 10 CHI).</summary>
public static class StockUnitCodes
{
    public const string Chi = "CHI";
    public const string Gram = "GRAM";
    public const string Luong = "LUONG";
    public const string Vien = "VIEN";
    public const string Carat = "CARAT";
    public const string Cai = "CAI";

    public const decimal GramsPerChi = 3.75m;
}

public static class MaterialTypes
{
    public const string Gold = "GOLD";
    public const string Stone = "STONE";
    public const string Accessory = "ACCESSORY";

    public static bool IsRawMaterial(string? materialType) =>
        materialType is Gold or Stone or Accessory;
}

public static class StockUnitConverter
{
    public static decimal ToStandardQuantity(decimal quantity, string fromUnitCode, string materialType)
    {
        var unit = Normalize(fromUnitCode);
        if (string.Equals(materialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase))
        {
            return unit switch
            {
                StockUnitCodes.Chi => quantity,
                StockUnitCodes.Gram => quantity / StockUnitCodes.GramsPerChi,
                StockUnitCodes.Luong => quantity * 10m,
                _ => throw new InvalidOperationException($"Không thể quy đổi đơn vị vàng '{fromUnitCode}' về CHI.")
            };
        }

        return quantity;
    }

    public static decimal ToStandardUnitPrice(decimal unitPrice, string fromUnitCode, string materialType)
    {
        var unit = Normalize(fromUnitCode);
        if (string.Equals(materialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase))
        {
            return unit switch
            {
                StockUnitCodes.Chi => unitPrice,
                StockUnitCodes.Gram => unitPrice * StockUnitCodes.GramsPerChi,
                StockUnitCodes.Luong => unitPrice / 10m,
                _ => throw new InvalidOperationException($"Không thể quy đổi đơn giá vàng '{fromUnitCode}' về /chỉ.")
            };
        }

        return unitPrice;
    }

    public static string StandardUnitFor(string materialType, string? existingUnit = null)
    {
        if (string.Equals(materialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase))
            return StockUnitCodes.Chi;

        var normalized = Normalize(existingUnit);
        if (string.Equals(materialType, MaterialTypes.Stone, StringComparison.OrdinalIgnoreCase))
            return normalized is StockUnitCodes.Carat ? StockUnitCodes.Carat : StockUnitCodes.Vien;

        if (string.Equals(materialType, MaterialTypes.Accessory, StringComparison.OrdinalIgnoreCase))
            return normalized is StockUnitCodes.Gram ? StockUnitCodes.Gram : StockUnitCodes.Cai;

        return normalized.Length > 0 ? normalized : StockUnitCodes.Cai;
    }

    public static string Normalize(string? unitCode)
    {
        var raw = (unitCode ?? string.Empty).Trim();
        if (raw.Length == 0)
            return string.Empty;

        var upper = raw.ToUpperInvariant();
        if (upper is "G" or "GRAM" or "GR" or "GRAMS")
            return StockUnitCodes.Gram;
        if (upper is "CHI" or "CH" || raw.Equals("chỉ", StringComparison.OrdinalIgnoreCase))
            return StockUnitCodes.Chi;
        if (upper.StartsWith("LUONG", StringComparison.Ordinal) || upper.StartsWith("CAY", StringComparison.Ordinal))
            return StockUnitCodes.Luong;
        if (upper is "VIEN" or "PCS" or "PC" || raw.Equals("viên", StringComparison.OrdinalIgnoreCase))
            return StockUnitCodes.Vien;
        if (upper.StartsWith("CARAT", StringComparison.Ordinal) || upper is "CT" or "KT")
            return StockUnitCodes.Carat;
        if (upper is "CAI" or "BO" or "SET" || raw.Equals("cái", StringComparison.OrdinalIgnoreCase))
            return StockUnitCodes.Cai;
        return upper;
    }
}
