namespace Kazuko.Core.UserManagement;

/// <summary>
/// Chức năng hệ thống dùng cho ma trận phân quyền theo nhóm.
/// FUNCTION_CODE dạng "SALES_RETAIL" — KHỚP với seed trong migration 0076.
/// </summary>
public sealed record SystemFunctionDefinition(string Code, string Name, string ModuleGroup)
{
    public override string ToString() => Name;
}

/// <summary>
/// Danh mục 28 chức năng của hệ thống, nhóm theo module thực tế.
/// Thứ tự = thứ tự hiển thị trên ma trận quyền.
/// </summary>
public static class SystemFunctionCatalog
{
    public const string ModuleSales = "Bán hàng";
    public const string ModuleWarehouse = "Kho";
    public const string ModuleProduction = "Sản xuất";
    public const string ModuleDebt = "Công nợ";
    public const string ModuleFinance = "Tài chính";
    public const string ModuleCatalog = "Danh mục";
    public const string ModuleReports = "Báo cáo";
    public const string ModuleSystem = "Hệ thống";

    public static readonly IReadOnlyList<SystemFunctionDefinition> All =
    [
        new("SALES_RETAIL", "Bán lẻ", ModuleSales),
        new("SALES_WHOLESALE", "Bán sỉ", ModuleSales),
        new("CRAFT_ORDER", "Đơn đặt chế tác", ModuleSales),
        new("OLD_GOLD_PURCHASE", "Mua vàng cũ", ModuleSales),

        new("INV_MATERIAL_RECEIPT", "Nhập kho NVL", ModuleWarehouse),
        new("INV_FINISHED_RECEIPT", "Nhập kho thành phẩm", ModuleWarehouse),
        new("INV_TRANSFER", "Xuất/điều chuyển kho", ModuleWarehouse),
        new("INV_STOCKTAKE", "Kiểm kê", ModuleWarehouse),

        new("PROD_ORDER", "Lệnh sản xuất", ModuleProduction),
        new("PROD_MATERIAL_ISSUE", "Giao NVL", ModuleProduction),
        new("PROD_GOLD_RECONCILE", "Đối chiếu vàng", ModuleProduction),

        new("DEBT_CUSTOMER", "Công nợ KH/NCC", ModuleDebt),
        new("DEBT_WORKER_GOLD", "Nợ vàng thợ", ModuleDebt),
        new("DEBT_SETTLEMENT", "Thu/Chi công nợ", ModuleDebt),

        new("FIN_CASH_IN", "Phiếu thu", ModuleFinance),
        new("FIN_CASH_OUT", "Phiếu chi", ModuleFinance),
        new("FIN_FUND", "Quỹ", ModuleFinance),

        new("CAT_CUSTOMERS", "Khách hàng", ModuleCatalog),
        new("CAT_SUPPLIERS", "Nhà cung cấp", ModuleCatalog),
        new("CAT_EXTERNAL_WORKERS", "Thợ ngoài", ModuleCatalog),
        new("CAT_EMPLOYEES", "Nhân sự", ModuleCatalog),
        new("CAT_GOLD_PRICE", "Bảng giá vàng", ModuleCatalog),

        new("REP_VIEW", "Xem báo cáo", ModuleReports),
        new("REP_EXPORT", "Xuất báo cáo", ModuleReports),

        new("SYS_USER_MANAGEMENT", "Quản lý người dùng", ModuleSystem),
        new("SYS_SETTINGS", "Cấu hình hệ thống", ModuleSystem),
        new("SYS_DATABASE", "Cơ sở dữ liệu", ModuleSystem),
        new("SYS_DATA_DELETE", "Xóa dữ liệu", ModuleSystem),
    ];

