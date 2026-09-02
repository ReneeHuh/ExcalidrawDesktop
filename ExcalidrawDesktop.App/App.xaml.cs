using ExcalidrawDesktop.Core;
using ExcalidrawDesktop.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace ExcalidrawDesktop.App;

public partial class App : Application
{
    private MainWindow? window;
    private ApplicationWorkspaceCoordinator? workspaceCoordinator;
    private DispatcherQueue? dispatcherQueue;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey("main");
        if (!mainInstance.IsCurrent)
        {
            await mainInstance.RedirectActivationToAsync(activation);
            Exit();
            return;
        }

        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        mainInstance.Activated += OnInstanceActivated;
#if DEBUG
        var tabSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "tab-smoke.request");
        var runTabSmoke = args.Arguments.Contains(
                "--tab-smoke",
                StringComparison.Ordinal) ||
            Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, "--tab-smoke", StringComparison.Ordinal)) ||
            File.Exists(tabSmokeRequestPath);
        if (File.Exists(tabSmokeRequestPath))
        {
            File.Delete(tabSmokeRequestPath);
        }
        var workspaceSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "workspace-smoke.request");
        var startupSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "startup-smoke.request");
        var recoverySmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "recovery-smoke.request");
        var recoveryRestoreRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "recovery-restore.request");
        var fileActivationSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "file-activation-smoke.request");
        var titleBarSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "titlebar-smoke.request");
        var multiWindowSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "multi-window-smoke.request");
        var multiWindowExitSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "multi-window-exit-smoke.request");
        var multiWindowDirtyExitSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "multi-window-dirty-exit-smoke.request");
        var performanceSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "performance-smoke.request");
        var suspensionSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "suspension-smoke.request");
        var imageExportSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "image-export-smoke.request");
        var runRecoverySmoke = File.Exists(recoverySmokeRequestPath);
        var verifyRecoverySmoke = File.Exists(recoveryRestoreRequestPath);
        var runTitleBarSmoke = File.Exists(titleBarSmokeRequestPath);
        var runMultiWindowSmoke = File.Exists(multiWindowSmokeRequestPath);
        var runMultiWindowExitSmoke = File.Exists(multiWindowExitSmokeRequestPath);
        var runMultiWindowDirtyExitSmoke = File.Exists(
            multiWindowDirtyExitSmokeRequestPath);
        var performanceTabCount = ReadPerformanceTabCount(
            performanceSmokeRequestPath);
        var performanceSuspendInactive = ReadPerformanceSuspendMode(
            performanceSmokeRequestPath);
        var performanceUnloadInactive = ReadPerformanceUnloadMode(
            performanceSmokeRequestPath);
        var runSuspensionSmoke = File.Exists(suspensionSmokeRequestPath);
        var runImageExportSmoke = File.Exists(imageExportSmokeRequestPath);
        string? workspaceStatePath = File.Exists(workspaceSmokeRequestPath)
            ? Path.Combine(AppContext.BaseDirectory, "workspace-smoke-state.json")
            : runTabSmoke
                ? Path.Combine(AppContext.BaseDirectory, "tab-smoke-state.json")
                : File.Exists(startupSmokeRequestPath)
                    ? Path.Combine(AppContext.BaseDirectory, "startup-smoke-state.json")
                    : File.Exists(fileActivationSmokeRequestPath)
                        ? Path.Combine(AppContext.BaseDirectory, "file-activation-smoke-state.json")
                    : runTitleBarSmoke
                        ? Path.Combine(AppContext.BaseDirectory, "titlebar-smoke-state.json")
                    : runMultiWindowSmoke
                        ? Path.Combine(AppContext.BaseDirectory, "multi-window-smoke-state.json")
                    : runMultiWindowExitSmoke || runMultiWindowDirtyExitSmoke
                        ? Path.Combine(AppContext.BaseDirectory, "multi-window-exit-smoke-state.json")
                    : performanceTabCount > 0
                        ? Path.Combine(AppContext.BaseDirectory, "performance-smoke-state.json")
                    : runSuspensionSmoke
                        ? Path.Combine(AppContext.BaseDirectory, "suspension-smoke-state.json")
                    : runImageExportSmoke
                        ? Path.Combine(AppContext.BaseDirectory, "image-export-smoke-state.json")
                    : runRecoverySmoke || verifyRecoverySmoke
                        ? Path.Combine(AppContext.BaseDirectory, "recovery-smoke-state.json")
                        : null;
        if (File.Exists(workspaceSmokeRequestPath))
        {
            File.Delete(workspaceSmokeRequestPath);
        }
        if (File.Exists(startupSmokeRequestPath))
        {
            File.Delete(startupSmokeRequestPath);
        }
        if (File.Exists(recoverySmokeRequestPath))
        {
            File.Delete(recoverySmokeRequestPath);
        }
        if (File.Exists(recoveryRestoreRequestPath))
        {
            File.Delete(recoveryRestoreRequestPath);
        }
        if (File.Exists(fileActivationSmokeRequestPath))
        {
            File.Delete(fileActivationSmokeRequestPath);
        }
        if (File.Exists(titleBarSmokeRequestPath))
        {
            File.Delete(titleBarSmokeRequestPath);
        }
        if (File.Exists(multiWindowSmokeRequestPath))
        {
            File.Delete(multiWindowSmokeRequestPath);
        }
        if (File.Exists(multiWindowExitSmokeRequestPath))
        {
            File.Delete(multiWindowExitSmokeRequestPath);
        }
        if (File.Exists(multiWindowDirtyExitSmokeRequestPath))
        {
            File.Delete(multiWindowDirtyExitSmokeRequestPath);
        }
        if (File.Exists(performanceSmokeRequestPath))
        {
            File.Delete(performanceSmokeRequestPath);
        }
        if (File.Exists(suspensionSmokeRequestPath))
        {
            File.Delete(suspensionSmokeRequestPath);
        }
        if (File.Exists(imageExportSmokeRequestPath))
        {
            File.Delete(imageExportSmokeRequestPath);
        }
