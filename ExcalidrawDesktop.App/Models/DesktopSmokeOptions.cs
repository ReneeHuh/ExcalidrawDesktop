namespace ExcalidrawDesktop.App.Models;

/// <summary>
/// Debug-build smoke-test scenario switches resolved once at launch. Release
/// builds always use <see cref="None"/>.
/// </summary>
internal sealed record DesktopSmokeOptions(
    bool RunTabSmoke = false,
    string? WorkspaceStatePath = null,
    bool RunRecoverySmoke = false,
    bool VerifyRecoverySmoke = false,
    bool RunTitleBarSmoke = false,
    int PerformanceTabCount = 0,
    bool RunSuspensionSmoke = false,
    bool PerformanceSuspendInactive = false,
    bool PerformanceUnloadInactive = false,
    bool RunMultiWindowSmoke = false,
    bool RunMultiWindowExitSmoke = false,
    bool RunMultiWindowDirtyExitSmoke = false,
    bool RunImageExportSmoke = false,
    bool RunDocumentSafetySmoke = false,
    bool RunCloseDecisionsSmoke = false,
    bool RunLocalizationSmoke = false,
    string? LocalizationSmokeLanguage = null)
{
    public static DesktopSmokeOptions None { get; } = new();

    /// <summary>
    /// Scenarios whose editor pages must expose the in-page smoke API.
    /// </summary>
    public bool RequiresEditorSmokeApi =>
        RunTabSmoke || RunMultiWindowSmoke || RunSuspensionSmoke ||
        RunRecoverySmoke || VerifyRecoverySmoke ||
        RunDocumentSafetySmoke || RunCloseDecisionsSmoke;
}
