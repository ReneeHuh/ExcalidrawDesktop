using System.Runtime.CompilerServices;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App.Services;

/// <summary>All dialogs and file pickers in a window share this queue.</summary>
internal sealed class WindowModalCoordinator
{
    private static readonly ConditionalWeakTable<Window, WindowModalCoordinator> Coordinators = new();
    private readonly AsyncOperationQueue queue = new();
    private readonly Window owner;

    private WindowModalCoordinator(Window owner) => this.owner = owner;

    public static WindowModalCoordinator For(Window owner) =>
        Coordinators.GetValue(owner, window => new WindowModalCoordinator(window));

    public bool IsBusy => queue.IsBusy;
    public Task<T> RunAsync<T>(Func<Task<T>> operation) => queue.RunAsync(operation);
    public Task RunAsync(Func<Task> operation) => queue.RunAsync(operation);

    public Task ShowMessageAsync(string title, string message) =>
        RunAsync(async () =>
        {
            try
            {
                // Resolve the root after waiting, since the window may have closed in the queue.
                var root = owner is MainWindow window ? window.DialogRoot : owner.Content?.XamlRoot;
                if (root is null) return;
                var dialog = new ContentDialog
                {
                    XamlRoot = root,
                    Title = title,
                    Content = message,
                    CloseButtonText = DesktopResources.Get("OkButton", "OK"),
                    DefaultButton = ContentDialogButton.Close,
                };
                await dialog.ShowAsync();
            }
            catch (Exception exception) when (exception is ObjectDisposedException or System.Runtime.InteropServices.COMException)
            {
                DiagnosticLogService.Info("window.message_unavailable", new { reason = exception.Message });
            }
        });
}
