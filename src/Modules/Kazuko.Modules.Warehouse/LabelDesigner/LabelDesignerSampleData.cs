using System.Data;
using Kazuko.Modules.Warehouse;
using Kazuko.Core.Company;
using Kazuko.Modules.Warehouse.Printing;

namespace Kazuko.Modules.Warehouse.LabelDesigner;

/// <summary>
/// Dữ liệu mẫu để xem trước mẫu tem trong designer.
/// Các giá trị khớp định dạng thực tế khi in tem thật.
/// </summary>
public static class LabelDesignerSampleData
{
    public static DataTable BuildSampleTable(CompanyProfileDto profile, bool compactWeightUnits = false)
    {
        var table = BuildSampleTable(profile.Name, profile.Address ?? string.Empty, profile.FacilityStandard);
        JewelryLabelData.ApplyOptions(table, profile, compactWeightUnits);
        return table;
    }

    public static DataTable BuildSampleTable(string companyName, string companyAddress, string? facilityStandard)
    {
        var table = new DataTable();
        table.Columns.Add("NGAYNHAP", typeof(DateTime));
        table.Columns.Add("MAVACH", typeof(string));
        table.Columns.Add("THANHTIEN", typeof(decimal));
        table.Columns.Add("TUOI", typeof(decimal));
        table.Columns.Add("DONGIA", typeof(decimal));
        table.Columns.Add("LOAIVANG", typeof(string));
        table.Columns.Add("QUAY", typeof(string));
        table.Columns.Add("CONGNHAP", typeof(decimal));
        table.Columns.Add("CONGBAN", typeof(decimal));
        table.Columns.Add("TONGTRONGLUONG", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG", typeof(decimal));
        table.Columns.Add("TRONGLUONGHOT", typeof(decimal));
        table.Columns.Add("TONGTRONGLUONG_LI", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG_LI", typeof(decimal));
        table.Columns.Add("TRONGLUONGHOT_LI", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG_CHUOI", typeof(string));
        table.Columns.Add("DMATHANGID", typeof(string));
        table.Columns.Add("TENHANG", typeof(string));
        table.Columns.Add("NOTE", typeof(string));
        table.Columns.Add("CONGNHAP_NGHIN", typeof(int));
        table.Columns.Add("CONGBAN_NGHIN", typeof(int));
        table.Columns.Add("NHACUNGCAP", typeof(string));
        table.Columns.Add("TRONGLUONGVANG_CHUOI2", typeof(string));
        table.Columns.Add("THUONGHIEU", typeof(string));
        table.Columns.Add("MANHACUNGCAP", typeof(string));
        table.Columns.Add("KYHIEU", typeof(string));
        table.Columns.Add("CONGSI", typeof(decimal));
        table.Columns.Add("CONGSI_NGHIN", typeof(int));
        table.Columns.Add("TONGTRONGLUONG_DEM", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG_DEM", typeof(decimal));
        table.Columns.Add("TRONGLUONGHOT_DEM", typeof(decimal));
        table.Columns.Add("TONGTRONGLUONG_CHUOI", typeof(string));
        table.Columns.Add("TRONGLUONGHOT_CHUOI", typeof(string));
        table.Columns.Add("DIACHINCC", typeof(string));
        table.Columns.Add("TIENDABAN_NGHIN", typeof(int));
        table.Columns.Add("TIENDANHAP_NGHIN", typeof(int));
        table.Columns.Add("GIABAN_NGHIN", typeof(int));
        table.Columns.Add("GIABAN", typeof(decimal));
        table.Columns.Add("TIENDABAN", typeof(decimal));
        table.Columns.Add("TIENDANHAP", typeof(decimal));
        table.Columns.Add("THANHTIENBAN", typeof(decimal));
        table.Columns.Add("MAHIEU", typeof(string));
        table.Columns.Add("TCCS", typeof(string));

        var row = table.NewRow();
        row["NGAYNHAP"] = DateTime.Today;
        row["MAVACH"] = "B600022";
        row["THANHTIEN"] = 0m;
        row["TUOI"] = 750m;
        row["DONGIA"] = 0m;
        row["LOAIVANG"] = "750";
        row["QUAY"] = string.Empty;
        row["CONGNHAP"] = 200000m;
        row["CONGBAN"] = 250000m;
        row["TONGTRONGLUONG"] = 2.5m;
        row["TRONGLUONGVANG"] = 2.2m;
        row["TRONGLUONGHOT"] = 0.3m;
        row["TONGTRONGLUONG_LI"] = 0m;
        row["TRONGLUONGVANG_LI"] = 0m;
        row["TRONGLUONGHOT_LI"] = 0m;
        row["TRONGLUONGVANG_CHUOI"] = "2.2 chỉ";
        row["DMATHANGID"] = string.Empty;
        row["TENHANG"] = "BÔNG CHIẾC LÁ";
        row["NOTE"] = string.Empty;
        row["CONGNHAP_NGHIN"] = 200;
        row["CONGBAN_NGHIN"] = 250;
        row["TRONGLUONGVANG_CHUOI2"] = "2.2 chỉ";
        row["THUONGHIEU"] = string.Empty;
        row["MANHACUNGCAP"] = string.Empty;
        row["KYHIEU"] = string.Empty;
        row["CONGSI"] = 0m;
        row["CONGSI_NGHIN"] = 0;
        row["TONGTRONGLUONG_DEM"] = 0m;
        row["TRONGLUONGVANG_DEM"] = 0m;
        row["TRONGLUONGHOT_DEM"] = 0m;
        row["TONGTRONGLUONG_CHUOI"] = "2.5 chỉ";
        row["TRONGLUONGHOT_CHUOI"] = "0.3 chỉ";
        row["TIENDABAN_NGHIN"] = 0;
        row["TIENDANHAP_NGHIN"] = 0;
        row["GIABAN_NGHIN"] = 0;
        row["GIABAN"] = 0m;
        row["TIENDABAN"] = 0m;
        row["TIENDANHAP"] = 0m;
        row["THANHTIENBAN"] = 0m;
        row["MAHIEU"] = string.Empty;
        FinishedGoodsLabelPrintService.ApplyCompanyLabelFields(row, companyName, companyAddress, facilityStandard);
        table.Rows.Add(row);

        return table;
    }
}
