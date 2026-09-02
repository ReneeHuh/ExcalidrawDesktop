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
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string statePath;

    public MultiWindowWorkspaceStateStore(string statePath)
    {
        this.statePath = Path.GetFullPath(statePath);
    }

    public async Task<MultiWindowWorkspaceState> LoadAsync()
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return MultiWindowWorkspaceState.Empty;
            }

            await using var stream = File.OpenRead(statePath);
            using var document = await JsonDocument.ParseAsync(stream);
            if (!TryReadVersion(document.RootElement, out var version))
            {
                return MultiWindowWorkspaceState.Empty;
            }

            if (version == MultiWindowWorkspaceState.CurrentVersion)
            {
                var state = document.RootElement.Deserialize<MultiWindowWorkspaceState>(
                    SerializerOptions);
                return IsValid(state)
                    ? Normalize(state!)
                    : MultiWindowWorkspaceState.Empty;
            }

            if (version is 1 or WorkspaceState.CurrentVersion)
            {
                var legacy = document.RootElement.Deserialize<WorkspaceState>(
                    SerializerOptions);
                return legacy is null
                    ? MultiWindowWorkspaceState.Empty
                    : MigrateLegacy(legacy);
            }

            return MultiWindowWorkspaceState.Empty;
        }
        catch (JsonException)
        {
            return MultiWindowWorkspaceState.Empty;
        }
        catch (IOException)
        {
            return MultiWindowWorkspaceState.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return MultiWindowWorkspaceState.Empty;
        }
    }

    public async Task SaveAsync(MultiWindowWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(statePath) ??
            throw new InvalidOperationException(
                "The workspace state path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(statePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    Normalize(state with
                    {
                        Version = MultiWindowWorkspaceState.CurrentVersion,
                    }),
                    SerializerOptions);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryReadVersion(JsonElement root, out int version)
    {
        version = 0;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("version", StringComparison.OrdinalIgnoreCase) &&
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
        state.Windows.All(window =>
            !string.IsNullOrWhiteSpace(window.Id) && window.Tabs is not null) &&
        state.Windows.Select(window => window.Id).Distinct(
            StringComparer.OrdinalIgnoreCase).Count() == state.Windows.Count;

    private static MultiWindowWorkspaceState Normalize(
        MultiWindowWorkspaceState state)
    {
        var windows = state.Windows
            .Where(window => !string.IsNullOrWhiteSpace(window.Id))
            .GroupBy(window => window.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with
            {
                Tabs = group.First().Tabs ?? Array.Empty<WorkspaceTabState>(),
            })
            .ToArray();
        return state with
        {
            Version = MultiWindowWorkspaceState.CurrentVersion,
            RecentFiles = state.RecentFiles ?? Array.Empty<string>(),
            Windows = windows,
            LastActiveWindowId = windows.Any(window => string.Equals(
                window.Id,
                state.LastActiveWindowId,
                StringComparison.OrdinalIgnoreCase))
                    ? state.LastActiveWindowId
                    : windows.FirstOrDefault()?.Id,
        };
    }

    private static MultiWindowWorkspaceState MigrateLegacy(WorkspaceState legacy)
    {
        if (legacy.Tabs is null || legacy.RecentFiles is null)
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
