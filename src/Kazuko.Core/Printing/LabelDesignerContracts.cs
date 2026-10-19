using Kazuko.Core.Authentication;

namespace Kazuko.Core.Printing;

/// <summary>
/// Dịch vụ mở designer sửa mẫu tem nữ trang.
/// Implementation WPF (LabelDesignerDialog) do module Kho cung cấp.
/// </summary>
public interface ILabelDesignerService
{
    /// <summary>
    /// Mở designer sửa mẫu tem. Trả về true nếu người dùng đã lưu mẫu.
    /// </summary>
    Task<bool> OpenDesignerAsync(AuthenticatedSession session);
}
