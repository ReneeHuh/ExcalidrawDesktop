using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace ExcalidrawDesktop.App.Services;

public sealed record DocumentOpenResult(string Status, string? FileName = null, string? Content = null)
{
    public static DocumentOpenResult Cancelled { get; } = new("cancelled");
}

public sealed record PickedDocument(
    StorageFile File,
    string CanonicalPath,
    string FileName,
    string Content,
    DesktopFileStamp Stamp);

public enum ExternalFileState
{
    None,
    Modified,
    Moved,
    Deleted,
}

public sealed record DocumentSaveResult(string Status, string? FileName = null)
{
    public static DocumentSaveResult Cancelled { get; } = new("cancelled");
}

public sealed record DocumentNewResult(string Status)
{
    public static DocumentNewResult Cancelled { get; } = new("cancelled");
    public static DocumentNewResult Created { get; } = new("created");
}

public enum CloseDecision
{
    Save,
    Discard,
    Cancel,
}

public sealed class DocumentService
{
    public const ulong MaxDocumentBytes = 50UL * 1024 * 1024;

    private Window window;
    private FrameworkElement dialogRoot;
    private StorageFile? activeFile;
    private StorageFile? pendingOpenFile;
    private DesktopFileStamp? activeFileStamp;
    private DesktopFileStamp? pendingOpenFileStamp;
    private string? activeCanonicalPath;
    private string? pendingCanonicalPath;
    private bool pendingNewDocument;

    public Func<string, bool>? IsPathOwnedByAnotherSession { get; set; }

    public string? DocumentPath => activeCanonicalPath ?? pendingCanonicalPath;

    public bool HasActiveFile => activeFile is not null;

    public DocumentService(Window window, FrameworkElement dialogRoot)
    {
        this.window = window;
        this.dialogRoot = dialogRoot;
    }

    public void AttachHost(Window window, FrameworkElement dialogRoot)
    {
        this.window = window;
        this.dialogRoot = dialogRoot;
    }

    public async Task<DocumentOpenResult> OpenAsync(bool hasUnsavedChanges)
    {
        pendingOpenFile = null;
        pendingOpenFileStamp = null;
        pendingCanonicalPath = null;
        pendingNewDocument = false;

        if (hasUnsavedChanges && !await ConfirmDiscardChangesAsync(
            "Opening another drawing will replace the current unsaved drawing.",
            "Discard and open"))
        {
            return DocumentOpenResult.Cancelled;
        }

        var pickedDocument = await PickOpenDocumentAsync();
        if (pickedDocument is null)
        {
            return DocumentOpenResult.Cancelled;
        }

        StageOpen(pickedDocument);
        return new DocumentOpenResult(
            "opened",
            pickedDocument.FileName,
            pickedDocument.Content);
    }

