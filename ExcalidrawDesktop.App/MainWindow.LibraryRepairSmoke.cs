using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml.Automation;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
#if DEBUG
    private async Task VerifyLibraryRepairRecoveryAsync(DocumentSession first, DocumentSession second)
    {
        DocumentTabs.SelectedItem = first.TabItem;
        var path = Path.Combine(DesktopPaths.DataRoot, "library.json");
        var existingId = "repair-existing-" + Guid.NewGuid().ToString("N");
        var localId = "repair-local-" + Guid.NewGuid().ToString("N");
        await first.CoreWebView!.ExecuteScriptAsync($"window.__EXCALIDRAW_DESKTOP_SMOKE__.addLibraryItem('{existingId}')");
        if (!await AsyncWait.UntilAsync(() => !first.HasUnsavedLibrary && FileContains(path, existingId), TimeSpan.FromSeconds(15)))
            throw new InvalidOperationException("The initial library item was not saved.");

        // Excalidraw deduplicates library entries by their elements, so the next
        // addition must contain a different scene from the already-saved item.
        var addedElementId = "repair-element-" + Guid.NewGuid().ToString("N");
        RequestAutomationEdit(first, addedElementId);
        var editDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (!(await ReadSmokeStateAsync(first)).ElementIds.Contains(addedElementId))
        {
            if (DateTimeOffset.UtcNow >= editDeadline)
                throw new InvalidOperationException("The second library fixture did not receive its distinct element.");
            await Task.Delay(50);
        }
        await File.WriteAllTextAsync(path, "[");
        var prompts = libraryRepairPromptsForSmoke;
        Title = "Excalidraw Desktop — Library repair smoke: cancel repair";
        await first.CoreWebView.ExecuteScriptAsync($"window.__EXCALIDRAW_DESKTOP_SMOKE__.addLibraryItem('{localId}')");
        if (!await AsyncWait.UntilAsync(() => libraryRepairPromptsForSmoke > prompts &&
                !WindowModalCoordinator.For(this).IsBusy, TimeSpan.FromSeconds(20)) || !first.HasUnsavedLibrary)
            throw new InvalidOperationException($"Cancelling repair did not retain the pending library edits (prompts={libraryRepairPromptsForSmoke - prompts}, busy={WindowModalCoordinator.For(this).IsBusy}, pending={first.HasUnsavedLibrary}, blocked={CommandsBlocked}).");

        var load = new BridgeMessage(1, "request", Guid.NewGuid().ToString("D"), "library.load", null);
        var laterTab = await HandleLibraryRequestAsync(second, load).WaitAsync(TimeSpan.FromSeconds(3));
        if (laterTab is not LibraryLoadResult { Status: "unavailable" } || libraryRepairPromptsForSmoke != prompts + 1)
            throw new InvalidOperationException("A later tab repeated the declined repair prompt.");

        Title = "Excalidraw Desktop — Library repair smoke: retry repair";
        await first.CoreWebView.ExecuteScriptAsync("window.__EXCALIDRAW_DESKTOP_SMOKE__.retryLibrary()");
        if (!await AsyncWait.UntilAsync(() => !first.HasUnsavedLibrary && FileContains(path, localId), TimeSpan.FromSeconds(20)) ||
            !FileContains(path, existingId))
            throw new InvalidOperationException("Repair retry lost existing library items or pending edits.");

        // Emulate another window completing the shared repair while this approval is open.
        await File.WriteAllTextAsync(path, "[{");
        var damaged = await workspaceCoordinator.LoadLibraryAsync();
        prompts = libraryRepairPromptsForSmoke;
        var staleApproval = HandleLibraryRequestAsync(second, load);
        if (!await AsyncWait.UntilAsync(() => libraryRepairPromptsForSmoke > prompts, TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("The stale repair approval fixture did not open.");
        var repairedElsewhere = await workspaceCoordinator.RepairLibraryAsync(damaged.Revision);
        Title = "Excalidraw Desktop — Library repair smoke: approve stale repair";
        if (await staleApproval.WaitAsync(TimeSpan.FromSeconds(20)) is not LibraryLoadResult result ||
            result != repairedElsewhere)
            throw new InvalidOperationException("A stale approval did not reload the library repaired elsewhere.");
    }

    private async Task VerifyExportCloseScopeAsync(DocumentSession session, string destinationPath)
    {
        using var cancellation = new CancellationTokenSource();
        session.PendingImageExport = new(Guid.NewGuid(),
            await Windows.Storage.StorageFile.GetFileFromPathAsync(destinationPath), cancellation);
        try
        {
            if (await windowClose.RequestCloseSessionAsync(session))
                throw new InvalidOperationException("An exporting drawing was allowed to close.");
            if (await EnterCloseBarrierAsync([session], closingWindow: true))
                throw new InvalidOperationException("An exporting window was allowed to close.");
        }
        finally { session.PendingImageExport = null; }
    }

    private void VerifyFileVerificationLabels(DocumentSession session)
    {
        DocumentTabs.SelectedItem = session.TabItem;
        foreach (var state in new[] { ExternalFileState.Unavailable, ExternalFileState.UnknownBaseline })
        {
            session.ExternalFileState = state;
            UpdateTabHeader(session);
            UpdateWindowTitle();
            var expectedTab = state == ExternalFileState.Unavailable
                ? DesktopResources.Get("TabStateFileUnavailable", "file unavailable")
                : DesktopResources.Get("TabStateFileVersionUnknown", "file version unknown");
            var expectedTitle = state == ExternalFileState.Unavailable
                ? DesktopResources.Get("FileUnavailableTitleSuffix", " — File unavailable")
                : DesktopResources.Get("FileVersionUnknownTitleSuffix", " — File version unknown");
            if (!AutomationProperties.GetName(session.TabItem).Contains(expectedTab, StringComparison.Ordinal) ||
                !Title.EndsWith(expectedTitle, StringComparison.Ordinal))
                throw new InvalidOperationException("File verification state was mislabeled in the title or accessible tab name.");
        }
        session.ExternalFileState = ExternalFileState.None;
        UpdateTabHeader(session);
        UpdateWindowTitle();
    }
#endif
}
