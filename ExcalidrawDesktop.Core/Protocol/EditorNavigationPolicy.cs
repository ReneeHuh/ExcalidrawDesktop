namespace ExcalidrawDesktop.Core;

/// <summary>A newly created editor may load its entry point exactly once.</summary>
public sealed class EditorNavigationPolicy(string entryPoint)
{
    private bool started;

    public bool TryBeginNavigation(string uri)
    {
        if (started || !string.Equals(uri, entryPoint, StringComparison.Ordinal))
        {
            return false;
        }

        started = true;
        return true;
    }
}
