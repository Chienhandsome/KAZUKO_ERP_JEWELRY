using System.Windows;
using System.Windows.Controls;
using Kazuko.Core.Finance;

namespace Kazuko.Modules.Debt;

public partial class DebtView : UserControl
{
    internal DebtView(DebtViewModel viewModel, DebtTabKind tab)
    {
        InitializeComponent();
        DebtContent.ContentTemplate = (DataTemplate)Resources[tab switch
        {
            DebtTabKind.Customer => "CustomerDebtTemplate",
            DebtTabKind.Supplier => "SupplierDebtTemplate",
            DebtTabKind.WorkerGold => "WorkerGoldDebtTemplate",
            _ => throw new ArgumentOutOfRangeException(nameof(tab))
        }];
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DebtViewModel viewModel)
            await viewModel.SearchAsync();
    }
}
