namespace ExcalidrawDesktop.Core;

public sealed record WorkspaceTabState(
    string? Path,
    bool WasDirty,
    string? RecoveryId = null,
    string? DisplayName = null,
    DateTimeOffset? RecoveryUpdatedAt = null);

public sealed record WorkspaceState(
    int Version,
    IReadOnlyList<WorkspaceTabState> Tabs,
    string? ActivePath,
    IReadOnlyList<string> RecentFiles,
    string? ActiveRecoveryId = null)
{
    public const int CurrentVersion = 2;

    public static WorkspaceState Empty { get; } = new(
        CurrentVersion,
        Array.Empty<WorkspaceTabState>(),
        null,
        Array.Empty<string>());
}