    public async Task<PickedDocument?> PickOpenDocumentAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".excalidraw");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return null;
        }

        return await ReadDocumentAsync(file);
    }

    public async Task<PickedDocument> OpenPathAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(
                DesktopDocumentPath.Normalize(path) ?? path);
            return await ReadDocumentAsync(file);
        }
        catch (BridgeProtocolException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            throw new BridgeProtocolException(
                "DocumentNotFound",
                "The drawing no longer exists at that location.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new BridgeProtocolException(
                "DocumentAccessDenied",
                "Excalidraw Desktop does not have permission to read that drawing.");
        }
        catch (IOException)
        {
            throw new BridgeProtocolException(
                "DocumentReadFailed",
                "The drawing could not be read.");
        }
    }

    private static async Task<PickedDocument> ReadDocumentAsync(StorageFile file)
    {
        try
        {
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > MaxDocumentBytes)
            {
                throw new BridgeProtocolException(
                    "DocumentTooLarge",
                    "The selected drawing exceeds the 50 MB desktop document limit.");
            }

            var content = await FileIO.ReadTextAsync(
                file,
                Windows.Storage.Streams.UnicodeEncoding.Utf8);
            ExcalidrawDocumentValidator.Validate(content);
            var canonicalPath = DesktopDocumentPath.Normalize(file.Path) ??
                throw new BridgeProtocolException(
                    "DocumentPathUnavailable",
                    "The selected drawing does not have a local file path.");
            return new PickedDocument(
                file,
                canonicalPath,
                file.Name,
                content,
                ReadFileStamp(file, properties));
        }
        catch (BridgeProtocolException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw new BridgeProtocolException(
                "DocumentAccessDenied",
                "Excalidraw Desktop does not have permission to read the selected file.");
        }
        catch (IOException)
        {
            throw new BridgeProtocolException(
                "DocumentReadFailed",
                "The selected drawing could not be read.");
        }
    }

    public void StageOpen(PickedDocument document)
    {
        activeFile = document.File;
        activeFileStamp = document.Stamp;
        activeCanonicalPath = document.CanonicalPath;
        pendingOpenFile = document.File;
        pendingOpenFileStamp = document.Stamp;
        pendingCanonicalPath = document.CanonicalPath;
        pendingNewDocument = false;
    }

    public async Task RestoreActiveFileAsync(string? path)
    {
        activeFile = null;
        activeFileStamp = null;
        activeCanonicalPath = null;
        pendingOpenFile = null;
        pendingOpenFileStamp = null;
        pendingCanonicalPath = null;
        pendingNewDocument = false;
        var canonicalPath = DesktopDocumentPath.Normalize(path);
        if (canonicalPath is null || !File.Exists(canonicalPath))
        {
            return;
        }

        try
        {
            activeFile = await StorageFile.GetFileFromPathAsync(canonicalPath);
            activeCanonicalPath = canonicalPath;
            var properties = await activeFile.GetBasicPropertiesAsync();
            activeFileStamp = ReadFileStamp(activeFile, properties);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or
                UnauthorizedAccessException or
                IOException)
        {
            activeFile = null;
            activeFileStamp = null;
            activeCanonicalPath = null;
        }
    }

    public bool ConfirmOpened(string fileName)
    {
        if (pendingOpenFile is null ||
            !string.Equals(pendingOpenFile.Name, fileName, StringComparison.Ordinal))
        {
            return false;
        }

        pendingOpenFile = null;
        pendingOpenFileStamp = null;
        pendingCanonicalPath = null;
        return true;
    }

    public async Task<string?> ReadActiveContentForHibernationAsync()
    {
        if (activeFile is null)
        {
            return null;
        }

        var properties = await activeFile.GetBasicPropertiesAsync();
        if (properties.Size > MaxDocumentBytes)
        {
            throw new BridgeProtocolException(
                "DocumentTooLarge",
                "The drawing exceeds the 50 MB desktop document limit.");
        }

        var content = await FileIO.ReadTextAsync(
            activeFile,
            Windows.Storage.Streams.UnicodeEncoding.Utf8);
        ExcalidrawDocumentValidator.Validate(content);
        return content;
    }

    public bool StageActiveFileReload()
    {
        if (activeFile is null || activeFileStamp is null || activeCanonicalPath is null)
        {
            return false;
        }

        pendingOpenFile = activeFile;
        pendingOpenFileStamp = activeFileStamp;
        pendingCanonicalPath = activeCanonicalPath;
        pendingNewDocument = false;
        return true;
    }

    public async Task<DocumentNewResult> NewAsync(bool hasUnsavedChanges)
    {
        pendingNewDocument = false;
        if (hasUnsavedChanges && !await ConfirmDiscardChangesAsync(
            "Creating a new drawing will replace the current unsaved drawing.",
            "Discard and create"))
        {
            return DocumentNewResult.Cancelled;
        }

        pendingNewDocument = true;
        return DocumentNewResult.Created;
    }

    public bool ConfirmCreated()
    {
        if (!pendingNewDocument)
        {
            return false;
        }

        activeFile = null;
        activeFileStamp = null;
        activeCanonicalPath = null;
        pendingOpenFile = null;
        pendingOpenFileStamp = null;
        pendingCanonicalPath = null;
        pendingNewDocument = false;
        return true;
    }

    public async Task<DocumentSaveResult> SaveAsync(string content, bool saveAs)
    {
        ValidateContentForSave(content);

        if (!saveAs && activeFile is not null &&
            await CheckExternalFileStateAsync() is not ExternalFileState.None)
        {
            throw new BridgeProtocolException(
                "DocumentChangedExternally",
                "The drawing changed outside Excalidraw Desktop. Resolve the conflict before saving.");
        }

        var file = saveAs || activeFile is null
            ? await PickSaveFileAsync()
            : activeFile;
        if (file is null)
        {
            return DocumentSaveResult.Cancelled;
        }

        var canonicalPath = DesktopDocumentPath.Normalize(file.Path);
        if (canonicalPath is not null &&
            IsPathOwnedByAnotherSession?.Invoke(canonicalPath) == true)
        {
            throw new BridgeProtocolException(
                "DocumentAlreadyOpen",
                "That drawing is already open in another tab. Choose a different file name.");
        }

        try
        {
            await WriteAtomicallyAsync(file, content);
            activeFile = file;
            activeCanonicalPath = canonicalPath;
            var properties = await file.GetBasicPropertiesAsync();
            activeFileStamp = ReadFileStamp(file, properties);
            pendingOpenFile = null;
            pendingOpenFileStamp = null;
            pendingCanonicalPath = null;
            pendingNewDocument = false;
            return new DocumentSaveResult("saved", file.Name);
        }
        catch (UnauthorizedAccessException)
        {
            throw new BridgeProtocolException(
                "DocumentAccessDenied",
                "Excalidraw Desktop does not have permission to write the selected file.");
        }
        catch (IOException)
        {
            throw new BridgeProtocolException(
                "DocumentWriteFailed",
                "The drawing could not be written to disk.");
        }
    }

    public async Task<ExternalFileState> CheckExternalFileStateAsync()
    {
        if (activeFile is null || activeFileStamp is null)
        {
            return ExternalFileState.None;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(activeFile.Path) ||
                !File.Exists(activeFile.Path))
            {
                return ExternalFileState.Deleted;
            }

            if (!DesktopDocumentPath.Equals(activeCanonicalPath, activeFile.Path))
            {
                return ExternalFileState.Moved;
            }

            var fileInfo = new FileInfo(activeFile.Path);
            var currentStamp = new DesktopFileStamp(
                fileInfo.LastWriteTimeUtc,
                (ulong)fileInfo.Length);
            return activeFileStamp.DiffersFrom(currentStamp)
                ? ExternalFileState.Modified
                : ExternalFileState.None;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or IOException)
        {
            return ExternalFileState.Deleted;
        }
    }

    public async Task<PickedDocument> ReloadActiveAsync()
    {
        if (activeFile is null)
        {
            throw new BridgeProtocolException(
                "DocumentNotFound",
                "This drawing no longer has a file to reload.");
        }

        return await ReadDocumentAsync(activeFile);
    }

    private static DesktopFileStamp ReadFileStamp(
        StorageFile file,
        Windows.Storage.FileProperties.BasicProperties fallback)
    {
        if (!string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
        {
            var fileInfo = new FileInfo(file.Path);
            return new DesktopFileStamp(
                fileInfo.LastWriteTimeUtc,
                (ulong)fileInfo.Length);
        }

        return new DesktopFileStamp(fallback.DateModified, fallback.Size);
    }

    private async Task<StorageFile?> PickSaveFileAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = activeFile is null
                ? "Untitled"
                : Path.GetFileNameWithoutExtension(activeFile.Name),
        };
        picker.FileTypeChoices.Add("Excalidraw drawing", new[] { ".excalidraw" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        return await picker.PickSaveFileAsync();
    }

    private static void ValidateContentForSave(string content)
    {
        ExcalidrawDocumentValidator.ValidateForSave(content, MaxDocumentBytes);
    }

    private static async Task WriteAtomicallyAsync(StorageFile file, string content)
    {
        using var transaction = await file.OpenTransactedWriteAsync();
        transaction.Stream.Size = 0;
        transaction.Stream.Seek(0);

        using (var writer = new DataWriter(transaction.Stream)
        {
            UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8,
        })
        {
            writer.WriteString(content);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        await transaction.CommitAsync();
    }

    public async Task<CloseDecision> PromptToSaveBeforeCloseAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = dialogRoot.XamlRoot,
            Title = "Save changes before closing?",
            Content = "Your changes will be lost if you close this drawing without saving.",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => CloseDecision.Save,
            ContentDialogResult.Secondary => CloseDecision.Discard,
            _ => CloseDecision.Cancel,
        };
    }

    private async Task<bool> ConfirmDiscardChangesAsync(
        string content,
        string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = dialogRoot.XamlRoot,
            Title = "Discard unsaved changes?",
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
