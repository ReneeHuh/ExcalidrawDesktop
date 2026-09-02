namespace ExcalidrawDesktop.Core;

public sealed class RecoverySnapshotStore
{
    private readonly string recoveryDirectory;

    public RecoverySnapshotStore(string recoveryDirectory)
    {
        this.recoveryDirectory = Path.GetFullPath(recoveryDirectory);
    }

    public async Task SaveAsync(string recoveryId, string content)
    {
        var snapshotPath = GetSnapshotPath(recoveryId);
        Directory.CreateDirectory(recoveryDirectory);
        var temporaryPath = Path.Combine(
            recoveryDirectory,
            $".{recoveryId}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content);
            File.Move(temporaryPath, snapshotPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

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
        if (File.Exists(snapshotPath))
        {
            File.Delete(snapshotPath);
        }
        return Task.CompletedTask;
    }

    public Task PruneExceptAsync(IEnumerable<string> retainedRecoveryIds)
    {
        if (!Directory.Exists(recoveryDirectory))
        {
            return Task.CompletedTask;
        }

        var retained = retainedRecoveryIds
            .Select(NormalizeRecoveryId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(recoveryDirectory, "*.excalidraw"))
        {
            if (!retained.Contains(Path.GetFileNameWithoutExtension(path)))
            {
                File.Delete(path);
            }
        }
        return Task.CompletedTask;
    }

    private string GetSnapshotPath(string recoveryId) => Path.Combine(
        recoveryDirectory,
        $"{NormalizeRecoveryId(recoveryId)}.excalidraw");

    private static string NormalizeRecoveryId(string recoveryId) =>
        Guid.TryParseExact(recoveryId, "N", out var parsed)
            ? parsed.ToString("N")
            : throw new ArgumentException("The recovery identifier is invalid.", nameof(recoveryId));
}
