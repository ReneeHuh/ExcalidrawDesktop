using System.Text.Json;

namespace ExcalidrawDesktop.Core;

public sealed record WorkspaceWindowBounds(
    int X,
    int Y,
    int Width,
    int Height);

public sealed record WorkspaceWindowState(
    string Id,
    WorkspaceWindowBounds? Bounds,
    bool IsMaximized,
    string? ActiveRecoveryId,
    IReadOnlyList<WorkspaceTabState> Tabs);

public sealed record MultiWindowWorkspaceState(
    int Version,
    IReadOnlyList<string> RecentFiles,
    string? LastActiveWindowId,
    IReadOnlyList<WorkspaceWindowState> Windows)
{
    public const int CurrentVersion = 3;

    public static MultiWindowWorkspaceState Empty { get; } = new(
        CurrentVersion,
        Array.Empty<string>(),
        null,
        Array.Empty<WorkspaceWindowState>());
}

public sealed class MultiWindowWorkspaceStateStore
{
    public enum LoadStatus { Loaded, Missing, Corrupt, Unsupported, Inaccessible }
    public LoadStatus LastLoadStatus { get; private set; }
    private const int MaxRecentFiles = 100, MaxWindows = 50, MaxTabsPerWindow = 100;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string statePath;
    private byte[]? lastSavedPayload;

    public MultiWindowWorkspaceStateStore(string statePath)
    {
        this.statePath = Path.GetFullPath(statePath);
    }

    public async Task<MultiWindowWorkspaceState> LoadAsync()
    {
        lastSavedPayload = null;
        try
        {
            if (!File.Exists(statePath))
            {
                LastLoadStatus = LoadStatus.Missing;
                return MultiWindowWorkspaceState.Empty;
            }

            var payload = await File.ReadAllBytesAsync(statePath);
            using var document = JsonDocument.Parse(payload);
            if (!TryReadVersion(document.RootElement, out var version))
            {
                LastLoadStatus = LoadStatus.Corrupt;
                return MultiWindowWorkspaceState.Empty;
            }

            if (version == MultiWindowWorkspaceState.CurrentVersion)
            {
                var state = document.RootElement.Deserialize<MultiWindowWorkspaceState>(
                    SerializerOptions);
                if (IsValid(state))
                {
                    LastLoadStatus = LoadStatus.Loaded;
                    lastSavedPayload = payload;
                    return Normalize(state!);
                }
                LastLoadStatus = LoadStatus.Corrupt;
                return state is not null && IsStructurallyReadable(state)
                    ? Normalize(state) : MultiWindowWorkspaceState.Empty;
            }

            if (version == 1 || version == WorkspaceState.CurrentVersion)
            {
                var legacy = document.RootElement.Deserialize<WorkspaceState>(
                    SerializerOptions);
                LastLoadStatus = legacy is null ? LoadStatus.Corrupt : LoadStatus.Loaded;
                return legacy is null ? MultiWindowWorkspaceState.Empty : MigrateLegacy(legacy);
            }

            LastLoadStatus = LoadStatus.Unsupported;
            return MultiWindowWorkspaceState.Empty;
        }
        catch (JsonException)
        {
            LastLoadStatus = LoadStatus.Corrupt;
            return MultiWindowWorkspaceState.Empty;
        }
        catch (IOException)
        {
            LastLoadStatus = LoadStatus.Inaccessible;
            return MultiWindowWorkspaceState.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            LastLoadStatus = LoadStatus.Inaccessible;
            return MultiWindowWorkspaceState.Empty;
        }
    }

    public async Task SaveAsync(MultiWindowWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            Normalize(state with
            {
                Version = MultiWindowWorkspaceState.CurrentVersion,
            }),
            SerializerOptions);
        if (lastSavedPayload is not null &&
            lastSavedPayload.AsSpan().SequenceEqual(payload))
        {
            return;
        }

