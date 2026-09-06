#if DEBUG
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;
using Windows.Storage;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private async Task VerifyRecoveryConflictSmokeAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"recovery-conflict-{Guid.NewGuid():N}.excalidraw");
        var copyPath = Path.Combine(AppContext.BaseDirectory, $"recovery-copy-{Guid.NewGuid():N}.excalidraw");
        var recoveryId = Guid.NewGuid().ToString("N");
        var original = CreateDocumentSafetyScene("original", 0);
        var recovered = CreateDocumentSafetyScene("unsaved-recovery", 40);
        var external = CreateDocumentSafetyScene("newer-external-drawing", 80);
        try
        {
            await File.WriteAllTextAsync(path, original);
            var beforeCrash = new DocumentService(this, DocumentTabs);
            var opened = await beforeCrash.OpenPathAsync(path);
            beforeCrash.StageOpen(opened);
            if (!beforeCrash.ConfirmOpened(opened.FileName, beforeCrash.PendingOpenId))
            {
                throw new InvalidOperationException("The recovery fixture failed to commit its staged file.");
            }
            await recoverySnapshotStore.SaveAsync(recoveryId, recovered, beforeCrash.RecoveryBaseline);
            var snapshot = await recoverySnapshotStore.LoadAsync(recoveryId) ??
                throw new InvalidOperationException("The recovery conflict fixture was not saved.");
            var stamp = RecoveryFileBaseline.ReadStamp(snapshot, path);
            var baseline = RecoveryFileBaseline.Read(snapshot, path);

            var unchanged = new DocumentService(this, DocumentTabs);
            await unchanged.RestoreActiveFileAsync(path, stamp, baseline?.ContentHash);
            if (await unchanged.CheckExternalFileStateAsync() != ExternalFileState.None)
            {
                throw new InvalidOperationException("An unchanged recovered file reported a conflict.");
            }

            await File.WriteAllTextAsync(path, external);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var afterRestart = new DocumentService(this, DocumentTabs);
            await afterRestart.RestoreActiveFileAsync(path, stamp, baseline?.ContentHash);
            await AssertRecoverySaveBlockedAsync(afterRestart, snapshot);
            if (await File.ReadAllTextAsync(path) != external)
            {
                throw new InvalidOperationException("Recovery overwrote an externally changed file.");
            }

            // Legacy snapshots have no trustworthy file baseline. They must
            // also require a choice even when the current file is readable.
            var legacy = new DocumentService(this, DocumentTabs);
            await legacy.RestoreActiveFileAsync(path, RecoveryFileBaseline.ReadStamp(recovered, path));
            await AssertRecoverySaveBlockedAsync(legacy, recovered);

            // A legacy timestamp alone cannot prove byte identity either.
            var stampOnly = new DocumentService(this, DocumentTabs);
            var current = await stampOnly.OpenPathAsync(path);
            await stampOnly.RestoreActiveFileAsync(path, current.Stamp);
            await AssertRecoverySaveBlockedAsync(stampOnly, recovered);

            await File.WriteAllTextAsync(copyPath, original);
            afterRestart.SaveFileOverrideForSmoke = await StorageFile.GetFileFromPathAsync(copyPath);
            await afterRestart.SaveAsync(recovered, saveAs: true);
            if (!FileContains(copyPath, "unsaved-recovery") ||
                await File.ReadAllTextAsync(path) != external ||
                await afterRestart.CheckExternalFileStateAsync() != ExternalFileState.None)
            {
                throw new InvalidOperationException("Save As did not preserve both recovery and the external drawing.");
            }

            // Explicitly reloading the disk version establishes a new baseline.
            var reloaded = await legacy.ReloadActiveAsync();
            legacy.StageOpen(reloaded);
            if (!legacy.ConfirmOpened(reloaded.FileName, legacy.PendingOpenId))
            {
                throw new InvalidOperationException("Reload did not commit its staged file.");
            }
            if (await legacy.CheckExternalFileStateAsync() != ExternalFileState.None)
            {
                throw new InvalidOperationException("Reload did not resolve the recovery conflict.");
            }
            await legacy.SaveAsync(external, saveAs: false);
        }
        finally
        {
            await recoverySnapshotStore.DeleteAsync(recoveryId);
            File.Delete(path);
            File.Delete(copyPath);
        }
    }

    private static async Task AssertRecoverySaveBlockedAsync(DocumentService document, string content)
    {
        if (await document.CheckExternalFileStateAsync() != ExternalFileState.Modified)
        {
            throw new InvalidOperationException("A recovered file lost its external-change conflict.");
        }
        try
        {
            await document.SaveAsync(content, saveAs: false);
        }
        catch (BridgeProtocolException exception) when (exception.Code == "DocumentChangedExternally")
        {
            return;
        }
        throw new InvalidOperationException("An unsafe recovery save was allowed.");
    }
}
#endif
