using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace ExcalidrawDesktop.App.Services;

public sealed record PickedDocument(
    StorageFile File,
    string CanonicalPath,
    string FileName,
    string Content,
    DesktopFileStamp Stamp,
    string ContentHash);

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
    private string? activeContentHash;
    private DesktopFileStamp? pendingOpenFileStamp;
    private string? pendingOpenContentHash;
    private string? activeCanonicalPath;
    private string? pendingCanonicalPath;
    private bool pendingNewDocument;
    public Guid? PendingOpenId { get; private set; }

    public Func<string, bool>? IsPathOwnedByAnotherSession { get; set; }
    /// <summary>Application-wide reservation for an in-flight open/save destination.</summary>
    public Func<string, IDisposable?>? TryReservePath { get; set; }

#if DEBUG
    internal StorageFile? SaveFileOverrideForSmoke { get; set; }
    internal Func<CancellationToken, Task>? BeforeSaveWriteForSmoke { get; set; }
#endif

    public string? DocumentPath => activeCanonicalPath ?? pendingCanonicalPath;

    public bool HasActiveFile => activeFile is not null;

    public RecoveryFileBaseline? RecoveryBaseline =>
        activeCanonicalPath is { } path && activeFileStamp is { } stamp
            ? new RecoveryFileBaseline(path, stamp, activeContentHash) : null;

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

    public async Task<PickedDocument?> PickOpenDocumentAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".excalidraw");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await WindowModalCoordinator.For(window).RunAsync(
            async () => await picker.PickSingleFileAsync());
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

            ExactFileBaseline? exactBaseline = null;
            string content;
            if (!string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
            {
                exactBaseline = await ExactFileBaseline.CaptureAsync(file.Path);
                content = exactBaseline.Utf8Content;
            }
            else
            {
                content = await FileIO.ReadTextAsync(
                    file,
                    Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
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
                exactBaseline?.Stamp ?? ReadFileStamp(file, properties),
                exactBaseline?.ContentHash ?? RecoveryFileBaseline.ComputeContentHash(content));
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
        catch (System.Text.DecoderFallbackException)
        {
            throw new BridgeProtocolException("DocumentInvalid", "The drawing must contain valid UTF-8 JSON.");
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
        pendingOpenFile = document.File;
        pendingOpenFileStamp = document.Stamp;
        pendingOpenContentHash = document.ContentHash;
        pendingCanonicalPath = document.CanonicalPath;
        pendingNewDocument = false;
        PendingOpenId = Guid.NewGuid();
    }

    public async Task RestoreActiveFileAsync(string? path, DesktopFileStamp? expectedStamp,
        string? expectedContentHash = null)
    {
        activeFile = null;
        activeFileStamp = null;
        activeContentHash = null;
        activeCanonicalPath = null;
        pendingOpenFile = null;
        pendingOpenFileStamp = null;
        pendingOpenContentHash = null;
        pendingCanonicalPath = null;
        pendingNewDocument = false;
        PendingOpenId = null;
        var canonicalPath = DesktopDocumentPath.Normalize(path);
        if (canonicalPath is null || !File.Exists(canonicalPath))
        {
            return;
        }

        try
        {
            activeFile = await StorageFile.GetFileFromPathAsync(canonicalPath);
            activeCanonicalPath = canonicalPath;
            // Reading today's file stamp here would hide edits made after the
            // recovery snapshot. A missing baseline must remain unknown.
            activeFileStamp = expectedStamp;
            activeContentHash = expectedContentHash;
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

    public bool ConfirmOpened(string fileName, Guid? loadId = null)
    {
        if (pendingOpenFile is null ||
            !string.Equals(pendingOpenFile.Name, fileName, StringComparison.Ordinal) ||
            (loadId is not { } id || PendingOpenId != id))
        {
            return false;
        }

        activeFile = pendingOpenFile;
        activeFileStamp = pendingOpenFileStamp;
        activeContentHash = pendingOpenContentHash;
        activeCanonicalPath = pendingCanonicalPath;
        pendingOpenFile = null;
        pendingOpenFileStamp = null;
        pendingOpenContentHash = null;
        pendingCanonicalPath = null;
        PendingOpenId = null;
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
        pendingOpenContentHash = activeContentHash;
        pendingCanonicalPath = activeCanonicalPath;
        pendingNewDocument = false;
        PendingOpenId = Guid.NewGuid();
        return true;
    }

    public async Task<DocumentNewResult> NewAsync(bool hasUnsavedChanges)
    {
        pendingNewDocument = false;
        if (hasUnsavedChanges && !await ConfirmDiscardChangesAsync(
            DesktopResources.Get(
                "NewDiscardContent",
                "Creating a new drawing will replace the current unsaved drawing."),
            DesktopResources.Get("DiscardAndCreate", "Discard and create")))
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
        activeContentHash = null;
        activeCanonicalPath = null;
        pendingOpenFile = null;
        pendingOpenFileStamp = null;
        pendingCanonicalPath = null;
        pendingNewDocument = false;
        return true;
    }

    public bool IsSaving { get; private set; }
    public event Action<bool>? SavePickerChanged;

    public async Task<DocumentSaveResult> SaveAsync(string content, bool saveAs,
        CancellationToken cancellationToken = default)
    {
        if (IsSaving)
        {
            throw new BridgeProtocolException("DocumentSaveInProgress", "Wait for the current save to finish.");
        }
        IsSaving = true;
        try
        {
            return await SaveCoreAsync(content, saveAs, cancellationToken);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task<DocumentSaveResult> SaveCoreAsync(string content, bool saveAs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateContentForSave(content);

        var file = saveAs || activeFile is null
            ? await PickSaveFileAsync(cancellationToken)
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
            IDisposable? pathReservation = null;
            if (canonicalPath is not null && TryReservePath is { } reserve)
            {
                pathReservation = reserve(canonicalPath);
                if (pathReservation is null)
                {
                    throw new BridgeProtocolException(
                        "DocumentAlreadyOpen",
                        "That drawing is already being opened or saved. Choose a different file name.");
                }
            }
            using (pathReservation)
            {
            // The save picker and any caller hooks can yield to external
            // writers. Check again immediately before opening the transaction.
            if (!saveAs && activeFile is not null) await VerifyFileBeforeSaveAsync();
#if DEBUG
            if (BeforeSaveWriteForSmoke is { } beforeWrite)
            {
                await beforeWrite(cancellationToken);
            }
#endif
            cancellationToken.ThrowIfCancellationRequested();
            await WriteAtomicallyAsync(
                file,
                content,
                cancellationToken,
                !saveAs && activeFileStamp is not null &&
                    !string.IsNullOrWhiteSpace(activeContentHash)
                    ? activeContentHash : null);
            activeFile = file;
            activeCanonicalPath = canonicalPath;
            var properties = await file.GetBasicPropertiesAsync();
            activeFileStamp = ReadFileStamp(file, properties);
            activeContentHash = RecoveryFileBaseline.ComputeContentHash(content);
            pendingOpenFile = null;
            pendingOpenFileStamp = null;
            pendingOpenContentHash = null;
            pendingCanonicalPath = null;
            pendingNewDocument = false;
            return new DocumentSaveResult("saved", file.Name);
            }
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

    private async Task VerifyFileBeforeSaveAsync()
    {
        var state = await CheckExternalFileStateAsync(compareContent: false);
        if (state == ExternalFileState.Unavailable)
            throw new BridgeProtocolException("DocumentUnavailable", "The file is unavailable. Retry or use Save As.");
        if (state == ExternalFileState.UnknownBaseline)
            throw new BridgeProtocolException("DocumentBaselineUnknown", "The recovered file version is unknown. Resolve it before saving.");
        if (state != ExternalFileState.None)
            throw new BridgeProtocolException("DocumentChangedExternally", "The drawing changed outside Excalidraw Desktop. Resolve the conflict before saving.");
    }

    public Task<ExternalFileState> CheckExternalFileStateAsync() => CheckExternalFileStateAsync(compareContent: true);

    private async Task<ExternalFileState> CheckExternalFileStateAsync(bool compareContent)
    {
        if (activeFile is null)
        {
            return ExternalFileState.None;
        }

        if (string.IsNullOrWhiteSpace(activeFile.Path)) return ExternalFileState.Unavailable;
        if (!DesktopDocumentPath.Equals(activeCanonicalPath, activeFile.Path)) return ExternalFileState.Moved;
        return await ExternalFileCheck.CheckAsync(activeFile.Path, activeFileStamp, activeContentHash, compareContent);
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

    public bool IsSavePickerOpen { get; private set; }

    private async Task<StorageFile?> PickSaveFileAsync(CancellationToken cancellationToken)
    {
#if DEBUG
        if (SaveFileOverrideForSmoke is { } smokeFile)
        {
            SaveFileOverrideForSmoke = null;
            return smokeFile;
        }
#endif
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = activeFile is null
                ? DesktopResources.Get("UntitledName", "Untitled")
                : Path.GetFileNameWithoutExtension(activeFile.Name),
        };
        picker.FileTypeChoices.Add(
            DesktopResources.Get("ExcalidrawDrawingType", "Excalidraw drawing"),
            new[] { ".excalidraw" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        IsSavePickerOpen = true;
        SavePickerChanged?.Invoke(true);
        try
        {
            return await WindowModalCoordinator.For(window).RunAsync(
                async () => await picker.PickSaveFileAsync().AsTask(cancellationToken));
        }
        finally
        {
            IsSavePickerOpen = false;
            SavePickerChanged?.Invoke(false);
        }
    }

    private static void ValidateContentForSave(string content)
    {
        ExcalidrawDocumentValidator.ValidateForSave(content, MaxDocumentBytes);
    }

    private static async Task WriteAtomicallyAsync(
        StorageFile file,
        string content,
        CancellationToken cancellationToken,
        string? expectedContentHash = null)
        => await TransactedDocumentWriter.WriteAsync(
            file, content, cancellationToken, expectedContentHash);

    public async Task<CloseDecision> PromptToSaveBeforeCloseAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = dialogRoot.XamlRoot,
            Title = DesktopResources.Get(
                "SaveChangesBeforeClosingTitle",
                "Save changes before closing?"),
            Content = DesktopResources.Get(
                "SaveChangesBeforeClosingContent",
                "Your changes will be lost if you close this drawing without saving."),
            PrimaryButtonText = DesktopResources.Get("SaveButton", "Save"),
            SecondaryButtonText = DesktopResources.Get("DiscardButton", "Discard"),
            CloseButtonText = DesktopResources.Get("CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        return await WindowModalCoordinator.For(window).RunAsync(
            async () => await dialog.ShowAsync()) switch
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
            Title = DesktopResources.Get(
                "DiscardUnsavedChangesTitle",
                "Discard unsaved changes?"),
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = DesktopResources.Get("CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await WindowModalCoordinator.For(window).RunAsync(
            async () => await dialog.ShowAsync()) == ContentDialogResult.Primary;
    }
}
