using System.Windows;
using Kazuko.Core.Appearance;

namespace Kazuko.Shared.UI.Services;

public sealed class AppearanceService : IAppearanceService
{
    private static readonly IReadOnlyDictionary<string, double> BaseFontSizes =
        new Dictionary<string, double>
        {
            ["FontSize.Caption"] = 12,
            ["FontSize.Body"] = 14,
            ["FontSize.Subtitle"] = 16,
            ["FontSize.Heading"] = 22,
            ["FontSize.Display"] = 32
        };

    private readonly IPreferencesService _preferences;

    public AppearanceService(IPreferencesService preferences)
    {
        _preferences = preferences;
        Current = Normalize(preferences.Load());
    }

    public AppPreferences Current { get; private set; }
    public event EventHandler<AppPreferences>? Changed;

    public void Apply(AppTheme theme, double fontScale)
    {
        Current = Normalize(new AppPreferences(theme, fontScale, Current.AutoBackupEnabled));
        ApplyTheme(Current.Theme);
        ApplyFontScale(Current.FontScale);
        _preferences.Save(Current);
        Changed?.Invoke(this, Current);
    }

    private static AppPreferences Normalize(AppPreferences preferences)
    {
        var allowed = new[] { 0.9, 1.0, 1.1, 1.25, 1.5 };
        var scale = allowed.MinBy(x => Math.Abs(x - preferences.FontScale));
        return preferences with { FontScale = scale };
    }

    private static void ApplyTheme(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(x =>
            x.Source?.OriginalString.Contains("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            x.Source?.OriginalString.Contains("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);

        var replacement = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Kazuko.Shared.UI;component/Themes/{theme}.xaml",
                UriKind.Absolute)
        };

        if (existing is null) dictionaries.Insert(0, replacement);
        else dictionaries[dictionaries.IndexOf(existing)] = replacement;
    }

    private static void ApplyFontScale(double scale)
    {
        foreach (var item in BaseFontSizes)
            Application.Current.Resources[item.Key] = item.Value * scale;
    }
}
