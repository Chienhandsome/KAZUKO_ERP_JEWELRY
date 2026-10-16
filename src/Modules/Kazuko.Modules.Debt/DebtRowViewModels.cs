using System.Globalization;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Debt;

public sealed class CustomerDebtRowViewModel : ObservableObject
{
    public CustomerDebtRowViewModel(int index, CustomerDebtRow row)
    {
        Index = index;
        Row = row;
    }

    public int Index { get; }
    public CustomerDebtRow Row { get; }
    public long CustomerId => Row.CustomerId;
    public string Code => Row.Code;
    public string Name => Row.Name;
    public string Phone => Row.Phone;
    public string GroupName => Row.GroupName;
    public string Address => Row.Address;
    public decimal TotalIncurredVnd => Row.TotalIncurredVnd;
    public decimal PaidVnd => Row.PaidVnd;
    public decimal RemainingVnd => Row.RemainingVnd;
    public decimal ReceivableRemainingVnd => Row.ReceivableRemainingVnd;
    public decimal PayableRemainingVnd => Row.PayableRemainingVnd;
    public DateTime? LastDebtDate => Row.LastDebtDate;
    public string StatusCode => Row.StatusCode;

    /// <summary>Hiện nút "Thu toàn bộ" khi còn phải thu ở bất kỳ chiều nào.</summary>
    public bool CanCollectAll => ReceivableRemainingVnd > 0;

    /// <summary>Hiện nút "Chi toàn bộ" khi DN còn nợ KH.</summary>
    public bool CanPayAll => PayableRemainingVnd > 0;

    public string TotalIncurredText => Row.TotalIncurredVnd.ToString("N0", CultureInfo.CurrentCulture);
    public string PaidText => Row.PaidVnd.ToString("N0", CultureInfo.CurrentCulture);
    public string RemainingText => Row.RemainingVnd.ToString("N0", CultureInfo.CurrentCulture);
    public string LastDebtDateText => Row.LastDebtDate?.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) ?? "—";
    public string StatusText => StatusCode switch
    {
        DebtDisplayStatuses.Overdue => "Quá hạn",
        DebtDisplayStatuses.Paid => "Đã thanh toán",
        _ => "Bình thường"
    };
}

public sealed class SupplierDebtRowViewModel : ObservableObject
{
    public SupplierDebtRowViewModel(int index, SupplierDebtRow row)
    {
        Index = index;
        Row = row;
    }

    public int Index { get; }
    public SupplierDebtRow Row { get; }
    public long SupplierId => Row.SupplierId;
    public string Code => Row.Code;
    public string Name => Row.Name;
    public string Phone => Row.Phone;
    public string GroupName => Row.GroupName;
    public string Address => Row.Address;
    public decimal TotalIncurredVnd => Row.TotalIncurredVnd;
    public decimal PaidVnd => Row.PaidVnd;
    public decimal RemainingVnd => Row.RemainingVnd;
    public decimal ReceivableRemainingVnd => Row.ReceivableRemainingVnd;
    public decimal PayableRemainingVnd => Row.PayableRemainingVnd;
    public DateTime? LastDebtDate => Row.LastDebtDate;
    public string StatusCode => Row.StatusCode;

    /// <summary>Hiện nút "Thu toàn bộ" khi NCC còn nợ DN.</summary>
    public bool CanCollectAll => ReceivableRemainingVnd > 0;

    /// <summary>Hiện nút "Chi toàn bộ" khi DN còn nợ NCC.</summary>
    public bool CanPayAll => PayableRemainingVnd > 0;

    public string TotalIncurredText => Row.TotalIncurredVnd.ToString("N0", CultureInfo.CurrentCulture);
    public string PaidText => Row.PaidVnd.ToString("N0", CultureInfo.CurrentCulture);
    public string RemainingText => Row.RemainingVnd.ToString("N0", CultureInfo.CurrentCulture);
    public string LastDebtDateText => Row.LastDebtDate?.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) ?? "—";
    public string StatusText => StatusCode switch
    {
        DebtDisplayStatuses.Overdue => "Quá hạn",
        DebtDisplayStatuses.Paid => "Đã thanh toán",
        _ => "Bình thường"
    };
}

public sealed class DebtLedgerRowViewModel
{
    public DebtLedgerRowViewModel(DebtLedgerEntry entry)
    {
        Entry = entry;
    }

    public DebtLedgerEntry Entry { get; }

