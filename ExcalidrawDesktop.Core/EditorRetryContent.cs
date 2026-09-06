namespace ExcalidrawDesktop.Core;

public sealed record PendingEditorLoad(
    string FileName,
    string Content,
    bool IsRecovery)
{
    // Assigned when a host stages this content for a particular editor page.
    public Guid LoadId { get; init; }
}

public sealed record EditorRetryState(
    string DisplayName,
    bool IsDirty,
    bool HasActiveFile,
    PendingEditorLoad? PendingLoad = null,
    string? HibernatedContent = null);

/// <summary>
/// Selects and validates the drawing to restore before an editor is replaced.
/// A dirty drawing must never fall back to an older saved file or an empty page.
/// </summary>
public static class EditorRetryContent
{
    public static async Task<PendingEditorLoad?> LoadAsync(
        EditorRetryState state,
        Func<Task<string?>> loadRecovery,
        Func<Task<PendingEditorLoad>> loadSavedDocument)
    {
        PendingEditorLoad? load;
        if (state.PendingLoad is { } pending)
        {
            load = pending;
        }
        else if (state.IsDirty)
        {
            var recovered = await loadRecovery();
            if (recovered is null)
            {
                throw new BridgeProtocolException(
                    "EditorRecoveryUnavailable",
                    "The unsaved drawing does not have an available recovery snapshot.");
            }
            load = new PendingEditorLoad(state.DisplayName, recovered, IsRecovery: true);
        }
        else if (state.HibernatedContent is { } hibernated)
        {
            load = new PendingEditorLoad(state.DisplayName, hibernated, IsRecovery: false);
        }
        else if (state.HasActiveFile)
        {
            load = await loadSavedDocument();
        }
        else
        {
            // An untouched, untitled tab is the only case that needs no load.
            return null;
        }

        ExcalidrawDocumentValidator.Validate(load.Content);
        return load;
    }
}
