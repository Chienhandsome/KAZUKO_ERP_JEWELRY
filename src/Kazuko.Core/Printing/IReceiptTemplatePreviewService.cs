using Kazuko.Core.Authentication;

namespace Kazuko.Core.Printing;

public interface IReceiptTemplatePreviewService
{
    /// <summary>Ảnh PNG trang đầu mẫu bill đã lưu, với dữ liệu minh họa.</summary>
    Task<byte[]> CreatePreviewAsync(string templateCode, AuthenticatedSession session);
}
