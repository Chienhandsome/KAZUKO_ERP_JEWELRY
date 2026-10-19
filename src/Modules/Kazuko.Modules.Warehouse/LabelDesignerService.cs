using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Printing;
using Kazuko.Modules.Warehouse.LabelDesigner;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// Mở designer sửa mẫu tem nữ trang (LabelDesignerDialog).
/// </summary>
public sealed class LabelDesignerService : ILabelDesignerService
{
    private readonly IPrintTemplateStore _templates;

    public LabelDesignerService(IPrintTemplateStore templates)
    {
        _templates = templates;
    }

    public Task<bool> OpenDesignerAsync(AuthenticatedSession session)
    {
        var vm = new LabelDesignerViewModel(_templates, session);
        var dialog = new LabelDesignerDialog(vm)
        {
            Owner = Application.Current?.MainWindow,
        };
        // ViewModel tự nạp template trong OnLoaded của dialog.
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
