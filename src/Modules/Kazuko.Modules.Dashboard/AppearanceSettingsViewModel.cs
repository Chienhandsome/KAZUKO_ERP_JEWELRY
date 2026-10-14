using Kazuko.Core.Appearance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Dashboard;

public sealed record ThemeChoice(string Label, AppTheme Value)
{
    public override string ToString() => Label;
}

public sealed record FontScaleChoice(string Label, double Value)
{
    public override string ToString() => Label;
}

public sealed class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IAppearanceService _appearance;
    private ThemeChoice _selectedTheme;
    private FontScaleChoice _selectedFontScale;

    public AppearanceSettingsViewModel(IAppearanceService appearance)
    {
        _appearance = appearance;
        Themes =
        [
            new ThemeChoice("Sáng", AppTheme.Light),
            new ThemeChoice("Tối", AppTheme.Dark)
        ];
        FontScales =
        [
            new FontScaleChoice("90%", 0.9),
            new FontScaleChoice("100%", 1.0),
            new FontScaleChoice("110%", 1.1),
            new FontScaleChoice("125%", 1.25),
            new FontScaleChoice("150%", 1.5)
        ];
        _selectedTheme = Themes.First(choice => choice.Value == appearance.Current.Theme);
        _selectedFontScale = FontScales.MinBy(
            choice => Math.Abs(choice.Value - appearance.Current.FontScale))!;
    }

    public IReadOnlyList<ThemeChoice> Themes { get; }
    public IReadOnlyList<FontScaleChoice> FontScales { get; }

    public ThemeChoice SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is not null && SetProperty(ref _selectedTheme, value))
                _appearance.Apply(value.Value, SelectedFontScale.Value);
        }
    }

    public FontScaleChoice SelectedFontScale
    {
        get => _selectedFontScale;
        set
        {
            if (value is not null && SetProperty(ref _selectedFontScale, value))
                _appearance.Apply(SelectedTheme.Value, value.Value);
        }
    }
}
