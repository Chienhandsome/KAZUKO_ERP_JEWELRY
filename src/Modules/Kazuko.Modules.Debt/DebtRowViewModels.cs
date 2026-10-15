using System.Globalization;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

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
