using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;

namespace Kazuko.Modules.Finance;

/// <summary>
/// Implementation WPF của <see cref="IDebtSettlementDialogService"/> dùng
/// <see cref="CashVoucherDialog"/> ở chế độ chỉ thu thập input (không tự tạo voucher).
/// Module Công nợ dùng qua contract ở Kazuko.Core, không tham chiếu module Tài chính.
/// </summary>
public sealed class FinanceDebtSettlementDialogService : IDebtSettlementDialogService
{
    private readonly IFinanceVoucherService _voucherService;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly ICashFundRepository _fundRepository;
    private readonly IBankAccountRepository _bankRepository;

    public FinanceDebtSettlementDialogService(
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        ICashFundRepository fundRepository,
        IBankAccountRepository bankRepository)
    {
        _voucherService = voucherService;
        _sessionAccessor = sessionAccessor;
        _fundRepository = fundRepository;
        _bankRepository = bankRepository;
    }

    public DebtSettlementDialogResult? ShowDialog(DebtSettlementDialogRequest request)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return CashVoucherDialog.ShowForDebtSettlement(
            owner, _voucherService, _sessionAccessor, _fundRepository, _bankRepository, request);
    }
}
