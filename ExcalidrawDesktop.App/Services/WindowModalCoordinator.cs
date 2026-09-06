using System.Runtime.CompilerServices;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;

namespace ExcalidrawDesktop.App.Services;

/// <summary>All dialogs and file pickers in a window share this queue.</summary>
internal sealed class WindowModalCoordinator
{
    private static readonly ConditionalWeakTable<Window, WindowModalCoordinator> Coordinators = new();
    private readonly AsyncOperationQueue queue = new();

    public static WindowModalCoordinator For(Window owner) =>
        Coordinators.GetValue(owner, _ => new WindowModalCoordinator());

    public bool IsBusy => queue.IsBusy;
    public Task<T> RunAsync<T>(Func<Task<T>> operation) => queue.RunAsync(operation);
    public Task RunAsync(Func<Task> operation) => queue.RunAsync(operation);
}
