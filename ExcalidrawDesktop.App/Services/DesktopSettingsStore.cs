using ExcalidrawDesktop.App.Models;
using System.Text.Json;

namespace ExcalidrawDesktop.App.Services;

internal sealed class DesktopSettingsStore
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string settingsPath;

    public DesktopSettingsStore(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? DesktopPaths.SettingsPath;
    }

    public DesktopPreferences Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return DesktopPreferences.Default;
            }

            var document = JsonSerializer.Deserialize<SettingsDocument>(
                File.ReadAllText(settingsPath),
                JsonOptions);
            if (document is not { Version: CurrentVersion } ||
                document.Preferences is not { } preferences)
            {
                return DesktopPreferences.Default;
            }

            return preferences with
            {
                Language = ExcalidrawDesktop.Core.DesktopLanguages
                    .NormalizePreference(preferences.Language),
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagnosticLogService.Error("settings.load_failed", exception);
            return DesktopPreferences.Default;
        }
    }

    /// <summary>
    /// Persists the preferences. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the settings file cannot
    /// be written; callers decide how to surface that.
    /// </summary>
    public void Save(DesktopPreferences preferences) =>
        ExcalidrawDesktop.Core.AtomicFile.WriteAllText(
            settingsPath,
            JsonSerializer.Serialize(
                new SettingsDocument(CurrentVersion, preferences),
                JsonOptions));

    private sealed record SettingsDocument(
        int Version,
        DesktopPreferences Preferences);
}
