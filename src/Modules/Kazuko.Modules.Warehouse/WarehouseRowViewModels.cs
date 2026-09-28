using System.Globalization;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public static class WarehouseTabs
{
    public const int Overview = 0;
    public const int RawMaterials = 1;
    public const int FinishedProducts = 2;
    public const int SalesGoods = 3;
    public const int Receipt = 4;
    public const int Issue = 5;
    public const int Transfer = 6;
    public const int Stocktake = 7;
}

public sealed class ChartSliceViewModel : ObservableObject
{
    public ChartSliceViewModel(string label, decimal value, string brushKey, string? legendText = null)
    {
        Label = label;
        Value = value;
        BrushKey = brushKey;
        LegendText = legendText ?? label;
    }

    public string Label { get; }
    public decimal Value { get; }
    public string BrushKey { get; }
    public string LegendText { get; }
}

public sealed class MaterialBalanceRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public MaterialBalanceRowViewModel(MaterialBalanceItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public MaterialBalanceItem Item { get; }
    public int Index { get; }
    public long MaterialId => Item.MaterialId;
    public string MaterialCode => Item.MaterialCode;
    public string MaterialName => Item.MaterialName;
    public string MaterialType => Item.MaterialType;
    public string MaterialTypeDisplay => Item.MaterialType switch
    {
        "GOLD" => "Vàng",
        "STONE" => "Đá",
        "ACCESSORY" => "Phụ kiện",
        _ => Item.MaterialType
    };
    public string PurityCode => Item.PurityCode;
    public string GoldTypeDisplay => string.IsNullOrWhiteSpace(PurityCode)
        ? MaterialTypeDisplay
        : PurityCode;
    public string UnitCode => Item.UnitCode;
    public long LocationId => Item.LocationId;
    public string LocationCode => Item.LocationCode;
    public string LocationName => Item.LocationName;
    public decimal Quantity => Item.Quantity;
    public decimal AverageCost => Item.AverageCost;
    public decimal StockValue => Item.StockValue;
    public decimal MinimumStock => Item.MinimumStock;
    public bool IsLowStock => Item.IsLowStock;

    public string QuantityText => Quantity.ToString("N4", Vi);
    public string AverageCostText => AverageCost.ToString("N0", Vi);
    public string StockValueText => StockValue.ToString("N0", Vi);
    public string MinimumStockText => MinimumStock.ToString("N4", Vi);
    public string LowStockText => IsLowStock ? "Thấp" : "OK";
}

public sealed class FinishedProductRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public FinishedProductRowViewModel(FinishedProductStockItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public FinishedProductStockItem Item { get; }
    public int Index { get; }
    public long Id => Item.Id;
    public string Code => Item.Code;
    public string ProductName => Item.ProductName;
    public string GoldPurity => Item.GoldPurity;
    public string UnitCode => Item.UnitCode;
    public decimal Quantity => Item.Quantity;
    public decimal StockValue => Item.StockValue;
    public string LocationName => Item.LocationName;
    public string StatusCode => Item.StatusCode;
    public string StatusDisplay => StatusCode switch
    {
        "IN_STOCK" => "Trong kho",
        "RESERVED" => "Đang giữ",
        "WAITING_RECEIPT" => "Chờ nhập",
        "DISPLAYED" => "Trưng bày",
        "SOLD" => "Đã bán",
        _ => StatusCode
    };

    public string QuantityText => Quantity.ToString("N0", Vi);
    public string StockValueText => StockValue.ToString("N0", Vi);
}

public sealed class SaleItemRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SaleItemRowViewModel(SaleItemStockItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public SaleItemStockItem Item { get; }
    public int Index { get; }
    public long Id => Item.Id;
    public string Code => Item.Code;
    public string ProductName => Item.ProductName;
    public string GoldPurity => Item.GoldPurity;
    public string UnitCode => Item.UnitCode;
    public decimal Quantity => Item.Quantity;
    public decimal StockValue => Item.StockValue;
    public string LocationName => Item.LocationName;
    public string StatusCode => Item.StatusCode;
    public string StatusDisplay => StatusCode switch
    {
        "DISPLAYED" => "Trưng bày",
        _ => StatusCode
    };

    public string QuantityText => Quantity.ToString("N0", Vi);
    public string StockValueText => StockValue.ToString("N0", Vi);
}

