namespace ExcalidrawDesktop.Core;

public enum ExternalFileState
{
    None,
    Modified,
    Moved,
    Deleted,
    Unavailable,
    UnknownBaseline,
}

public static class ExternalFileCheck
{
    public static async Task<ExternalFileState> CheckAsync(
        string path, DesktopFileStamp? expectedStamp, string? expectedHash, bool compareContent = true)
    {
        try
        {
            // File.Exists collapses access errors into false. Read metadata instead.
            var stamp = ExactFileBaseline.ReadStamp(path);
            if (expectedStamp is null || string.IsNullOrWhiteSpace(expectedHash))
                return ExternalFileState.UnknownBaseline;
            if (expectedStamp.DiffersFrom(stamp)) return ExternalFileState.Modified;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > ExactFileBaseline.DefaultMaxBytes) return ExternalFileState.Unavailable;
            // Save preflights still reject missing baselines, changed stamps and
            // inaccessible files. Only the transaction may authorize the write
            // by comparing the expected hash while holding write protection.
            if (!compareContent)
                return expectedStamp.DiffersFrom(ExactFileBaseline.ReadStamp(path))
                    ? ExternalFileState.Modified : ExternalFileState.None;
            var hash = await RecoveryFileBaseline.ComputeContentHashAsync(stream);
            return string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase) &&
                !expectedStamp.DiffersFrom(ExactFileBaseline.ReadStamp(path))
                ? ExternalFileState.None : ExternalFileState.Modified;
        }
        catch (FileNotFoundException) { return ExternalFileState.Deleted; }
        catch (DirectoryNotFoundException) { return ExternalFileState.Deleted; }
        catch (IOException) { return ExternalFileState.Unavailable; }
        catch (UnauthorizedAccessException) { return ExternalFileState.Unavailable; }
    }
}