#else
        const bool runTabSmoke = false;
        const string? workspaceStatePath = null;
        const bool runRecoverySmoke = false;
        const bool verifyRecoverySmoke = false;
        const bool runTitleBarSmoke = false;
        const bool runMultiWindowSmoke = false;
        const bool runMultiWindowExitSmoke = false;
        const bool runMultiWindowDirtyExitSmoke = false;
        const int performanceTabCount = 0;
        const bool performanceSuspendInactive = false;
        const bool performanceUnloadInactive = false;
        const bool runSuspensionSmoke = false;
        const bool runImageExportSmoke = false;
#endif
        workspaceCoordinator = new ApplicationWorkspaceCoordinator(workspaceStatePath);
        await workspaceCoordinator.InitializeAsync();
        var restoredWindows = workspaceCoordinator.RestoredWindows;
        var firstRestoredWindow = restoredWindows.FirstOrDefault();
        window = new MainWindow(
            workspaceCoordinator,
            runTabSmoke,
            workspaceStatePath,
            runRecoverySmoke,
            verifyRecoverySmoke,
            runTitleBarSmoke,
            performanceTabCount,
            runSuspensionSmoke,
            performanceSuspendInactive,
            performanceUnloadInactive,
            restoreWorkspace: true,
            runMultiWindowSmoke: runMultiWindowSmoke,
            runMultiWindowExitSmoke: runMultiWindowExitSmoke,
            runMultiWindowDirtyExitSmoke: runMultiWindowDirtyExitSmoke,
            runImageExportSmoke: runImageExportSmoke);
        workspaceCoordinator.RegisterWindow(window, firstRestoredWindow);
        window.Activate();
        MainWindow? restoredActiveWindow =
            workspaceCoordinator.IsRestoredLastActiveWindow(window) ? window : null;
        foreach (var restoredWindow in restoredWindows.Skip(1))
        {
            var additionalWindow = workspaceCoordinator.CreateWindow(
                activate: true,
                createInitialTab: true,
                restoreState: restoredWindow);
            if (workspaceCoordinator.IsRestoredLastActiveWindow(additionalWindow))
            {
                restoredActiveWindow = additionalWindow;
            }
        }
        restoredActiveWindow?.Activate();
        QueueActivatedFiles(activation, includeProcessArguments: true);
    }

    private void OnInstanceActivated(object? sender, AppActivationArguments args)
    {
        dispatcherQueue?.TryEnqueue(() =>
        {
            workspaceCoordinator?.ActivateMostRecentWindow();
            QueueActivatedFiles(args, includeProcessArguments: false);
        });
    }

    private void QueueActivatedFiles(
        AppActivationArguments activation,
        bool includeProcessArguments)
    {
        if (workspaceCoordinator is null)
        {
            return;
        }

        IEnumerable<string> paths = activation.Kind switch
        {
            ExtendedActivationKind.File
                when activation.Data is IFileActivatedEventArgs fileActivation =>
                fileActivation.Files
                .OfType<StorageFile>()
                .Select(file => file.Path),
            ExtendedActivationKind.Launch
                when activation.Data is ILaunchActivatedEventArgs launchActivation =>
                ParseLaunchFile(launchActivation.Arguments),
            _ => [],
        };

        var activatedPaths = paths
            .Concat(includeProcessArguments
                ? Environment.GetCommandLineArgs().Skip(1)
                : [])
            .Where(path => string.Equals(
                Path.GetExtension(path.Trim('"')),
                ".excalidraw",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => path.Trim().Trim('"'))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        workspaceCoordinator.QueueActivatedFiles(activatedPaths);
    }

    private static IEnumerable<string> ParseLaunchFile(string arguments)
    {
        var path = DesktopLaunchFile.ParseArguments(arguments);
        return path is not null && File.Exists(path)
                ? [path]
                : [];
    }

#if DEBUG
    private static int ReadPerformanceTabCount(string requestPath)
    {
        if (!File.Exists(requestPath))
        {
            return 0;
        }

        var value = File.ReadAllText(requestPath);
        return int.TryParse(value.Split(':')[0], out var count) &&
            count is >= 1 and <= 20
                ? count
                : 0;
    }

    private static bool ReadPerformanceSuspendMode(string requestPath) =>
        File.Exists(requestPath) &&
        File.ReadAllText(requestPath).EndsWith(
            ":suspend",
            StringComparison.OrdinalIgnoreCase);

    private static bool ReadPerformanceUnloadMode(string requestPath) =>
        File.Exists(requestPath) &&
        File.ReadAllText(requestPath).EndsWith(
            ":unload",
            StringComparison.OrdinalIgnoreCase);
#endif
}
