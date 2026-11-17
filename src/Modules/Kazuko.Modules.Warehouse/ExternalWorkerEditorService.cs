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
        var (viewModel, _) = OpenDialog(false, null);
        return viewModel.LastSaved is { } saved
            ? new ExternalWorkerEditResult(saved.Id, saved.Code, saved.Name, saved.IsActive)
            : null;
    }

    public ExternalWorkerCatalogResult ShowCatalog(long? selectedId)
    {
        var (viewModel, accepted) = OpenDialog(true, selectedId);
        var selected = accepted && viewModel.SelectedWorker is { IsActive: true } worker
            ? new ExternalWorkerEditResult(worker.Id, worker.Code, worker.Name, worker.IsActive)
            : null;
        return new ExternalWorkerCatalogResult(selected, viewModel.HasChanges);
    }

    private (ExternalWorkerDialogViewModel ViewModel, bool Accepted) OpenDialog(bool canChoose, long? selectedId)
    {
        var session = _sessionAccessor.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

        var viewModel = new ExternalWorkerDialogViewModel(_workers, _craft, session, canChoose, selectedId);
        var dialog = new ExternalWorkerDialog(viewModel)
        {
            Owner = ResolveOwner(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        return (viewModel, dialog.ShowDialog() == true);
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
