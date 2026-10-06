using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales.Dialogs;

public sealed class SalesCustomerLookupDialogViewModel : ObservableObject
{
    private readonly Func<string, Task<IReadOnlyList<SalesCustomerOption>>> _searchAsync;
    private string _searchText = string.Empty;
    private SalesCustomerOption? _selectedCustomer;
    private string _statusMessage = string.Empty;
    private bool? _dialogResult;
    private int _searchVersion;

    public SalesCustomerLookupDialogViewModel(
        Func<string, Task<IReadOnlyList<SalesCustomerOption>>> searchAsync,
        SalesCustomerOption? current)
    {
        _searchAsync = searchAsync ?? throw new ArgumentNullException(nameof(searchAsync));
        SelectedCustomer = current;
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        ConfirmCommand = new RelayCommand(Confirm, () => SelectedCustomer is not null);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        _ = SearchAsync();
    }

    public ObservableCollection<SalesCustomerOption> Results { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = SearchAsync();
        }
    }

    public SalesCustomerOption? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
                ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public SalesCustomerOption? Result { get; private set; }

    public ICommand SearchCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public void ConfirmSelection()
    {
        if (SelectedCustomer is null)
            return;
        Confirm();
    }

    private async Task SearchAsync()
    {
        var version = ++_searchVersion;
        try
        {
            await Task.Delay(180);
            if (version != _searchVersion)
                return;
            var rows = await _searchAsync(SearchText.Trim());
            if (version != _searchVersion)
                return;
            Results.Clear();
            foreach (var row in rows)
                Results.Add(row);
            StatusMessage = Results.Count == 0 ? "Không tìm thấy khách hàng." : string.Empty;
        }
        catch (Exception ex)
        {
            if (version == _searchVersion)
                StatusMessage = ex.Message;
        }
    }

    private void Confirm()
    {
        if (SelectedCustomer is null)
            return;
        Result = SelectedCustomer;
        DialogResult = true;
    }
}
