using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClosedXML.Excel;
using Kazuko.Shared.UI.DataGridLayout;
using Microsoft.Win32;
using Kazuko.Core.UserManagement;

namespace Kazuko.Shared.UI.Services;

/// <summary>Kiểu định dạng ô khi xuất Excel.</summary>
public enum ExcelCellKind
{
    Text,
    Integer,
    Money,
    Quantity,
    Date
}

/// <summary>Định nghĩa một cột khi xuất Excel.</summary>
/// <param name="Key">Khớp với DataGridColumnLayout.Key của cột trên grid (dùng để tôn trọng ẩn/hiện cột).</param>
/// <param name="Header">Tiêu đề cột trong file Excel.</param>
/// <param name="ValueSelector">Lấy giá trị từ dòng dữ liệu.</param>
/// <param name="Kind">Kiểu định dạng ô.</param>
public sealed record ExcelExportColumn<T>(
    string Key,
    string Header,
    Func<T, object?> ValueSelector,
    ExcelCellKind Kind = ExcelCellKind.Text);

/// <summary>
/// Service xuất Excel dùng chung: header in đậm, freeze dòng đầu, autofit cột,
/// chỉ xuất các cột đang hiện trên grid, định dạng số/ngày theo vi-VN.
/// </summary>
public static class ExcelExportService
{
    private static IAuthorizationService? _authorization;
    public static void ConfigureAuthorization(IAuthorizationService authorization) => _authorization = authorization;
    private const string MoneyFormat = "#,##0";
    private const string QuantityFormat = "#,##0.00";
    private const string DateFormat = "dd/MM/yyyy";

    /// <summary>
    /// Xuất danh sách ra file .xlsx. Trả về true nếu xuất thành công.
    /// </summary>
    /// <param name="dialogTitle">Tiêu đề hộp thoại chọn nơi lưu.</param>
    /// <param name="defaultFileName">Tên file gợi ý (không gồm đuôi).</param>
    /// <param name="grid">DataGrid nguồn (để đọc cột đang hiện); null = xuất tất cả cột.</param>
    /// <param name="rows">Các dòng dữ liệu (đã filter).</param>
    /// <param name="columns">Định nghĩa cột.</param>
    public static bool Export<T>(
        string dialogTitle,
        string defaultFileName,
        DataGrid? grid,
        IEnumerable<T> rows,
        IReadOnlyList<ExcelExportColumn<T>> columns)
    {
        if (_authorization is not null)
        {
            var type = typeof(T).FullName ?? string.Empty;
            var domain = type.Contains("Payroll") ? "HR_PAYROLL" : type.Contains("Customers") ? "CAT_CUSTOMERS" : type.Contains("Suppliers") ? "CAT_SUPPLIERS"
                : type.Contains("HumanResources") ? "CAT_EMPLOYEES" : type.Contains("Production") ? "PROD_ORDER"
                : type.Contains("WorkerGold") ? "DEBT_WORKER_GOLD" : type.Contains("Debt") ? "DEBT_CUSTOMER" : null;
            var requirements = new List<PermissionRequirement> { new("DATA_EXPORT") };
            if (domain is not null) requirements.Add(new(domain));
            try { _authorization.DemandAsync(requirements).GetAwaiter().GetResult(); }
            catch (Exception ex)
            { MessageBox.Show(ex.Message, "Xuất Excel", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        }
        var rowList = rows.ToList();
        if (rowList.Count == 0)
        {
            MessageBox.Show(
                "Không có dữ liệu để xuất.",
                "Xuất Excel",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var visibleColumns = ResolveVisibleColumns(grid, columns);
        if (visibleColumns.Count == 0)
        {
            MessageBox.Show(
                "Không có cột nào đang hiển thị để xuất.",
                "Xuất Excel",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var dialog = new SaveFileDialog
        {
            Title = dialogTitle,
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"{defaultFileName}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx"
        };
        if (dialog.ShowDialog() != true)
            return false;

        try
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Sheet1");

            // Header: in đậm, nền xám nhạt
            for (var c = 0; c < visibleColumns.Count; c++)
            {
                var cell = sheet.Cell(1, c + 1);
                cell.Value = visibleColumns[c].Header;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }
            sheet.SheetView.FreezeRows(1);

            // Dữ liệu
            for (var r = 0; r < rowList.Count; r++)
            {
                for (var c = 0; c < visibleColumns.Count; c++)
                {
                    var col = visibleColumns[c];
                    var cell = sheet.Cell(r + 2, c + 1);
                    SetCellValue(cell, col.ValueSelector(rowList[r]), col.Kind);
                }
            }

            sheet.Columns().AdjustToContents();

            workbook.SaveAs(dialog.FileName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không thể xuất file Excel.\n{ex.Message}",
                "Xuất Excel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Tìm DataGrid đầu tiên trong cây visual (dùng cho view có nhiều tab/grid).</summary>
    public static DataGrid? FindDataGrid(DependencyObject? root)
    {
        if (root is null)
            return null;
        if (root is DataGrid grid)
            return grid;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindDataGrid(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }
        return null;
    }

    private static List<ExcelExportColumn<T>> ResolveVisibleColumns<T>(
        DataGrid? grid,
        IReadOnlyList<ExcelExportColumn<T>> columns)
    {
        if (grid is null)
            return columns.ToList();

        // Map key -> cột grid đang hiển thị
        var visibleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedKeys = new List<string>();
        foreach (var gridColumn in grid.Columns.OrderBy(c => c.DisplayIndex))
        {
            if (gridColumn.Visibility != Visibility.Visible)
                continue;
            var key = DataGridColumnLayout.GetColumnKey(gridColumn);
            if (string.IsNullOrWhiteSpace(key) || !visibleKeys.Add(key))
                continue;
            orderedKeys.Add(key);
        }

        var byKey = columns.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        var result = new List<ExcelExportColumn<T>>();
        foreach (var key in orderedKeys)
        {
            if (byKey.TryGetValue(key, out var col))
                result.Add(col);
        }
        return result;
    }

    private static void SetCellValue(IXLCell cell, object? value, ExcelCellKind kind)
    {
        if (value is null)
        {
            cell.Value = Blank.Value;
            return;
        }

        switch (kind)
        {
            case ExcelCellKind.Money:
            case ExcelCellKind.Integer:
                cell.Value = Convert.ToDecimal(value);
                cell.Style.NumberFormat.Format = MoneyFormat;
                break;
            case ExcelCellKind.Quantity:
                cell.Value = Convert.ToDecimal(value);
                cell.Style.NumberFormat.Format = QuantityFormat;
                break;
            case ExcelCellKind.Date:
                if (value is DateTime dt)
                {
                    cell.Value = dt;
                    cell.Style.NumberFormat.Format = DateFormat;
                }
                else
                {
                    cell.Value = value.ToString();
                }
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }
}
