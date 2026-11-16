using System.Windows.Controls;
using Kazuko.Core.Finance;
using Kazuko.Core.UserManagement;
using Kazuko.Shared.UI.Behaviors;

namespace Kazuko.Modules.Finance;

public partial class FinanceView : UserControl
{
    internal FinanceView(IDebtTabFactory debtTabs, PermissionSnapshot permissions, bool openDebt)
    {
        InitializeComponent();

        CustomerDebtTab.Content = debtTabs.CreateView(DebtTabKind.Customer);
        SupplierDebtTab.Content = debtTabs.CreateView(DebtTabKind.Supplier);
        WorkerGoldDebtTab.Content = debtTabs.CreateView(DebtTabKind.WorkerGold);

        var tabs = FinanceTabs.Items.OfType<TabItem>();
        if (openDebt)
            tabs = new[] { CustomerDebtTab, SupplierDebtTab, WorkerGoldDebtTab }.Concat(tabs);

        FinanceTabs.SelectedItem = tabs.FirstOrDefault(tab =>
            PermissionAccess.GetRule(tab) is string rule && permissions.Allows(rule));
    }
}
