namespace ExcalidrawDesktop.Core;

public sealed class RecoverySnapshotStore
{
    private readonly string recoveryDirectory;
    private readonly SemaphoreSlim fileGate = new(1, 1);

    public RecoverySnapshotStore(string recoveryDirectory)
    {
        this.recoveryDirectory = Path.GetFullPath(recoveryDirectory);
    }

    public async Task SaveAsync(string recoveryId, string content)
    {
        await fileGate.WaitAsync();
        try { await AtomicFile.WriteAllTextAsync(GetSnapshotPath(recoveryId), content); }
        finally { fileGate.Release(); }
    }

    public Task SaveAsync(string recoveryId, string content, RecoveryFileBaseline? baseline) =>
        SaveAsync(recoveryId, RecoveryFileBaseline.Attach(content, baseline));

    public async Task<string?> LoadAsync(string recoveryId)
    {
        var snapshotPath = GetSnapshotPath(recoveryId);
        try
        {
            return File.Exists(snapshotPath)
                ? await File.ReadAllTextAsync(snapshotPath)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Task DeleteAsync(string recoveryId)
    {
        var snapshotPath = GetSnapshotPath(recoveryId);
        return Task.Run(async () =>
        {
            await fileGate.WaitAsync();
            try
            {
                if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
            }
            finally { fileGate.Release(); }
        });
    }

    public Task PruneExceptAsync(IEnumerable<string> retainedRecoveryIds)
        => PruneExceptAsync(() => retainedRecoveryIds);

    public async Task PruneExceptAsync(Func<IEnumerable<string>> retainedRecoveryIds)
    {
        await fileGate.WaitAsync();
        try
        {
            var retained = retainedRecoveryIds()
                .Select(TryNormalizeRecoveryId)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            await Task.Run(() =>
            {
                if (!Directory.Exists(recoveryDirectory)) return;
                foreach (var path in Directory.EnumerateFiles(recoveryDirectory, "*.excalidraw"))
                {
                    if (!retained.Contains(Path.GetFileNameWithoutExtension(path)))
                    {
                        try { File.Delete(path); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            });
        }
        finally { fileGate.Release(); }
    }

    /// <summary>Returns snapshot identities found on disk, including orphaned ones.</summary>
    public IReadOnlyList<string> DiscoverSnapshotIds()
    {
        try
        {
            // Directory.Exists also returns false for access errors. Only a
            // genuinely missing directory is safe to treat as no recoveries.
            return Directory.EnumerateFiles(recoveryDirectory, "*.excalidraw")
                .Select(path => TryNormalizeRecoveryId(Path.GetFileNameWithoutExtension(path)))
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (DirectoryNotFoundException) { return Array.Empty<string>(); }
    }

    private string GetSnapshotPath(string recoveryId) => Path.Combine(
        recoveryDirectory,
        $"{NormalizeRecoveryId(recoveryId)}.excalidraw");

    private static string NormalizeRecoveryId(string recoveryId) =>
        Guid.TryParseExact(recoveryId, "N", out var parsed)
            ? parsed.ToString("N")
            : throw new ArgumentException("The recovery identifier is invalid.", nameof(recoveryId));

    private static string? TryNormalizeRecoveryId(string? recoveryId) =>
        recoveryId is null ? null : Guid.TryParseExact(recoveryId, "N", out var parsed)
            ? parsed.ToString("N") : null;
}
