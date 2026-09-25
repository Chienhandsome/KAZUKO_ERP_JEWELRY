using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Appearance;
using Kazuko.Core.Modules;
using Kazuko.Core.Mvvm;

namespace Kazuko.App;

public sealed record ThemeChoice(string Label, AppTheme Value);
public sealed record FontScaleChoice(string Label, double Value);

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(NavigationItem item, Action<NavigationItem> navigate)
    {
        Item = item;
        NavigateCommand = new RelayCommand(() => navigate(item));
    }

    public NavigationItem Item { get; }
    public ICommand NavigateCommand { get; }
}

public sealed class ShellViewModel : ObservableObject
{
    private readonly IAppearanceService _appearance;
    private object? _currentView;
    private string _currentTitle = string.Empty;
    private ThemeChoice _selectedTheme;
    private FontScaleChoice _selectedFontScale;

    public ShellViewModel(
        string displayName,
        IModuleNavigationRegistry navigation,
        IAppearanceService appearance)
    {
        DisplayName = displayName;
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
        _selectedTheme = Themes.First(x => x.Value == appearance.Current.Theme);
        _selectedFontScale = FontScales.MinBy(x => Math.Abs(x.Value - appearance.Current.FontScale))!;
        NavigationItems = new ObservableCollection<NavigationItemViewModel>(
            navigation.Items.Select(x => new NavigationItemViewModel(x, Navigate)));

        var first = navigation.Items.FirstOrDefault();
        if (first is not null) Navigate(first);
    }

    public string DisplayName { get; }
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public IReadOnlyList<ThemeChoice> Themes { get; }
    public IReadOnlyList<FontScaleChoice> FontScales { get; }

    public object? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        private set => SetProperty(ref _currentTitle, value);
    }

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

    private void Navigate(NavigationItem item)
    {
        CurrentTitle = item.Label;
        CurrentView = item.CreateView();
    }
}
