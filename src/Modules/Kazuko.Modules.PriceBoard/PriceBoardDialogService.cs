using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

public sealed class PriceBoardDialogService : IPriceBoardDialogService
{
    private readonly IPriceBoardRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;

    public PriceBoardDialogService(IPriceBoardRepository repository, ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
    }

    public void ShowPriceBoard()
    {
        var viewModel = new PriceBoardDialogViewModel(_repository, _sessionAccessor, this);
        var dialog = new PriceBoardDialog
        {
            Owner = ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        viewModel.RequestClose += () =>
        {
            if (dialog.IsLoaded)
                dialog.DialogResult = false;
            else
                dialog.Close();
        };

        dialog.ShowDialog();
    }

    internal void ShowHistory(Window? owner)
    {
        var viewModel = new PriceHistoryDialogViewModel(_repository);
        var dialog = new PriceHistoryDialog
        {
            Owner = owner ?? ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        viewModel.RequestClose += () =>
        {
            if (dialog.IsLoaded)
                dialog.DialogResult = false;
            else
                dialog.Close();
        };
        dialog.ShowDialog();
    }

    internal bool? ShowAddGold(Window? owner, string unitCode, out AddGoldTypeResult? result)
    {
        result = null;
        var viewModel = new AddGoldTypeDialogViewModel(unitCode);
        var dialog = new AddGoldTypeDialog
        {
            Owner = owner ?? ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        AddGoldTypeResult? captured = null;
        viewModel.RequestClose += value =>
        {
            captured = value;
            if (dialog.IsLoaded)
                dialog.DialogResult = value is not null;
            else
                dialog.Close();
        };
        var ok = dialog.ShowDialog();
        result = captured;
        return ok;
    }

    internal bool? ShowAddCurrency(Window? owner, out AddCurrencyResult? result)
    {
        result = null;
        var viewModel = new AddCurrencyDialogViewModel();
        var dialog = new AddCurrencyDialog
        {
            Owner = owner ?? ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        AddCurrencyResult? captured = null;
        viewModel.RequestClose += value =>
        {
            captured = value;
            if (dialog.IsLoaded)
                dialog.DialogResult = value is not null;
            else
                dialog.Close();
        };
        var ok = dialog.ShowDialog();
        result = captured;
        return ok;
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

public sealed record AddGoldTypeResult(string Code, string Name, decimal PurityRate, decimal BuyPrice, decimal SellPrice);
public sealed record AddCurrencyResult(string Code, string Name, decimal BuyRate, decimal SellRate);
