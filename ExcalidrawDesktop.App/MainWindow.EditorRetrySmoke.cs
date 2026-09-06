#if DEBUG
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private async Task VerifyEditorRetrySmokeAsync(DocumentSession session)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            $"editor-retry-smoke-{Guid.NewGuid():N}.excalidraw");
        var saved = CreateDocumentSafetyScene("retry-saved", 40);
        try
        {
            await File.WriteAllTextAsync(path, saved);
            documents.AttachDocumentToSession(
                session, await session.DocumentService.OpenPathAsync(path), select: true);
            await WaitForRetrySmokeReadyAsync(session, isDirty: false);

            // Process failure must release a save-before-close waiter so Retry
            // does not inherit an unfinished close operation from the old page.
            var closeCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.WindowCloseSaveCompletion = closeCompletion;
            var firstWebView = session.CoreWebView;
            SimulateWebViewProcessFailureForSmoke(session);
            if (!closeCompletion.Task.IsCompletedSuccessfully || closeCompletion.Task.Result)
            {
                throw new InvalidOperationException(
                    "Editor failure left a close-save operation pending.");
            }
            await editorSessions.RetryAsync(session);
            await WaitForRetrySmokeReadyAsync(session, isDirty: false);
            if (ReferenceEquals(firstWebView, session.CoreWebView) ||
                !(await ReadSmokeStateAsync(session)).ElementIds.Contains("retry-saved"))
            {
                throw new InvalidOperationException("Retry lost the saved drawing content.");
            }

            RequestAutomationEdit(session, "retry-unsaved");
            if (!await AsyncWait.UntilAsync(
                    () => session.IsDirty && session.RecoveryUpdatedAt is not null &&
                        !session.IsBridgeDispatching,
                    TimeSpan.FromSeconds(15)))
            {
                throw new InvalidOperationException("The retry fixture did not create recovery data.");
            }
            var snapshot = await recoverySnapshotStore.LoadAsync(session.RecoveryId);
            if (snapshot is null || !snapshot.Contains("retry-unsaved", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The recovery snapshot omitted the unsaved edit.");
            }

            SimulateWebViewProcessFailureForSmoke(session);
            await editorSessions.RetryAsync(session);
            await WaitForRetrySmokeReadyAsync(session, isDirty: true);
            var restored = await ReadSmokeStateAsync(session);
            if (!restored.ElementIds.Contains("retry-saved") ||
                !restored.ElementIds.Contains("retry-unsaved"))
            {
                throw new InvalidOperationException("Retry lost the recovered drawing content.");
            }

            foreach (var unavailableSnapshot in new string?[] { null, "invalid recovery json" })
            {
                // Disconnect first so a scheduled snapshot from the old page
                // cannot repair the deliberately missing/corrupt test fixture.
                EditorSessionController.DetachWebView(session);
                await session.RecoveryGate.WaitAsync();
                try
                {
                    if (unavailableSnapshot is null)
                    {
                        await recoverySnapshotStore.DeleteAsync(session.RecoveryId);
                    }
                    else
                    {
                        await recoverySnapshotStore.SaveAsync(session.RecoveryId, unavailableSnapshot);
                    }
                }
                finally
                {
                    session.RecoveryGate.Release();
                }
                SimulateWebViewProcessFailureForSmoke(session);
                await editorSessions.RetryAsync(session);
                // Tab selection and window activation must not bypass a failed
                // Retry by automatically initializing an empty page.
                await editorSessions.InitializeAsync(session);
                if (session.IsReady || session.IsRetrying ||
                    !session.IsDirty || !session.Content.IsFailureVisible ||
                    EditorSessionController.CanRequestSave(session))
                {
                    throw new InvalidOperationException(
                        "Retry exposed an empty editor without usable recovery data.");
                }
                if (!string.Equals(await File.ReadAllTextAsync(path), saved, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Retry changed the saved drawing file.");
                }

                await recoverySnapshotStore.SaveAsync(session.RecoveryId, snapshot);
                await editorSessions.RetryAsync(session);
                await WaitForRetrySmokeReadyAsync(session, isDirty: true);
            }

            if (!(await ReadSmokeStateAsync(session)).ElementIds.Contains("retry-unsaved") ||
                !string.Equals(await File.ReadAllTextAsync(path), saved, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Retry did not preserve both recovery and disk content.");
            }
        }
        finally
        {
            session.DetachExternalFileWatcher();
            File.Delete(path);
        }
    }

    private static async Task WaitForRetrySmokeReadyAsync(DocumentSession session, bool isDirty)
    {
        if (!await AsyncWait.UntilAsync(
                () => session.IsReady && !session.IsRetrying &&
                    session.PendingDocumentLoad is null &&
                    session.IsDirty == isDirty && !session.IsBridgeDispatching,
                TimeSpan.FromSeconds(20)))
        {
            throw new InvalidOperationException(
                $"The retried editor did not finish loading: {session.LastLifecycleFailure}");
        }
    }
}
#endif
