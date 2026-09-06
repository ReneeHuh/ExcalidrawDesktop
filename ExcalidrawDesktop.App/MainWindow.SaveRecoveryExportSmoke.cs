#if DEBUG
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using Windows.Storage;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private async Task VerifySaveRecoveryExportSmokeAsync(DocumentSession session)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"resilience-{Guid.NewGuid():N}.excalidraw");
        var pngPath = Path.ChangeExtension(path, ".png");
        var original = CreateDocumentSafetyScene("resilience-shape", 40);
        var timeout = windowClose.SaveResponseTimeoutForSmoke;
        var exportFailure = imageExports.ExportFailedForSmoke;
        try
        {
            await File.WriteAllTextAsync(path, original);
            documents.AttachDocumentToSession(session,
                await session.DocumentService.OpenPathAsync(path), select: true);
            await WaitForRetrySmokeReadyAsync(session, isDirty: false);

            var recoveryAttempts = 0;
            documents.BeforeRecoveryWriteForSmoke = candidate =>
            {
                if (ReferenceEquals(candidate, session) && ++recoveryAttempts == 1)
                {
                    throw new IOException("Injected recovery write failure.");
                }
                return Task.CompletedTask;
            };
            var previousSnapshot = session.RecoveryUpdatedAt;
            await UpdateDirtySmokeAppStateAsync(session, new { viewBackgroundColor = "#123456" });
            if (!await AsyncWait.UntilAsync(() => recoveryAttempts >= 2 && session.RecoveryUpdatedAt != previousSnapshot,
                    TimeSpan.FromSeconds(12)))
            {
                throw new InvalidOperationException("Recovery did not retry a failed write without another edit.");
            }
            var recovered = await recoverySnapshotStore.LoadAsync(session.RecoveryId);
            if (recovered is null || ReadDirtySmokeBackground(recovered) != "#123456")
            {
                throw new InvalidOperationException("The retried recovery snapshot contained the wrong drawing.");
            }
            documents.BeforeRecoveryWriteForSmoke = null;

            // Save revision A while revision B reaches durable recovery. When
            // A completes, its acknowledgement must not delete B's snapshot.
            await UpdateDirtySmokeAppStateAsync(session, new { viewBackgroundColor = "#111111" });
            if (!await AsyncWait.UntilAsync(() => session.IsDirty, TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("Revision A did not become dirty.");
            }
            var saveAStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSaveA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.DocumentService.BeforeSaveWriteForSmoke = async cancellation =>
            {
                saveAStarted.TrySetResult(true);
                await releaseSaveA.Task.WaitAsync(cancellation);
            };
            RequestSessionSave(session);
            await saveAStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var beforeRevisionB = session.RecoveryUpdatedAt;
            await UpdateDirtySmokeAppStateAsync(session, new { viewBackgroundColor = "#222222" });
            if (!await AsyncWait.UntilAsync(
                    () => session.RecoveryUpdatedAt is { } updated && updated != beforeRevisionB,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException("Revision B did not reach recovery while A was saving.");
            }
            releaseSaveA.TrySetResult(true);
            if (!await AsyncWait.UntilAsync(
                    () => FileContains(path, "111111"), TimeSpan.FromSeconds(10)) ||
                !(await recoverySnapshotStore.LoadAsync(session.RecoveryId) ?? "").Contains(
                    "222222", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Completing save A deleted or replaced the newer revision B recovery snapshot.");
            }
            session.DocumentService.BeforeSaveWriteForSmoke = null;
            var beforeCancelledSave = await File.ReadAllTextAsync(path);

            var writeStarted = false;
            session.DocumentService.BeforeSaveWriteForSmoke = async cancellation =>
            {
                writeStarted = true;
                await Task.Delay(Timeout.Infinite, cancellation);
            };
            windowClose.SaveResponseTimeoutForSmoke = TimeSpan.FromSeconds(1);
            if (await windowClose.RequestSaveForWindowCloseAsync(session) || !writeStarted ||
                !await AsyncWait.UntilAsync(() => !session.IsBridgeDispatching, TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("A close timeout did not cancel its native save.");
            }
            await Task.Delay(250);
            if (await session.CoreWebView!.ExecuteScriptAsync("window.__EXCALIDRAW_DESKTOP_SMOKE__.isSaving()") != "false" ||
                await File.ReadAllTextAsync(path) != beforeCancelledSave || !session.IsDirty)
            {
                throw new InvalidOperationException("Cancelling a save left the editor busy or changed the file.");
            }
            session.DocumentService.BeforeSaveWriteForSmoke = null;
            RequestSessionSave(session);
            if (!await AsyncWait.UntilAsync(() => !session.IsDirty && !session.IsBridgeDispatching,
                    TimeSpan.FromSeconds(10)) || ReadDirtySmokeBackground(await File.ReadAllTextAsync(path)) != "#222222")
            {
                throw new InvalidOperationException("Saving could not be retried after cancellation.");
            }

            // Dispose the actual WebView while its resource callback owns a
            // deferral. Both the cancellation response and cleanup must survive.
            byte[] sentinel = [1, 2, 3, 4];
            await File.WriteAllBytesAsync(pngPath, sentinel);
            var uploadStarted = false;
            var uploadFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            imageExports.ExportFailedForSmoke = _ => true;
            imageExports.BeforeUploadWriteForSmoke = async candidate =>
            {
                if (ReferenceEquals(candidate, session))
                {
                    uploadStarted = true;
                    SimulateWebViewProcessFailureForSmoke(session);
                    session.Content.CloseEditor();
                    await Task.Yield();
                }
            };
            imageExports.UploadFinishedForSmoke = () =>
            {
                if (uploadStarted)
                {
                    uploadFinished.TrySetResult(true);
                }
            };
            imageExports.StartImageExport(session, session.CoreWebView!, await StorageFile.GetFileFromPathAsync(pngPath));
            await uploadFinished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (session.IsExporting || session.IsReady || !(await File.ReadAllBytesAsync(pngPath)).SequenceEqual(sentinel))
            {
                throw new InvalidOperationException("Editor failure during PNG export did not preserve the destination.");
            }
            imageExports.BeforeUploadWriteForSmoke = null;
            imageExports.UploadFinishedForSmoke = null;
            await editorSessions.RetryAsync(session);
            await WaitForRetrySmokeReadyAsync(session, isDirty: false);
        }
        finally
        {
            documents.BeforeRecoveryWriteForSmoke = null;
            session.DocumentService.BeforeSaveWriteForSmoke = null;
            windowClose.SaveResponseTimeoutForSmoke = timeout;
            imageExports.BeforeUploadWriteForSmoke = null;
            imageExports.UploadFinishedForSmoke = null;
            imageExports.ExportFailedForSmoke = exportFailure;
            session.DetachExternalFileWatcher();
            File.Delete(path);
            File.Delete(pngPath);
        }
    }
}
#endif