    public static SystemFunctionDefinition? Find(string code) =>
        All.FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Quy tắc kế thừa quyền (được UI ma trận áp dụng tự động + hiển thị giải thích).
/// </summary>
public static class PermissionInheritanceRules
{
    public const string Explanation =
        "Quy tắc kế thừa quyền trong nhóm:\n" +
        "• Tích chọn \"Thêm\" hoặc \"Xóa\" → tự động tích \"Sửa\" và \"Xem\" (vì thêm/xóa đều cần xem và sửa).\n" +
        "• Tích chọn \"Sửa\" → tự động tích \"Xem\".\n" +
        "• Bỏ chọn \"Xem\" → tự động bỏ \"Thêm\"/\"Sửa\"/\"Xóa\".\n" +
        "• Tích chọn \"Tất cả\" → tích đủ 4 quyền Xem/Thêm/Sửa/Xóa.\n" +
        "• Tích chọn \"Bị khóa\" → vô hiệu hóa và bỏ toàn bộ 4 quyền của chức năng đó.\n" +
        "• Nhớ bấm \"Lưu\" để ghi ma trận vào cơ sở dữ liệu.";
}

/// <summary>Nhóm người dùng (USER_GROUPS).</summary>
public sealed class UserGroup
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public int UserCount { get; set; }

    public override string ToString() => Name;
}

/// <summary>Một dòng ma trận quyền của nhóm (GROUP_PERMISSIONS).</summary>
public sealed record GroupPermissionRow(
    long GroupId,
    string FunctionCode,
    bool CanView,
    bool CanAdd,
    bool CanEdit,
    bool CanDelete,
    bool IsLocked);

/// <summary>Người dùng hệ thống (USERS + join nhân viên/kho/nhóm). Tài khoản Admin không trả về ở danh sách.</summary>
public sealed class SystemUser
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? Email { get; set; }
    public long? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public long? GroupId { get; set; }
    public string? GroupName { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Dữ liệu tạo/sửa người dùng. Password null hoặc rỗng khi sửa = giữ nguyên mật khẩu cũ.</summary>
public sealed record UserSaveRequest(
    long? Id,
    long EmployeeId,
    long? WarehouseId,
    string? Email,
    long GroupId,
    string Username,
    string? Password,
    string? Notes,
    bool IsActive,
    long CreatedBy);

/// <summary>Dữ liệu tạo/sửa nhóm người dùng.</summary>
public sealed record UserGroupSaveRequest(
    long? Id,
    string Code,
    string Name,
    string? Icon,
    string? Description,
    bool IsActive,
    long CreatedBy);

/// <summary>Một dòng ma trận quyền cần lưu (được UI áp quy tắc kế thừa trước khi gửi).</summary>
public sealed record PermissionSaveRow(
    string FunctionCode,
    bool CanView,
    bool CanAdd,
    bool CanEdit,
    bool CanDelete,
    bool IsLocked);

public sealed record EmployeeOption(long Id, string Code, string FullName, string? Email)
{
    public override string ToString() => $"{Code} — {FullName}";
}

public sealed record WarehouseOption(long Id, string Code, string Name)
{
    public override string ToString() => $"{Code} — {Name}";
}

public sealed record UserGroupOption(long Id, string Code, string Name)
{
    public override string ToString() => Name;
}

public interface IUserManagementRepository
{
    Task<IReadOnlyList<UserGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Danh sách người dùng của một nhóm. Tài khoản Admin hệ thống (ROLE ADMIN)
    /// KHÔNG bao giờ được trả về.
    /// </summary>
    Task<IReadOnlyList<SystemUser>> GetUsersAsync(long groupId, CancellationToken cancellationToken = default);

    Task<SystemUser?> GetUserByIdAsync(long userId, CancellationToken cancellationToken = default);

    Task<UserGroup?> GetGroupByIdAsync(long groupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupPermissionRow>> GetGroupPermissionsAsync(
        long groupId, CancellationToken cancellationToken = default);

    Task SaveGroupPermissionsAsync(
        long groupId,
        IReadOnlyList<PermissionSaveRow> rows,
        long updatedBy,
        CancellationToken cancellationToken = default);

    Task<UserGroup> SaveGroupAsync(UserGroupSaveRequest request, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(long groupId, CancellationToken cancellationToken = default);

    Task<SystemUser> SaveUserAsync(UserSaveRequest request, CancellationToken cancellationToken = default);

    Task DeleteUserAsync(long userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeOption>> GetEmployeesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WarehouseOption>> GetWarehousesAsync(CancellationToken cancellationToken = default);
}
