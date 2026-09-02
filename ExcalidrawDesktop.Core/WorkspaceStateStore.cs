using System.Text.Json;

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

public sealed class WorkspaceStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string statePath;

    public WorkspaceStateStore(string statePath)
    {
        this.statePath = Path.GetFullPath(statePath);
    }

    public async Task<WorkspaceState> LoadAsync()
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return WorkspaceState.Empty;
            }

            await using var stream = File.OpenRead(statePath);
            var state = await JsonSerializer.DeserializeAsync<WorkspaceState>(
                stream,
                SerializerOptions);
            if (state is
            {
                Version: WorkspaceState.CurrentVersion,
                Tabs: not null,
                RecentFiles: not null,
            })
            {
                return state;
            }

            if (state is { Version: 1, Tabs: not null, RecentFiles: not null })
            {
                return state with
                {
                    Version = WorkspaceState.CurrentVersion,
                    Tabs = state.Tabs
                        .Select(tab => tab with
                        {
                            RecoveryId = tab.RecoveryId ?? Guid.NewGuid().ToString("N"),
                        })
                        .ToArray(),
                };
            }

            return WorkspaceState.Empty;
        }
        catch (JsonException)
        {
            return WorkspaceState.Empty;
        }
        catch (IOException)
        {
            return WorkspaceState.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return WorkspaceState.Empty;
        }
    }

    public async Task SaveAsync(WorkspaceState state)
    {
        var directory = Path.GetDirectoryName(statePath) ??
            throw new InvalidOperationException("The workspace state path has no directory.");
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
                    state with { Version = WorkspaceState.CurrentVersion },
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
}
