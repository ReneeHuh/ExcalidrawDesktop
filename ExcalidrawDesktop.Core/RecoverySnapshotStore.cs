namespace ExcalidrawDesktop.Core;

public sealed class RecoverySnapshotStore
{
    private readonly string recoveryDirectory;

    public RecoverySnapshotStore(string recoveryDirectory)
    {
        this.recoveryDirectory = Path.GetFullPath(recoveryDirectory);
    }

    public Task SaveAsync(string recoveryId, string content) =>
        AtomicFile.WriteAllTextAsync(GetSnapshotPath(recoveryId), content);

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
        return Task.Run(() =>
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        });
    }

    public Task PruneExceptAsync(IEnumerable<string> retainedRecoveryIds)
    {
        var retained = retainedRecoveryIds
            .Select(NormalizeRecoveryId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.Run(() =>
        {
            if (!Directory.Exists(recoveryDirectory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(recoveryDirectory, "*.excalidraw"))
            {
                if (!retained.Contains(Path.GetFileNameWithoutExtension(path)))
                {
                    File.Delete(path);
                }
            }
        });
    }

    private string GetSnapshotPath(string recoveryId) => Path.Combine(
        recoveryDirectory,
        $"{NormalizeRecoveryId(recoveryId)}.excalidraw");

    private static string NormalizeRecoveryId(string recoveryId) =>
        Guid.TryParseExact(recoveryId, "N", out var parsed)
            ? parsed.ToString("N")
            : throw new ArgumentException("The recovery identifier is invalid.", nameof(recoveryId));
}