    /// <summary>Hiện nút Thu: dòng còn dư có thể tất toán, hướng phải thu.</summary>
    public bool CanShowCollectButton =>
        Entry.CanSettle
        && string.Equals(Entry.Direction, DebtLedgerDirections.Receive, StringComparison.OrdinalIgnoreCase)
        && Entry.RemainingAmountVnd > 0;

    /// <summary>Hiện nút Chi: dòng còn dư có thể tất toán, hướng phải trả.</summary>
    public bool CanShowPayButton =>
        Entry.CanSettle
        && string.Equals(Entry.Direction, DebtLedgerDirections.Pay, StringComparison.OrdinalIgnoreCase)
        && Entry.RemainingAmountVnd > 0;
    public string EntryDateText => Entry.EntryDate.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
    public string DocumentCode => Entry.DocumentCode;
    public string Description => Entry.Description;
    public string ReceiptText => Entry.ReceiptAmountVnd > 0
        ? Entry.ReceiptAmountVnd.ToString("N0", CultureInfo.CurrentCulture)
        : string.Empty;
    public string PaymentText => Entry.PaymentAmountVnd > 0
        ? Entry.PaymentAmountVnd.ToString("N0", CultureInfo.CurrentCulture)
        : string.Empty;
    public string BalanceText => Entry.BalanceAfterVnd.ToString("N0", CultureInfo.CurrentCulture);
}

public sealed class StatusFilterOption(string code, string displayName)
{
    public string Code { get; } = code;
    public string DisplayName { get; } = displayName;
    public override string ToString() => DisplayName;
}

public sealed class GroupFilterOption(long id, string name)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

public sealed class WorkerGoldDebtRowViewModel : ObservableObject
{
    public WorkerGoldDebtRowViewModel(int index, WorkerGoldDebtListItem row)
    {
        Index = index;
        Row = row;
    }

    public int Index { get; }
    public WorkerGoldDebtListItem Row { get; }
    public long DebtId => Row.Id;
    public string Code => Row.Code;
    public string WorkerDisplayName => Row.WorkerDisplayName;
    public decimal WeightChi => Row.WeightChi;
    public decimal WeightPaidChi => Row.WeightPaidChi;
    public decimal RemainingChi => Row.RemainingChi;
    public string ProductionOrderCode => Row.ProductionOrderCode;
    public string StatusCode => Row.StatusCode;

    public string WorkerTypeText => string.Equals(
        Row.WorkerType, WorkerGoldDebtWorkerTypes.Employee, StringComparison.OrdinalIgnoreCase)
        ? "Nội bộ"
        : "Ngoài";

    public string GoldPurityText => string.IsNullOrWhiteSpace(Row.GoldPurityCode) ? "—" : Row.GoldPurityCode;

    /// <summary>Hiện nút "Ghi trả vàng" khi thợ còn nợ.</summary>
    public bool CanRecordRepayment => RemainingChi > 0;

    public string WeightText => Row.WeightChi.ToString("N3", CultureInfo.CurrentCulture);
    public string WeightPaidText => Row.WeightPaidChi.ToString("N3", CultureInfo.CurrentCulture);
    public string RemainingText => Row.RemainingChi.ToString("N3", CultureInfo.CurrentCulture);
    public string CreatedAtText => Row.CreatedAt.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
    public string StatusText => StatusCode switch
    {
        WorkerGoldDebtStatuses.Paid => "Đã trả hết",
        WorkerGoldDebtStatuses.Partial => "Trả một phần",
        WorkerGoldDebtStatuses.Cancelled => "Đã hủy",
        WorkerGoldDebtStatuses.Open => "Còn nợ",
        _ => StatusCode
    };

    /// <summary>Mã trạng thái dùng cho DebtStatusToBrushConverter (PAID xanh / còn lại cam).</summary>
    public string StatusBrushCode => StatusCode switch
    {
        WorkerGoldDebtStatuses.Paid => DebtDisplayStatuses.Paid,
        _ => DebtDisplayStatuses.Normal
    };
}

public static class WorkerDebtStatusFilterCodes
{
    public const string Open = "OPEN_ONLY";
    public const string Paid = "PAID_ONLY";
    public const string All = "ALL";
}

public sealed class WorkerDebtStatusFilterOption(string code, string displayName)
{
    public string Code { get; } = code;
    public string DisplayName { get; } = displayName;
    public override string ToString() => DisplayName;
}

public sealed class GoldPurityFilterOption(string? code, string displayName)
{
    public string? Code { get; } = code;
    public string DisplayName { get; } = displayName;
    public override string ToString() => DisplayName;
}
