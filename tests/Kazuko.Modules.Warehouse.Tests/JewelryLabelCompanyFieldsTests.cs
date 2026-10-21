using System.Data;
using Kazuko.Modules.Warehouse;
using Kazuko.Modules.Warehouse.LabelDesigner;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class JewelryLabelCompanyFieldsTests
{
    [Fact]
    public void Sample_label_uses_company_profile_for_supplier_and_standard()
    {
        using var table = LabelDesignerSampleData.BuildSampleTable(
            "VÀNG BẠC KAZUKO",
            "925 Âu Cơ",
            "01:2026/KZJ");

        var row = table.Rows[0];
        Assert.Equal("B600022", row["MAVACH"]);
        Assert.Equal("VÀNG BẠC KAZUKO", row["NHACUNGCAP"]);
        Assert.Equal("925 Âu Cơ", row["DIACHINCC"]);
        Assert.Equal("01:2026/KZJ", row["TCCS"]);
    }

    [Fact]
    public void Company_fields_leave_blanks_when_profile_is_empty()
    {
        var table = new DataTable();
        table.Columns.Add("NHACUNGCAP", typeof(string));
        table.Columns.Add("DIACHINCC", typeof(string));
        table.Columns.Add("TCCS", typeof(string));
        var row = table.NewRow();

        FinishedGoodsLabelPrintService.ApplyCompanyLabelFields(row, "", "", null);

        Assert.Equal(string.Empty, row["NHACUNGCAP"]);
        Assert.Equal(string.Empty, row["DIACHINCC"]);
        Assert.Equal(string.Empty, row["TCCS"]);
    }
}
