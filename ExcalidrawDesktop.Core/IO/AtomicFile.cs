namespace ExcalidrawDesktop.Core;

/// <summary>
/// Writes files so that readers only ever observe the previous complete
/// content or the new complete content, never a partially written file.
/// </summary>
public static class AtomicFile
{
    public static async Task WriteAllBytesAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var (directory, temporaryPath) = PrepareTemporaryPath(path);
        Directory.CreateDirectory(directory);
        try
        {
            await using (var stream = OpenTemporary(temporaryPath, useAsync: true))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            DeleteTemporary(temporaryPath);
        }
    }

    public static Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default) =>
        WriteAllBytesAsync(
            path,
            System.Text.Encoding.UTF8.GetBytes(content),
            cancellationToken);

    public static void WriteAllBytes(string path, ReadOnlySpan<byte> content)
    {
        var (directory, temporaryPath) = PrepareTemporaryPath(path);
        Directory.CreateDirectory(directory);
        try
        {
            using (var stream = OpenTemporary(temporaryPath, useAsync: false))
            {
                stream.Write(content);
                stream.Flush();
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            DeleteTemporary(temporaryPath);
        }
    }

    public static void WriteAllText(string path, string content) =>
        WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(content));

    private static (string Directory, string TemporaryPath) PrepareTemporaryPath(
        string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
            throw new ArgumentException(
                "The target path has no directory.",
                nameof(path));
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        return (directory, temporaryPath);
    }

    private static FileStream OpenTemporary(string temporaryPath, bool useAsync) =>
        new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough |
                (useAsync ? FileOptions.Asynchronous : FileOptions.None));

    private static void DeleteTemporary(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
            // The rename already succeeded or the file is gone; nothing to clean.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
