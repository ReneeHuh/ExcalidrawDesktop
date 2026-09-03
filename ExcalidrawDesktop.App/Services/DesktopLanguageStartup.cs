using System.Runtime.InteropServices;
using ExcalidrawDesktop.Core;
using Windows.Globalization;
using Windows.System.UserProfile;

namespace ExcalidrawDesktop.App.Services;

internal static class DesktopLanguageStartup
{
    public static void ApplyBeforeXaml(string preference)
    {
        var normalized = DesktopLanguages.NormalizePreference(preference);
        if (!string.Equals(
            normalized,
            DesktopLanguages.SystemPreference,
            StringComparison.OrdinalIgnoreCase))
        {
            ApplicationLanguages.PrimaryLanguageOverride =
                DesktopLanguages.Resolve(normalized).WinUiTag;
            return;
        }

        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
        }
        catch (Exception exception) when (
            exception is COMException or ArgumentException)
        {
            // Some Windows App SDK runtime combinations reject an empty reset.
            // Re-resolve the real user language list on every launch so the
            // system preference still follows Windows without blocking startup.
            ApplicationLanguages.PrimaryLanguageOverride =
                ResolveEffective(DesktopLanguages.SystemPreference).WinUiTag;
        }
    }

    public static DesktopLanguage ResolveEffective(string preference)
    {
        var normalized = DesktopLanguages.NormalizePreference(preference);
        return string.Equals(
            normalized,
            DesktopLanguages.SystemPreference,
            StringComparison.OrdinalIgnoreCase)
                ? DesktopLanguages.Resolve(
                    DesktopLanguages.SystemPreference,
                    GlobalizationPreferences.Languages)
                : DesktopLanguages.Resolve(normalized);
    }
}
