using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App.Views.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; private set; } = null!;

    public SettingsPage() => InitializeComponent();

    internal void Initialize(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnSettingsPersistenceRetry(object sender, RoutedEventArgs args) =>
        ViewModel.RetryPersistence();
}
