using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ExcalidrawDesktop.App.Services;

internal static class DesktopResources
{
    private static readonly ResourceManager Manager = new();
    private static readonly ResourceMap Resources =
        Manager.MainResourceMap.GetSubtree("Resources");
    private static ResourceContext? context;

    public static void ConfigureLanguage(string languageTag)
    {
        var culture = CultureInfo.GetCultureInfo(languageTag);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var resourceContext = Manager.CreateResourceContext();
        resourceContext.QualifierValues["Language"] = languageTag;
        context = resourceContext;
    }

    public static string Get(string key, string fallback)
    {
        try
        {
            // PRI stores XAML property resources (for example
            // "FileMenu.Title") with a slash separator.
            var resourceKey = key.Contains('.')
                ? key.Replace('.', '/')
                : key;
            var value = context is null
                ? Resources.GetValue(resourceKey).ValueAsString
                : Resources.GetValue(resourceKey, context).ValueAsString;
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    public static string Format(string key, string fallback, params object[] args) =>
        string.Format(
            CultureInfo.CurrentUICulture,
            Get(key, fallback),
            args);
}