        await AtomicFile.WriteAllBytesAsync(statePath, payload);
        lastSavedPayload = payload;
    }

    private static bool TryReadVersion(JsonElement root, out int version)
    {
        version = 0;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("version", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out version))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsValid(MultiWindowWorkspaceState? state) =>
        state is
        {
            Version: MultiWindowWorkspaceState.CurrentVersion,
            RecentFiles: not null,
            Windows: not null,
        } &&
        state.RecentFiles.Count <= MaxRecentFiles &&
        state.RecentFiles.All(path => DesktopDocumentPath.Normalize(path) is not null) &&
        state.Windows.Count <= MaxWindows &&
        state.Windows.All(window =>
            window is not null && !string.IsNullOrWhiteSpace(window.Id) &&
            window.Tabs is not null && window.Tabs.Count <= MaxTabsPerWindow && window.Tabs.All(tab => tab is not null)) &&
        state.Windows.Select(window => window.Id).Distinct(
            StringComparer.OrdinalIgnoreCase).Count() == state.Windows.Count;

    private static bool IsStructurallyReadable(MultiWindowWorkspaceState state) =>
        state.RecentFiles is not null && state.RecentFiles.Count <= MaxRecentFiles &&
        state.RecentFiles.All(path => path is not null) &&
        state.Windows is not null && state.Windows.Count <= MaxWindows &&
        state.Windows.All(window => window is not null && window.Tabs is not null &&
            window.Tabs.Count <= MaxTabsPerWindow && window.Tabs.All(tab => tab is not null)) &&
        state.Windows.Select(window => window.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == state.Windows.Count;

    private static MultiWindowWorkspaceState Normalize(
        MultiWindowWorkspaceState state)
    {
        var usedRecoveryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var windows = (state.Windows ?? Array.Empty<WorkspaceWindowState>())
            .Where(window => window is not null)
            .Where(window => !string.IsNullOrWhiteSpace(window.Id))
            .GroupBy(window => window.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with
            {
                Tabs = SanitizeTabs(group.First().Tabs, usedRecoveryIds),
            })
            .ToArray();
        return state with
        {
            Version = MultiWindowWorkspaceState.CurrentVersion,
            RecentFiles = (state.RecentFiles ?? Array.Empty<string>()).Select(DesktopDocumentPath.Normalize).OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecentFiles).ToArray(),
            Windows = windows,
            LastActiveWindowId = windows.Any(window => string.Equals(
                window.Id,
                state.LastActiveWindowId,
                StringComparison.OrdinalIgnoreCase))
                    ? state.LastActiveWindowId
                    : windows.FirstOrDefault()?.Id,
        };
    }

    private static WorkspaceTabState[] SanitizeTabs(IEnumerable<WorkspaceTabState>? source, HashSet<string> used)
    {
        var result = new List<WorkspaceTabState>();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in source ?? Array.Empty<WorkspaceTabState>())
        {
            if (tab is null) continue;
            var path = DesktopDocumentPath.Normalize(tab.Path);
            if (path is not null && !usedPaths.Add(path))
            {
                if (!tab.WasDirty) continue;
                path = null;
            }
            var recovery = tab.RecoveryId is { } id && Guid.TryParse(id, out var parsed)
                ? parsed.ToString("N") : null;
            if (recovery is not null && !used.Add(recovery)) recovery = null;
            if (tab.WasDirty && recovery is null) continue;
            result.Add(tab with { Path = path, RecoveryId = recovery });
            if (result.Count == MaxTabsPerWindow) break;
        }
        return result.ToArray();
    }

    private static MultiWindowWorkspaceState MigrateLegacy(WorkspaceState legacy)
    {
        if (legacy.Tabs is null || legacy.RecentFiles is null ||
            legacy.Tabs.Any(tab => tab is null) ||
            legacy.RecentFiles.Any(string.IsNullOrWhiteSpace))
        {
            return MultiWindowWorkspaceState.Empty;
        }

        var tabs = legacy.Tabs.Select(tab => tab with
        {
            RecoveryId = tab.RecoveryId is { } recoveryId &&
                Guid.TryParseExact(recoveryId, "N", out _)
                    ? recoveryId
                    : Guid.NewGuid().ToString("N"),
        }).ToArray();
        const string migratedWindowId = "main";
        return new MultiWindowWorkspaceState(
            MultiWindowWorkspaceState.CurrentVersion,
            legacy.RecentFiles,
            migratedWindowId,
            new[]
            {
                new WorkspaceWindowState(
                    migratedWindowId,
                    null,
                    false,
                    legacy.ActiveRecoveryId ?? tabs.FirstOrDefault(tab =>
                        DesktopDocumentPath.Equals(tab.Path, legacy.ActivePath))?.RecoveryId,
                    tabs),
            });
    }
}
