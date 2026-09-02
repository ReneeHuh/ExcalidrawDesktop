namespace ExcalidrawDesktop.Core;

public static class WorkspaceTabOperations
{
    public static async Task<bool> RunSequentiallyAsync<T>(
        IEnumerable<T> tabs,
        Func<T, Task<bool>> operation)
    {
        foreach (var tab in tabs)
        {
            if (!await operation(tab))
            {
                return false;
            }
        }

        return true;
    }

    public static T? Adjacent<T>(IReadOnlyList<T> orderedTabs, T source, bool next)
        where T : class
    {
        var index = IndexOf(orderedTabs, source);
        if (index < 0 || orderedTabs.Count < 2)
        {
            return null;
        }

        var offset = next ? 1 : -1;
        return orderedTabs[(index + offset + orderedTabs.Count) % orderedTabs.Count];
    }

    public static IReadOnlyList<T> OtherTabs<T>(IReadOnlyList<T> orderedTabs, T source)
        where T : class => orderedTabs
            .Where(tab => !ReferenceEquals(tab, source))
            .ToArray();

    public static IReadOnlyList<T> TabsToRight<T>(IReadOnlyList<T> orderedTabs, T source)
        where T : class
    {
        var index = IndexOf(orderedTabs, source);
        return index < 0
            ? Array.Empty<T>()
            : orderedTabs.Skip(index + 1).ToArray();
    }

    private static int IndexOf<T>(IReadOnlyList<T> tabs, T source)
        where T : class
    {
        for (var index = 0; index < tabs.Count; index++)
        {
            if (ReferenceEquals(tabs[index], source))
            {
                return index;
            }
        }

        return -1;
    }
}
