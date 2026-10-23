using System.Data;
using System.Globalization;
using Kazuko.Core.Company;

namespace Kazuko.Modules.Warehouse.Printing;

internal static class JewelryLabelData
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    internal static string FormatWeight(decimal chi, bool compact)
    {
        decimal value = chi;
        string unit = "chỉ";
        if (compact && chi != 0 && Math.Abs(chi) < 1)
        {
            bool usePhan = Math.Abs(chi) >= 0.1m;
            value *= usePhan ? 10m : 100m;
            unit = usePhan ? "phân" : "ly";
        }
        return value.ToString("0.############################", Vi) + " " + unit;
    }

    internal static string FormatGrams(decimal chi) => (chi * 3.75m).ToString("0.######", Vi) + " g";

    internal static void ApplyOptions(DataTable table, CompanyProfileDto profile, bool compact)
    {
        string[] extra = ["TEN_NPP", "DIACHI_NPP", "TEN_NSX", "DIACHI_NSX", "XUATXU",
            "TONGTRONGLUONG_GRAM", "TRONGLUONGHOT_GRAM", "TRONGLUONGVANG_GRAM"];
        foreach (var column in extra)
            if (!table.Columns.Contains(column)) table.Columns.Add(column, typeof(string));
        foreach (DataRow row in table.Rows)
        {
            row["TEN_NPP"] = profile.DistributorName ?? string.Empty;
            row["DIACHI_NPP"] = profile.DistributorAddress ?? string.Empty;
            row["TEN_NSX"] = profile.ManufacturerName ?? string.Empty;
            row["DIACHI_NSX"] = profile.ManufacturerAddress ?? string.Empty;
            row["XUATXU"] = profile.CountryOfOrigin ?? string.Empty;
            row["TCCS"] = profile.FacilityStandard ?? string.Empty;
            foreach (string field in new[] { "TONGTRONGLUONG", "TRONGLUONGHOT", "TRONGLUONGVANG" })
            {
                decimal chi = row.IsNull(field) ? 0m : Convert.ToDecimal(row[field], CultureInfo.InvariantCulture);
                row[field + "_CHUOI"] = FormatWeight(chi, compact);
                row[field + "_GRAM"] = FormatGrams(chi);
            }
            row["TRONGLUONGVANG_CHUOI2"] = row["TRONGLUONGVANG_CHUOI"];
        }
    }
}
