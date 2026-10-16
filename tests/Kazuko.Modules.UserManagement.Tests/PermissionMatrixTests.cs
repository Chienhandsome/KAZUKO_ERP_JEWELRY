using Kazuko.Core.UserManagement;
using Kazuko.Modules.UserManagement;
using Xunit;

namespace Kazuko.Modules.UserManagement.Tests;

public sealed class PermissionMatrixRowViewModelTests
{
    private static PermissionMatrixRowViewModel CreateRow() =>
        new("SALES_RETAIL", "Bán lẻ", SystemFunctionCatalog.ModuleSales);

    [Fact]
    public void CanAdd_true_auto_checks_edit_and_view()
    {
        var row = CreateRow();

        row.CanAdd = true;

        Assert.True(row.CanAdd);
        Assert.True(row.CanEdit);
        Assert.True(row.CanView);
    }

    [Fact]
    public void CanDelete_true_auto_checks_edit_and_view()
    {
        var row = CreateRow();

        row.CanDelete = true;

        Assert.True(row.CanDelete);
        Assert.True(row.CanEdit);
        Assert.True(row.CanView);
    }

    [Fact]
    public void CanEdit_true_auto_checks_view()
    {
        var row = CreateRow();

        row.CanEdit = true;

        Assert.True(row.CanEdit);
        Assert.True(row.CanView);
        Assert.False(row.CanAdd);
        Assert.False(row.CanDelete);
    }

    [Fact]
    public void CanView_false_clears_add_edit_delete()
    {
        var row = CreateRow();
        row.All = true;

        row.CanView = false;

        Assert.False(row.CanView);
        Assert.False(row.CanAdd);
        Assert.False(row.CanEdit);
        Assert.False(row.CanDelete);
    }

    [Fact]
    public void All_true_checks_all_four()
    {
        var row = CreateRow();

        row.All = true;

        Assert.True(row.All);
        Assert.True(row.CanView);
        Assert.True(row.CanAdd);
        Assert.True(row.CanEdit);
        Assert.True(row.CanDelete);
    }

    [Fact]
    public void All_false_clears_all_four()
    {
        var row = CreateRow();
        row.All = true;

        row.All = false;

        Assert.False(row.All);
        Assert.False(row.CanView);
        Assert.False(row.CanAdd);
        Assert.False(row.CanEdit);
        Assert.False(row.CanDelete);
    }

    [Fact]
    public void All_reflects_partial_state_as_false()
    {
        var row = CreateRow();
        row.CanView = true;

        Assert.False(row.All);
    }

    [Fact]
    public void IsLocked_true_clears_checks_and_disables()
    {
        var row = CreateRow();
        row.All = true;

        row.IsLocked = true;

        Assert.True(row.IsLocked);
        Assert.False(row.All);
        Assert.False(row.CanView);
        Assert.False(row.CanAdd);
        Assert.False(row.CanEdit);
        Assert.False(row.CanDelete);
        Assert.False(row.IsCheckEnabled);
    }

    [Fact]
    public void IsLocked_false_enables_checks()
    {
        var row = CreateRow();
        row.IsLocked = true;

        row.IsLocked = false;

        Assert.False(row.IsLocked);
        Assert.True(row.IsCheckEnabled);
    }

    [Fact]
    public void BuildFrom_returns_all_28_functions_in_catalog_order()
    {
        var rows = PermissionMatrixRowViewModel.BuildFrom(Array.Empty<GroupPermissionRow>());

        Assert.Equal(28, rows.Count);
        Assert.Equal(
            SystemFunctionCatalog.All.Select(f => f.Code),
            rows.Select(r => r.FunctionCode));
    }

    [Fact]
    public void BuildFrom_merges_saved_permissions()
    {
        var saved = new[]
        {
            new GroupPermissionRow(1, "SALES_RETAIL", true, true, true, false, false),
            new GroupPermissionRow(1, "INV_TRANSFER", false, false, false, false, true),
        };

        var rows = PermissionMatrixRowViewModel.BuildFrom(saved);

        var retail = rows.Single(r => r.FunctionCode == "SALES_RETAIL");
        Assert.True(retail.CanView);
        Assert.True(retail.CanAdd);
        Assert.True(retail.CanEdit);
        Assert.False(retail.CanDelete);
        Assert.False(retail.IsLocked);

        var locked = rows.Single(r => r.FunctionCode == "INV_TRANSFER");
        Assert.True(locked.IsLocked);
        Assert.False(locked.IsCheckEnabled);

        // Chức năng không có dòng lưu thì mặc định tắt hết.
        var wholesale = rows.Single(r => r.FunctionCode == "SALES_WHOLESALE");
        Assert.False(wholesale.All);
    }

    [Fact]
    public void ToSaveRow_round_trips_state()
    {
        var row = CreateRow();
        row.CanAdd = true;
        row.IsLocked = false;

        var saved = row.ToSaveRow();

        Assert.Equal("SALES_RETAIL", saved.FunctionCode);
        Assert.True(saved.CanView);
        Assert.True(saved.CanAdd);
        Assert.True(saved.CanEdit);
        Assert.False(saved.CanDelete);
        Assert.False(saved.IsLocked);
    }
}

public sealed class SystemFunctionCatalogTests
{
    // Phải khớp EXACT với seed trong migration 0076.
    private static readonly string[] ExpectedCodes =
    [
        "SALES_RETAIL", "SALES_WHOLESALE", "CRAFT_ORDER", "OLD_GOLD_PURCHASE",
        "INV_MATERIAL_RECEIPT", "INV_FINISHED_RECEIPT", "INV_TRANSFER", "INV_STOCKTAKE",
        "PROD_ORDER", "PROD_MATERIAL_ISSUE", "PROD_GOLD_RECONCILE",
        "DEBT_CUSTOMER", "DEBT_WORKER_GOLD", "DEBT_SETTLEMENT",
        "FIN_CASH_IN", "FIN_CASH_OUT", "FIN_FUND",
        "CAT_CUSTOMERS", "CAT_SUPPLIERS", "CAT_EXTERNAL_WORKERS", "CAT_EMPLOYEES", "CAT_GOLD_PRICE",
        "REP_VIEW", "REP_EXPORT",
        "SYS_USER_MANAGEMENT", "SYS_SETTINGS", "SYS_DATABASE", "SYS_DATA_DELETE",
    ];

    [Fact]
    public void Catalog_has_exactly_28_unique_codes()
    {
        Assert.Equal(28, SystemFunctionCatalog.All.Count);
        Assert.Equal(28, SystemFunctionCatalog.All.Select(f => f.Code.ToUpperInvariant()).Distinct().Count());
        Assert.Equal(ExpectedCodes, SystemFunctionCatalog.All.Select(f => f.Code));
    }

    [Fact]
    public void Catalog_groups_cover_all_8_modules()
    {
        var groups = SystemFunctionCatalog.All.Select(f => f.ModuleGroup).Distinct().ToArray();
        Assert.Equal(
            new[]
            {
                SystemFunctionCatalog.ModuleSales,
                SystemFunctionCatalog.ModuleWarehouse,
                SystemFunctionCatalog.ModuleProduction,
                SystemFunctionCatalog.ModuleDebt,
                SystemFunctionCatalog.ModuleFinance,
                SystemFunctionCatalog.ModuleCatalog,
                SystemFunctionCatalog.ModuleReports,
                SystemFunctionCatalog.ModuleSystem,
            },
            groups);
    }
}
