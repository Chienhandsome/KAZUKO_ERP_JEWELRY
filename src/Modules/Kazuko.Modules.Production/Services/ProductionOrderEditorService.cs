using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Kazuko.Modules.Production.Dialogs;

namespace Kazuko.Modules.Production.Services;

public sealed class ProductionOrderEditorService : IProductionOrderEditorService
{
    private readonly IServiceProvider _services;

    public ProductionOrderEditorService(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ProductionOrderEditResult? ShowCreate(string? preferredOrderSource = null)
    {
        var sessionAccessor = Require<ICurrentSessionAccessor>();
        var session = sessionAccessor.Current
            ?? throw new InvalidOperationException("Chưa đăng nhập.");
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập thiếu chi nhánh.");

        if (string.Equals(preferredOrderSource, ProductionOrderSources.SalesOrder, StringComparison.OrdinalIgnoreCase))
        {
            var craftViewModel = new CraftOrderDialogViewModel(
                Require<ICraftOrderApplicationService>(), sessionAccessor,
                Require<ICashFundRepository>(), Require<IBankAccountRepository>(),
                Require<IGoldTypeCatalogService>(), Require<ICustomerEditorService>());
            var craftDialog = new CraftOrderDialog
            {
                Owner = ResolveOwner(),
                DataContext = craftViewModel,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var created = craftDialog.ShowDialog() == true;
            return new ProductionOrderEditResult(null, craftViewModel.Created?.Code, created);
        }

        var viewModel = new ProductionOrderDialogViewModel(
            null,
            Require<IProductionQueryRepository>(),
            Require<IProductionApplicationService>(),
            Require<IProductionAttachmentStore>(),
            Require<IProductionPrintDataService>(),
            sessionAccessor,
            Require<IExternalWorkerEditorService>(),
            Require<IGoldTypeCatalogService>(),
            session.BranchId,
            Require<ICraftOrderApplicationService>(),
            Require<ICashFundRepository>(),
            Require<IBankAccountRepository>(),
            Require<ICustomerEditorService>(),
            preferredOrderSource);

        var dialog = new ProductionOrderDialog
        {
            Owner = ResolveOwner(),
            WindowState = WindowState.Maximized,
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var confirmed = dialog.ShowDialog() == true;
        return new ProductionOrderEditResult(
            viewModel.CurrentProductionOrderId,
            string.IsNullOrWhiteSpace(viewModel.Code) ? null : viewModel.Code,
            confirmed);
    }

    private T Require<T>() where T : class =>
        _services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");

    private static Window? ResolveOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
        ?? Application.Current?.MainWindow;
}
