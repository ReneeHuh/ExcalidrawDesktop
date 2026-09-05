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

    public void Save(DesktopPreferences preferences)
    {
        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    new SettingsDocument(CurrentVersion, preferences),
                    JsonOptions));
            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record SettingsDocument(
        int Version,
        DesktopPreferences Preferences);
}
