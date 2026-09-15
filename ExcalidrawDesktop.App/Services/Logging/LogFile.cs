namespace ExcalidrawDesktop.App.Services.Logging;

internal static class LogFile
{
    public static FileStream Create(string directory, string prefix)
    {
        Directory.CreateDirectory(directory);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        for (var index = 0; index < 1000; index++)
        {
            var suffix = index == 0 ? string.Empty : $"_{index}";
            var path = Path.Combine(directory, $"{prefix}_{timestamp}{suffix}.txt");
            try
            {
                // Redirected launches and simultaneous crashes must not overwrite a log.
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }
        throw new IOException("Could not reserve a unique log filename.");
    }
}
