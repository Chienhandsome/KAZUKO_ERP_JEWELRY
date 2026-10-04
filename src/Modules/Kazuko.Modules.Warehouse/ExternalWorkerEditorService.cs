using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class ExternalWorkerEditorService : IExternalWorkerEditorService
{
    private readonly IExternalWorkerRepository _workers;
    private readonly IOutsourcedCraftService _craft;
    private readonly ICurrentSessionAccessor _sessionAccessor;

    public ExternalWorkerEditorService(
        IExternalWorkerRepository workers,
        IOutsourcedCraftService craft,
        ICurrentSessionAccessor sessionAccessor)
    {
        _workers = workers;
        _craft = craft;
        _sessionAccessor = sessionAccessor;
    }

    public ExternalWorkerEditResult? Show()
    {
        var session = _sessionAccessor.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

        var viewModel = new ExternalWorkerDialogViewModel(_workers, _craft, session);
        var dialog = new ExternalWorkerDialog(viewModel)
        {
            Owner = ResolveOwner(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        dialog.ShowDialog();

        if (viewModel.LastSaved is { } saved)
            return new ExternalWorkerEditResult(saved.Id, saved.Code, saved.Name, saved.IsActive);

        if (viewModel.SelectedWorker is { } selected)
            return new ExternalWorkerEditResult(selected.Id, selected.Code, selected.Name, selected.IsActive);

        return null;
    }

    private static Window? ResolveOwner()
    {
        if (Application.Current?.Windows is null)
            return Application.Current?.MainWindow;

        foreach (Window window in Application.Current.Windows)
        {
            if (window.IsActive)
                return window;
        }

        return Application.Current.MainWindow;
    }
}
