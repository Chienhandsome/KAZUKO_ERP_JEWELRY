using System.Globalization;
using Kazuko.Core.Warehouse;
using System.Windows;

namespace Kazuko.Modules.Debt;

/// <summary>Kết quả dialog ghi nhận trả vàng: null khi người dùng bấm Hủy.</summary>
public sealed record WorkerGoldRepaymentResult(decimal WeightChi, DateTime PaidDate, string? Notes);

/// <summary>
/// Dialog ghi nhận 1 lần thợ trả vàng (code-behind thuần, không ViewModel):
/// chỉ thu thập Số chỉ trả / Ngày trả / Ghi chú, validate rồi trả về.
/// Không chứa SQL — gọi repository do DebtViewModel thực hiện.
/// </summary>
public partial class WorkerGoldRepaymentDialog : Window
{
    private readonly decimal _remainingChi;

    public WorkerGoldRepaymentResult? Result { get; private set; }

    public WorkerGoldRepaymentDialog(WorkerGoldDebtRowViewModel debt)
    {
        ArgumentNullException.ThrowIfNull(debt);
        _remainingChi = debt.RemainingChi;

        InitializeComponent();

        WorkerNameBox.Text = debt.WorkerDisplayName;
        PurityBox.Text = debt.GoldPurityText;
        RemainingBox.Text = debt.RemainingText;
        AmountBox.Text = WeightDecimalInput.Format(debt.RemainingChi);
        PaidDatePicker.SelectedDate = DateTime.Today;

        AmountBox.Focus();
        AmountBox.SelectAll();
    }

    public static WorkerGoldRepaymentResult? Show(Window? owner, WorkerGoldDebtRowViewModel debt)
    {
        var dialog = new WorkerGoldRepaymentDialog(debt) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseAmount(AmountBox.Text, out var amount))
        {
            ShowError("Nhập số chỉ hợp lệ: 0.023 hoặc 0,023; tối đa 4 số lẻ, không ngăn cách hàng nghìn.");
            return;
        }

        if (amount <= 0)
        {
            ShowError("Số chỉ trả phải lớn hơn 0.");
            return;
        }

        if (amount > _remainingChi)
        {
            ShowError($"Số chỉ trả ({amount:N3}) vượt số còn nợ ({_remainingChi:N3}).");
            return;
        }

        if (PaidDatePicker.SelectedDate is not { } paidDate)
        {
            ShowError("Chọn ngày trả vàng.");
            return;
        }

        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        Result = new WorkerGoldRepaymentResult(amount, paidDate.Date, notes);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    internal static bool TryParseAmount(string? text,out decimal amount)=>WeightDecimalInput.TryParse(text,out amount);
}
