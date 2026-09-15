using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Views.Settings;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
#if DEBUG
    private void VerifyWindowViewModelsForSmoke(MainWindow destination)
    {
        if (ReferenceEquals(ViewModel, destination.ViewModel) ||
            ReferenceEquals(ViewModel.SettingsVM, destination.ViewModel.SettingsVM) ||
            !ReferenceEquals(AppSettingsPage.ViewModel, ViewModel.SettingsVM) ||
            !ReferenceEquals(destination.AppSettingsPage.ViewModel, destination.ViewModel.SettingsVM))
            throw new InvalidOperationException("Windows did not own distinct, long-lived settings view models.");

        var original = desktopPreferences;
        var preferenceEvents = 0;
        void OnChanged(object? sender, PreferencesChangedEventArgs args) => preferenceEvents++;
        ViewModel.SettingsVM.PreferencesChanged += OnChanged;
        destination.ViewModel.SettingsVM.PreferencesChanged += OnChanged;
        try
        {
            ViewModel.SettingsVM.ReopenSavedTabs = !original.ReopenSavedTabs;
            if (destination.ViewModel.SettingsVM.ReopenSavedTabs != !original.ReopenSavedTabs ||
                destination.desktopPreferences.ReopenSavedTabs != !original.ReopenSavedTabs ||
                preferenceEvents != 1)
                throw new InvalidOperationException("A shared preference did not reach both windows exactly once.");
        }
        finally
        {
            ViewModel.SettingsVM.PreferencesChanged -= OnChanged;
            destination.ViewModel.SettingsVM.PreferencesChanged -= OnChanged;
            workspaceCoordinator.UpdatePreferences(original);
        }
    }

    private async Task VerifySettingsBindingsForSmokeAsync()
    {
        // The Settings page starts collapsed; wait for its content templates to mount.
        if (!await AsyncWait.UntilAsync(() =>
                ((ComboBox)AppSettingsPage.FindName("ThemePicker")).IsLoaded,
                TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Settings controls did not mount.");
        var settings = ViewModel.SettingsVM;
        var original = desktopPreferences;
        var preferenceEvents = 0;
        void OnChanged(object? sender, PreferencesChangedEventArgs args) => preferenceEvents++;
        settings.PreferencesChanged += OnChanged;
        try
        {
            if (!ReferenceEquals(AppSettingsPage.ViewModel, settings) ||
                SaveMenuItem.IsEnabled || SaveAsMenuItem.IsEnabled ||
                ExportPngMenuItem.IsEnabled || SaveAllMenuItem.IsEnabled ||
                !CloseTabMenuItem.IsEnabled)
                throw new InvalidOperationException($"Settings bindings failed: VM={ReferenceEquals(AppSettingsPage.ViewModel, settings)}, Save={SaveMenuItem.IsEnabled}, SaveAs={SaveAsMenuItem.IsEnabled}, Export={ExportPngMenuItem.IsEnabled}, SaveAll={SaveAllMenuItem.IsEnabled}, Close={CloseTabMenuItem.IsEnabled}.");

            var themePicker = (ComboBox)AppSettingsPage.FindName("ThemePicker");
            var reopenToggle = (ToggleSwitch)AppSettingsPage.FindName("ReopenSavedTabsToggle");
            var languagePicker = (ComboBox)AppSettingsPage.FindName("LanguagePicker");
            var restartNotice = (InfoBar)AppSettingsPage.FindName("LanguageRestartInfoBar");
            var persistenceNotice = (InfoBar)AppSettingsPage.FindName("SettingsPersistenceInfoBar");
            var versionText = (TextBlock)AppSettingsPage.FindName("VersionText");

            settings.LoadPreferences(original with { Theme = DesktopThemePreference.Dark },
                workspaceCoordinator.EffectiveLanguage, workspaceCoordinator.StartupLanguagePreference);
            await Task.Yield();
            if (themePicker.SelectedIndex != 2 || preferenceEvents != 0 ||
                versionText.Text != settings.VersionText)
                throw new InvalidOperationException($"Loading settings bindings failed: Theme={themePicker.SelectedIndex}, Events={preferenceEvents}, Version='{versionText.Text}', ExpectedVersion='{settings.VersionText}', PageContext={AppSettingsPage.DataContext?.GetType().Name}, ThemeContext={themePicker.DataContext?.GetType().Name}, Loaded={themePicker.IsLoaded}.");

            reopenToggle.IsOn = !original.ReopenSavedTabs;
            await Task.Yield();
            if (settings.ReopenSavedTabs == original.ReopenSavedTabs ||
                desktopPreferences.ReopenSavedTabs != settings.ReopenSavedTabs || preferenceEvents != 1)
                throw new InvalidOperationException("Editing a settings control did not update the shared preferences exactly once.");

            languagePicker.SelectedItem = settings.Languages.First(language =>
                language.Tag is "fr-FR" or "ja-JP" &&
                language.Tag != workspaceCoordinator.StartupLanguagePreference);
            await Task.Yield();
            if (!settings.RestartRequired || !restartNotice.IsOpen || restartNotice.Message != settings.RestartMessage)
                throw new InvalidOperationException("Changing the language did not refresh the restart notice.");

            settings.ShowSettingsPersistenceFailure();
            await Task.Yield();
            if (!persistenceNotice.IsOpen || restartNotice.IsOpen)
                throw new InvalidOperationException("A settings persistence failure did not replace the restart notice.");
            settings.ClearSettingsPersistenceFailure();
            await Task.Yield();
            if (persistenceNotice.IsOpen || !restartNotice.IsOpen)
                throw new InvalidOperationException("Recovering settings persistence did not restore the restart notice.");
        }
        finally
        {
            settings.PreferencesChanged -= OnChanged;
            workspaceCoordinator.UpdatePreferences(original);
        }
    }
#endif
}
