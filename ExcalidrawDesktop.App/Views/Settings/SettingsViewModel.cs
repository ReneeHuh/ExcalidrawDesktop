using System.Reflection;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.ViewModels;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App.Views.Settings;

public sealed record SettingsLanguageOption(string Tag, string Name);

internal sealed class PreferencesChangedEventArgs(DesktopPreferences preferences) : EventArgs
{
    public DesktopPreferences Preferences { get; } = preferences;
}

/// <summary>Editable preferences for one window, synchronized by the shared workspace.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private DesktopPreferences preferences = DesktopPreferences.Default;
    private string startupLanguagePreference = DesktopLanguages.SystemPreference;
    private string effectiveLanguageName = string.Empty;
    private bool persistenceFailed;

    internal event EventHandler<PreferencesChangedEventArgs>? PreferencesChanged;
    internal event EventHandler? SettingsPersistenceRetryRequested;

    public IReadOnlyList<SettingsLanguageOption> Languages { get; } =
    [
        new(DesktopLanguages.SystemPreference, DesktopResources.Get("SystemLanguageOption", "Use system language")),
        .. DesktopLanguages.Supported.Select(language => new SettingsLanguageOption(language.PreferenceTag, language.NativeName)),
    ];

    public string VersionText { get; } = GetVersionText();

    public int ThemeIndex
    {
        get => preferences.Theme switch { DesktopThemePreference.Light => 1, DesktopThemePreference.Dark => 2, _ => 0 };
        set => Update(preferences with { Theme = value switch { 1 => DesktopThemePreference.Light, 2 => DesktopThemePreference.Dark, _ => DesktopThemePreference.System } });
    }

    public SettingsLanguageOption SelectedLanguage
    {
        get => Languages.FirstOrDefault(language => string.Equals(language.Tag,
            DesktopLanguages.NormalizePreference(preferences.Language), StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
        set
        {
            if (value is not null) Update(preferences with { Language = value.Tag });
        }
    }

    public bool ReopenSavedTabs
    {
        get => preferences.ReopenSavedTabs;
        set => Update(preferences with { ReopenSavedTabs = value });
    }

    public bool SaveDirtyDrawingsOnClose
    {
        get => preferences.SaveDirtyDrawingsOnClose;
        set => Update(preferences with { SaveDirtyDrawingsOnClose = value });
    }

    public bool SuspendInactiveTabs
    {
        get => preferences.SuspendInactiveTabs;
        set => Update(preferences with { SuspendInactiveTabs = value });
    }

    public bool UnloadInactiveTabs
    {
        get => preferences.UnloadInactiveTabs;
        set => Update(preferences with { UnloadInactiveTabs = value });
    }

    public bool PersistenceFailed => persistenceFailed;
    public string RestartTitle => DesktopResources.Get("LanguageRestartInfoBar.Title", "Restart required");
    public bool RestartRequired => !persistenceFailed && !string.Equals(
        DesktopLanguages.NormalizePreference(preferences.Language),
        DesktopLanguages.NormalizePreference(startupLanguagePreference), StringComparison.OrdinalIgnoreCase);
    public string RestartMessage => DesktopResources.Format(
        "LanguageRestartMessageFormat",
        "The application is currently using {0}. Restart Excalidraw Desktop to apply the selected language. Unsaved drawings will not be closed automatically.",
        effectiveLanguageName);

    internal void LoadPreferences(DesktopPreferences value, DesktopLanguage effectiveLanguage, string startupLanguage)
    {
        preferences = value;
        effectiveLanguageName = effectiveLanguage.NativeName;
        startupLanguagePreference = startupLanguage;
        // Loading another window's changes must never write preferences back.
        OnPropertyChanged(string.Empty);
    }

    internal void ShowSettingsPersistenceFailure() => SetPersistenceFailure(true);
    internal void ClearSettingsPersistenceFailure() => SetPersistenceFailure(false);

    public void RetryPersistence() => SettingsPersistenceRetryRequested?.Invoke(this, EventArgs.Empty);

    private void Update(DesktopPreferences value)
    {
        if (preferences == value) return;
        preferences = value;
        OnPropertyChanged(string.Empty);
        PreferencesChanged?.Invoke(this, new PreferencesChangedEventArgs(value));
    }

    private void SetPersistenceFailure(bool value)
    {
        if (!SetProperty(ref persistenceFailed, value, nameof(PersistenceFailed))) return;
        OnPropertyChanged(nameof(RestartRequired));
    }

    private static string GetVersionText()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
        return DesktopResources.Format("VersionFormat", "Version {0}.{1}.{2}.{3}",
            version.Major, version.Minor, version.Build, version.Revision);
    }
}
