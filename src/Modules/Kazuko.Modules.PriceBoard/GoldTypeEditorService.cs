using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

public sealed class GoldTypeEditorService : IGoldTypeEditorService
{
    private readonly IGoldTypeCatalogService _goldTypes;

    public GoldTypeEditorService(IGoldTypeCatalogService goldTypes)
    {
        _goldTypes = goldTypes;
    }

    public async Task<GoldTypeOption?> ShowCreateAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var unitCode = GoldUnitConverter.DefaultUnitCode;
        var viewModel = new AddGoldTypeDialogViewModel(unitCode);
        var dialog = new AddGoldTypeDialog
        {
            Owner = ResolveOwner(),
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

        if (dialog.ShowDialog() != true || captured is null)
            return null;

        var buyChi = GoldUnitConverter.ConvertToChi(captured.BuyPrice, unitCode);
        var sellChi = GoldUnitConverter.ConvertToChi(captured.SellPrice, unitCode);
        var purity = captured.PurityRate > 0
            ? captured.PurityRate
            : GoldTypePurity.InferFromCode(captured.Code);

        var id = await _goldTypes.CreateAsync(
            new GoldTypeUpsertRequest(
                captured.Code,
                captured.Name,
                purity,
                buyChi,
                sellChi),
            session,
            cancellationToken);

        return new GoldTypeOption(id, captured.Code.Trim().ToUpperInvariant(), captured.Name.Trim(), purity, true);
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
