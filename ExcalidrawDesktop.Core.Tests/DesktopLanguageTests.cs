using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Core.Tests;

public sealed class DesktopLanguageTests
{
    [Fact]
    public void SystemPreferenceUsesTheFirstSupportedWindowsLanguage()
    {
        var resolved = DesktopLanguages.Resolve(
            DesktopLanguages.SystemPreference,
            ["it-IT", "fr-CA", "de-DE"]);

        Assert.Equal("fr-FR", resolved.WinUiTag);
        Assert.Equal("fr-FR", resolved.ExcalidrawCode);
        Assert.False(resolved.IsRightToLeft);
    }

    [Theory]
    [InlineData("es-MX", "es-ES")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("ar-EG", "ar-SA")]
    public void SystemPreferenceFallsBackToASupportedRegionalVariant(
        string preferred,
        string expected)
    {
        Assert.Equal(
            expected,
            DesktopLanguages.Resolve("system", [preferred]).PreferenceTag);
    }

    [Fact]
    public void ExplicitArabicUsesTheRtlExcalidrawLocale()
    {
        var resolved = DesktopLanguages.Resolve("ar-SA", ["en-US"]);

        Assert.Equal("ar-SA", resolved.ExcalidrawCode);
        Assert.Equal("rtl", resolved.Direction);
    }

    [Fact]
    public void InvalidExplicitPreferenceFallsBackToEnglish()
    {
        Assert.Equal(
            DesktopLanguages.EnglishPreference,
            DesktopLanguages.NormalizePreference("not-a-locale"));
        Assert.Equal(
            "en",
            DesktopLanguages.Resolve("not-a-locale", ["fr-FR"]).ExcalidrawCode);
    }

    [Fact]
    public void SupportedMappingsAreUniqueAndComplete()
    {
        Assert.Equal(
            DesktopLanguages.Supported.Count,
            DesktopLanguages.Supported.Select(value => value.PreferenceTag).Distinct(
                StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(DesktopLanguages.Supported, language =>
        {
            Assert.False(string.IsNullOrWhiteSpace(language.WinUiTag));
            Assert.False(string.IsNullOrWhiteSpace(language.ExcalidrawCode));
            Assert.False(string.IsNullOrWhiteSpace(language.NativeName));
        });
    }
}
