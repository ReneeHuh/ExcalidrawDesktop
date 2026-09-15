using System.Globalization;

namespace ExcalidrawDesktop.Core;

public sealed record DesktopLanguage(
    string PreferenceTag,
    string WinUiTag,
    string ExcalidrawCode,
    string NativeName,
    bool IsRightToLeft)
{
    public string Direction => IsRightToLeft ? "rtl" : "ltr";
}

public static class DesktopLanguages
{
    public const string SystemPreference = "system";
    public const string EnglishPreference = "en-US";

    public static IReadOnlyList<DesktopLanguage> Supported { get; } =
    [
        new("en-US", "en-US", "en", "English", IsRightToLeft: false),
        new("es-ES", "es-ES", "es-ES", "Español", IsRightToLeft: false),
        new("fr-FR", "fr-FR", "fr-FR", "Français", IsRightToLeft: false),
        new("de-DE", "de-DE", "de-DE", "Deutsch", IsRightToLeft: false),
        new("pt-BR", "pt-BR", "pt-BR", "Português (Brasil)", IsRightToLeft: false),
        new("ja-JP", "ja-JP", "ja-JP", "日本語", IsRightToLeft: false),
        new("zh-CN", "zh-CN", "zh-CN", "简体中文", IsRightToLeft: false),
        new("ar-SA", "ar-SA", "ar-SA", "العربية", IsRightToLeft: true),
    ];

    public static string NormalizePreference(string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference) ||
            string.Equals(preference, SystemPreference, StringComparison.OrdinalIgnoreCase))
        {
            return SystemPreference;
        }

        return FindExact(preference)?.PreferenceTag ?? EnglishPreference;
    }

    public static DesktopLanguage Resolve(
        string? preference,
        IEnumerable<string>? preferredLanguages = null)
    {
        var normalized = NormalizePreference(preference);
        if (!string.Equals(
            normalized,
            SystemPreference,
            StringComparison.OrdinalIgnoreCase))
        {
            return FindExact(normalized) ?? English;
        }

        var candidates = preferredLanguages?.Where(value =>
                !string.IsNullOrWhiteSpace(value)) ??
            [CultureInfo.CurrentUICulture.Name];
        foreach (var candidate in candidates)
        {
            if (FindBestMatch(candidate) is { } match)
            {
                return match;
            }
        }

        return English;
    }

    private static DesktopLanguage English =>
        Supported.First(language => language.PreferenceTag == EnglishPreference);

    private static DesktopLanguage? FindExact(string tag) =>
        Supported.FirstOrDefault(language =>
            string.Equals(language.PreferenceTag, tag, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language.WinUiTag, tag, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language.ExcalidrawCode, tag, StringComparison.OrdinalIgnoreCase));

    private static DesktopLanguage? FindBestMatch(string tag)
    {
        if (FindExact(tag) is { } exact)
        {
            return exact;
        }

        var normalized = tag.Replace('_', '-');
        if (FindExact(normalized) is { } normalizedExact)
        {
            return normalizedExact;
        }

        string language;
        try
        {
            language = CultureInfo.GetCultureInfo(normalized).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            language = normalized.Split('-', 2)[0];
        }

        return Supported.FirstOrDefault(candidate =>
            candidate.PreferenceTag.StartsWith(
                $"{language}-",
                StringComparison.OrdinalIgnoreCase));
    }
}
