namespace Kazuko.Core.Appearance;

public enum AppTheme
{
    Light,
    Dark
}

public sealed record AppPreferences(AppTheme Theme = AppTheme.Light, double FontScale = 1.0);

public interface IPreferencesService
{
    AppPreferences Load();
    void Save(AppPreferences preferences);
}

public interface IAppearanceService
{
    AppPreferences Current { get; }
    event EventHandler<AppPreferences>? Changed;
    void Apply(AppTheme theme, double fontScale);
}
