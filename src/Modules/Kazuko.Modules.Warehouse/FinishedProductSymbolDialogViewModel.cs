using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class FinishedProductSymbolDialogViewModel : ObservableObject
{
    private readonly IWarehouseRepository _warehouse;
    private readonly AuthenticatedSession _session;
    private readonly long? _id;
    public string Title => _id is > 0 ? "Sửa ký hiệu thành phẩm" : "Thêm ký hiệu thành phẩm";

    private string _code = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool? _dialogResult;
    private FinishedProductSymbolOption? _createdSymbol;

    public FinishedProductSymbolDialogViewModel(
        IWarehouseRepository warehouse,
        AuthenticatedSession session, FinishedProductCatalogItem? item = null)
    {
        _warehouse = warehouse;
        _session = session;
        _id = item?.Id;
        _code = item?.Code ?? string.Empty;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => DialogResult = false, () => !IsBusy);
    }

    public FinishedProductSymbolOption? CreatedSymbol => _createdSymbol;

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                (SaveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var saved = await _warehouse.SaveFinishedProductCatalogAsync(true, _id, Code, Code, _session);
            _createdSymbol = new(saved.Id, saved.Code);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
