using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Suppliers;
using Kazuko.Core.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class MaterialReceiptSupplierCatalogTests
{
    [Fact]
    public void Selecting_a_supplier_keeps_existing_receipt_lines()
    {
        var first = Supplier(1);
        var second = Supplier(2);
        var editor = new CatalogEditor { Result = new(new(second.Id, second.Code, second.Name, "", "", true), true) };
        IReadOnlyList<SupplierListItem> available = [first];
        var viewModel = Create(editor, () => available);
        viewModel.SelectedSupplier = first;
        viewModel.Lines.Add(new(10, "NVL", "Vật liệu", "", "", 1, 2));
        available = [first, second];

        viewModel.OpenSupplierCatalogCommand.Execute(null);

        Assert.Equal((true, (long?)1), editor.Request);
        Assert.Same(second, viewModel.SelectedSupplier);
        Assert.Single(viewModel.Lines);
    }

    [Fact]
    public void Closing_catalog_after_archiving_selected_supplier_requires_a_new_selection()
    {
        var first = Supplier(1);
        var second = Supplier(2);
        var editor = new CatalogEditor { Result = new(null, true) };
        IReadOnlyList<SupplierListItem> available = [first, second];
        var viewModel = Create(editor, () => available);
        viewModel.SelectedSupplier = first;
        available = [second];

        viewModel.OpenSupplierCatalogCommand.Execute(null);

        Assert.Null(viewModel.SelectedSupplier);
        Assert.Same(second, Assert.Single(viewModel.Suppliers));
        Assert.Contains("chọn lại", viewModel.ErrorMessage);
    }

    private static SupplierListItem Supplier(long id) =>
        new(id, $"NCC{id}", $"Nhà cung cấp {id}", "", "", "", "", 0, 0, null, true, "", "", "", "", "", 1);

    private static MaterialReceiptDialogViewModel Create(CatalogEditor editor, Func<IReadOnlyList<SupplierListItem>> load) =>
        new(Stub<IMaterialReceiptService>(),
            Stub<IWarehouseRepository>(method => method switch
            {
                nameof(IWarehouseRepository.GetLocationsAsync) => Task.FromResult<IReadOnlyList<WarehouseLocationOption>>([]),
                nameof(IWarehouseRepository.SearchMaterialsAsync) => Task.FromResult<IReadOnlyList<MaterialOption>>([]),
                _ => throw new NotSupportedException(method)
            }),
            Stub<ISupplierRepository>(_ => Task.FromResult(load())), editor, Stub<IGoldTypeEditorService>(),
            Stub<ICashFundRepository>(_ => Task.FromResult<IReadOnlyList<CashFundOption>>([])),
            Stub<IBankAccountRepository>(_ => Task.FromResult<IReadOnlyList<BankAccountOption>>([])),
            new AuthenticatedSession(1, "tester", "Tester", "ADMIN", 1, "CN01", "Chi nhánh"));

    private static T Stub<T>(Func<string, object?>? call = null) where T : class
    {
        var stub = DispatchProxy.Create<T, ServiceProxy>();
        ((ServiceProxy)(object)stub).Call = call;
        return stub;
    }

    public class ServiceProxy : DispatchProxy
    {
        public Func<string, object?>? Call { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Call?.Invoke(targetMethod!.Name) ?? throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class CatalogEditor : ISupplierEditorService
    {
        public SupplierCatalogResult Result { get; init; } = new(null, false);
        public (bool SelectSupplier, long? SelectedId) Request { get; private set; }
        public SupplierEditResult? ShowCreate() => throw new NotSupportedException();
        public SupplierEditResult? ShowEdit(long supplierId) => throw new NotSupportedException();
        public SupplierCatalogResult ShowCatalog(bool selectSupplier = false, long? selectedId = null)
        {
            Request = (selectSupplier, selectedId);
            return Result;
        }
    }
}
