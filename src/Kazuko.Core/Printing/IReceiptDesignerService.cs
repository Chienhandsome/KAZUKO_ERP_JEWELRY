using Kazuko.Core.Authentication;

namespace Kazuko.Core.Printing;

public interface IReceiptDesignerService
{
    Task<bool> OpenDesignerAsync(string templateCode, AuthenticatedSession session);
}
