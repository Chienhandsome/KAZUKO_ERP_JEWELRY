using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class ExternalWorkerCatalogTests
{
    [Fact]
    public void Filter_updates_as_typed_and_only_active_workers_can_be_chosen()
    {
        var workers = DispatchProxy.Create<IExternalWorkerRepository, MaterialReceiptSupplierCatalogTests.ServiceProxy>();
        ((MaterialReceiptSupplierCatalogTests.ServiceProxy)(object)workers).Call = _ =>
            Task.FromResult<IReadOnlyList<ExternalWorkerListItem>>(
                [new(1, "T01", "Trần Văn An", "", "", "", "", "", true),
                 new(2, "T02", "Lê Văn Bình", "", "", "123456", "", "", false)]);
        var craft = DispatchProxy.Create<IOutsourcedCraftService, MaterialReceiptSupplierCatalogTests.ServiceProxy>();
        ((MaterialReceiptSupplierCatalogTests.ServiceProxy)(object)craft).Call = _ =>
            Task.FromResult<IReadOnlyList<OutsourcedHoldingRow>>([]);
        var viewModel = new ExternalWorkerDialogViewModel(workers, craft,
            new AuthenticatedSession(1, "tester", "Tester", "ADMIN", 1, "CN01", "Chi nhánh"),
            canChoose: true, initialId: 1);

        Assert.Equal(1, Assert.Single(viewModel.Workers).Id);
        Assert.Null(viewModel.DialogResult);
        viewModel.ActiveOnly = false;
        viewModel.SearchText = "123456";
        Assert.Equal(2, Assert.Single(viewModel.Workers).Id);
        Assert.Equal(2, viewModel.SelectedWorker?.Id);
        Assert.False(viewModel.ChooseCommand.CanExecute(null));

        viewModel.SearchText = "trần";
        Assert.Equal(1, Assert.Single(viewModel.Workers).Id);
        viewModel.ChooseCommand.Execute(null);
        Assert.True(viewModel.DialogResult);
    }
}