public sealed class StockDocumentRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public StockDocumentRowViewModel(StockDocumentSummary item, int index)
    {
        Item = item;
        Index = index;
    }

    public StockDocumentSummary Item { get; }
    public int Index { get; }
    public long Id => Item.Id;
    public string Code => Item.Code;
    public DateTime DocumentDate => Item.DocumentDate;
    public string DocumentType => Item.DocumentType;
    public string StatusCode => Item.StatusCode;
    public string SourceLocationName => Item.SourceLocationName;
    public string DestinationLocationName => Item.DestinationLocationName;
    public string ReferenceNo => Item.ReferenceNo;
    public string CounterpartyName => Item.CounterpartyName;
    public int LineCount => Item.LineCount;
    public decimal TotalQuantity => Item.TotalQuantity;
    public decimal TotalAmount => Item.TotalAmount;
    public string CreatedByName => Item.CreatedByName;
    public DateTime CreatedAt => Item.CreatedAt;
    public string PostedByName => Item.PostedByName;
    public DateTime? PostedAt => Item.PostedAt;

    public string DocumentDateText => DocumentDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string DocumentTypeDisplay => DocumentType switch
    {
        "OPENING" => "Đầu kỳ",
        "RECEIPT" => "Nhập",
        "ISSUE" => "Xuất",
        "TRANSFER" => "Chuyển",
        "OUTSOURCED_CRAFT_ISSUE" => "Xuất thợ",
        "CRAFT_RETURN_RECEIPT" => "Nhập chế tác",
        "FINISHED_GOODS_ISSUE" => "Xuất TP",
        "STOCK_ADJUSTMENT" => "Điều chỉnh",
        _ => DocumentType
    };
    public string StatusDisplay => StatusCode switch
    {
        "DRAFT" => "Nháp",
        "POSTED" => "Đã ghi sổ",
        "CANCELLED" => "Đã hủy",
        _ => StatusCode
    };
    public string WarehouseDisplay =>
        string.IsNullOrWhiteSpace(DestinationLocationName)
            ? (string.IsNullOrWhiteSpace(SourceLocationName) ? "—" : SourceLocationName)
            : DestinationLocationName;
    public string TotalQuantityText => TotalQuantity.ToString("N4", Vi);
    public string TotalAmountText => TotalAmount.ToString("N0", Vi);
    public string PostedAtText => PostedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—";
}

public sealed class StockDocumentLineRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public StockDocumentLineRowViewModel(StockDocumentLine item)
    {
        Item = item;
    }

    public StockDocumentLine Item { get; }
    public int LineNumber => Item.LineNumber;
    public string MaterialCode => Item.MaterialCode;
    public string MaterialName => Item.MaterialName;
    public string MaterialType => Item.MaterialType;
    public string UnitCode => Item.UnitCode;
    public decimal Quantity => Item.Quantity;
    public decimal UnitCost => Item.UnitCost;
    public decimal LineAmount => Item.LineAmount;
    public string LotNo => Item.LotNo;
    public string Notes => Item.Notes;

    public string QuantityText => Quantity.ToString("N4", Vi);
    public string UnitCostText => UnitCost.ToString("N0", Vi);
    public string LineAmountText => LineAmount.ToString("N0", Vi);
}

public sealed class StockMovementRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public StockMovementRowViewModel(StockMovementItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public StockMovementItem Item { get; }
    public int Index { get; }
    public long Id => Item.Id;
    public DateTime OccurredAt => Item.OccurredAt;
    public string DocumentCode => Item.DocumentCode;
    public string DocumentType => Item.DocumentType;
    public string MaterialCode => Item.MaterialCode;
    public string MaterialName => Item.MaterialName;
    public string LocationName => Item.LocationName;
    public string MovementType => Item.MovementType;
    public decimal QuantityChange => Item.QuantityChange;
    public decimal UnitCost => Item.UnitCost;
    public decimal ValueChange => Item.ValueChange;
    public string CreatedByName => Item.CreatedByName;

    public string OccurredAtText => OccurredAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    public string OccurredDateText => OccurredAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string DocumentTypeDisplay => DocumentType switch
    {
        "OPENING" => "Đầu kỳ",
        "RECEIPT" => "Nhập",
        "ISSUE" => "Xuất",
        "TRANSFER" => "Chuyển",
        "OUTSOURCED_CRAFT_ISSUE" => "Xuất thợ",
        "CRAFT_RETURN_RECEIPT" => "Nhập chế tác",
        "FINISHED_GOODS_ISSUE" => "Xuất TP",
        "STOCK_ADJUSTMENT" => "Điều chỉnh",
        _ => DocumentType
    };
    public string MovementTypeDisplay => MovementType switch
    {
        "IN" => "Nhập",
        "OUT" => "Xuất",
        "REVERSAL" => "Đảo",
        _ => MovementType
    };
    public string QuantityChangeText => QuantityChange.ToString("N4", Vi);
    public string UnitCostText => UnitCost.ToString("N0", Vi);
    public string ValueChangeText => ValueChange.ToString("N0", Vi);
}
