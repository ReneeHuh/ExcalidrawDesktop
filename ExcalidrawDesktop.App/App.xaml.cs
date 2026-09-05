using ExcalidrawDesktop.Core;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace ExcalidrawDesktop.App;

public partial class App : Application
{
    private readonly DesktopPreferences startupPreferences;
#if DEBUG
    private readonly string localizationSmokeRequestPath = Path.Combine(
        AppContext.BaseDirectory,
        "localization-smoke.request");
    private readonly string? localizationSmokeLanguage;
#endif
    private MainWindow? window;
    private ApplicationWorkspaceCoordinator? workspaceCoordinator;
    private DispatcherQueue? dispatcherQueue;

    public App()
    {
        DiagnosticLogService.Initialize();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        var preferences = new DesktopSettingsStore().Load();
#if DEBUG
        if (File.Exists(localizationSmokeRequestPath))
        {
            var requestedLanguage = File.ReadAllText(
                localizationSmokeRequestPath).Trim();
            localizationSmokeLanguage = DesktopLanguages.Supported
                .FirstOrDefault(language => string.Equals(
                    language.PreferenceTag,
                    requestedLanguage,
                    StringComparison.OrdinalIgnoreCase))
                ?.PreferenceTag;
            if (localizationSmokeLanguage is not null)
            {
                preferences = preferences with { Language = localizationSmokeLanguage };
            }
        }
#endif
        startupPreferences = preferences;
        DesktopLanguageStartup.ApplyBeforeXaml(startupPreferences.Language);
        DesktopResources.ConfigureLanguage(
            Microsoft.Windows.Globalization.ApplicationLanguages.Languages
                .FirstOrDefault() ??
            DesktopLanguageStartup.ResolveEffective(
                startupPreferences.Language).WinUiTag);
        InitializeComponent();
    }

    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        DiagnosticLogService.Error("exception.xaml_unhandled", args.Exception);
#if DEBUG
        if (localizationSmokeLanguage is null)
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "localization-smoke-crash.txt"),
            args.Exception.ToString());
#endif
    }

    private static void OnDomainUnhandledException(
        object sender,
        System.UnhandledExceptionEventArgs args)
    {
        var exception = args.ExceptionObject as Exception ??
            new InvalidOperationException(args.ExceptionObject?.ToString());
        DiagnosticLogService.Error(
            "exception.runtime_unhandled",
            exception,
            new { args.IsTerminating });
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        DiagnosticLogService.Error("exception.task_unobserved", args.Exception);
        args.SetObserved();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        DiagnosticLogService.Info("application.launch_requested");
        EnsureFileTypeRegistration();
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey("main");
        if (!mainInstance.IsCurrent)
        {
            DiagnosticLogService.Info("application.activation_redirected");
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
        var documentSafetySmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "document-safety-smoke.request");
        var closeDecisionsSmokeRequestPath = Path.Combine(
            AppContext.BaseDirectory,
            "close-decisions-smoke.request");
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
        var runDocumentSafetySmoke = File.Exists(documentSafetySmokeRequestPath);
        var runCloseDecisionsSmoke = File.Exists(closeDecisionsSmokeRequestPath);
        var runLocalizationSmoke = localizationSmokeLanguage is not null;
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
                    : runDocumentSafetySmoke
                        ? Path.Combine(AppContext.BaseDirectory, "document-safety-smoke-state.json")
                    : runCloseDecisionsSmoke
                        ? Path.Combine(AppContext.BaseDirectory, "close-decisions-smoke-state.json")
                    : runLocalizationSmoke
                        ? Path.Combine(AppContext.BaseDirectory, "localization-smoke-state.json")
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
        if (File.Exists(documentSafetySmokeRequestPath))
        {
            File.Delete(documentSafetySmokeRequestPath);
        }
        if (File.Exists(closeDecisionsSmokeRequestPath))
        {
            File.Delete(closeDecisionsSmokeRequestPath);
        }
        var closeDecisionsSmokeResultPath = Path.Combine(
            AppContext.BaseDirectory,
            "close-decisions-smoke.result");
        if (runCloseDecisionsSmoke && File.Exists(closeDecisionsSmokeResultPath))
        {
            File.Delete(closeDecisionsSmokeResultPath);
        }
        if (File.Exists(localizationSmokeRequestPath))
        {
            File.Delete(localizationSmokeRequestPath);
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
        const bool runDocumentSafetySmoke = false;
        const bool runCloseDecisionsSmoke = false;
        const bool runLocalizationSmoke = false;
        const string? localizationSmokeLanguage = null;
#endif
        workspaceCoordinator = new ApplicationWorkspaceCoordinator(
            workspaceStatePath,
            startupPreferences);
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
            runImageExportSmoke: runImageExportSmoke,
            runDocumentSafetySmoke: runDocumentSafetySmoke,
            runCloseDecisionsSmoke: runCloseDecisionsSmoke,
            runLocalizationSmoke: runLocalizationSmoke,
            localizationSmokeLanguage: localizationSmokeLanguage);
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
        DiagnosticLogService.Info("application.launch_completed", new
        {
            restoredWindowCount = restoredWindows.Count,
            windowCount = workspaceCoordinator.Windows.Count,
        });
    }

    private void OnInstanceActivated(object? sender, AppActivationArguments args)
    {
        DiagnosticLogService.Info("application.instance_activated", new
        {
            activationKind = args.Kind.ToString(),
        });
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

    private static void EnsureFileTypeRegistration()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            ActivationRegistrationManager.RegisterForFileTypeActivation(
                [".excalidraw"],
                $"{executablePath},0",
                DesktopResources.Get(
                    "ExcalidrawFileTypeDisplayName",
                    "Excalidraw drawing"),
                ["open"],
                executablePath);
            DiagnosticLogService.Info("application.file_type_registered");
        }
        catch (Exception exception)
        {
            // File association repair is best effort and must never block the
            // editor from opening directly or accepting command-line paths.
            DiagnosticLogService.Error(
                "application.file_type_registration_failed",
                exception);
        }
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
