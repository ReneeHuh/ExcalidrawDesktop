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
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey("main");
        if (!mainInstance.IsCurrent)
        {
            DiagnosticLogService.Info("application.activation_redirected");
            await mainInstance.RedirectActivationToAsync(activation);
            Exit();
            return;
        }

        // Only the surviving main instance repairs file associations; a
        // redirected launch must not touch the registry on its way out.
        EnsureFileTypeRegistration();
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        mainInstance.Activated += OnInstanceActivated;
        var smokeOptions = ResolveSmokeOptions(args.Arguments);
        workspaceCoordinator = new ApplicationWorkspaceCoordinator(
            smokeOptions.WorkspaceStatePath,
            startupPreferences);
        await workspaceCoordinator.InitializeAsync();
        var restoredWindows = workspaceCoordinator.RestoredWindows;
        var firstRestoredWindow = restoredWindows.FirstOrDefault();
        window = new MainWindow(
            workspaceCoordinator,
            restoreWorkspace: true,
            smokeOptions: smokeOptions);
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

        var candidates = paths
            .Concat(includeProcessArguments
                ? Environment.GetCommandLineArgs().Skip(1)
                : [])
            .Select(path => path.Trim().Trim('"'));
        // SelectSupported canonicalises, filters to .excalidraw, skips
        // malformed paths and de-duplicates, so the same file passed twice
        // (relative and absolute, or differently cased) opens once.
        var activatedPaths = DesktopDropPaths.SelectSupported(candidates)
            .Where(File.Exists);
        workspaceCoordinator.QueueActivatedFiles(activatedPaths);
    }

    private static IEnumerable<string> ParseLaunchFile(string arguments)
    {
        var path = DesktopLaunchFile.ParseArguments(arguments);
        return path is not null && File.Exists(path)
                ? [path]
                : [];
    }

    /// <summary>
    /// Registers the .excalidraw association for this executable once per
    /// install location and version. The registration writes HKCU keys, so it
    /// is skipped when a stamp shows the same executable already did it.
    /// </summary>
    private static void EnsureFileTypeRegistration()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0";
            var stamp = $"{executablePath}|{version}";
            var stampPath = DesktopPaths.FileAssociationStampPath;
            if (ReadStamp(stampPath) == stamp)
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
            AtomicFile.WriteAllText(stampPath, stamp);
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

    private static string? ReadStamp(string stampPath)
    {
        try
        {
            return File.Exists(stampPath) ? File.ReadAllText(stampPath) : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Consumes the debug smoke-test request files dropped next to the
    /// executable by the SmokeTests scripts and selects the isolated
    /// workspace-state file for the active scenario.
    /// </summary>
    private DesktopSmokeOptions ResolveSmokeOptions(string launchArguments)
    {
#if DEBUG
        var baseDirectory = AppContext.BaseDirectory;
        string StatePath(string name) =>
            Path.Combine(baseDirectory, $"{name}-state.json");

        // Returns whether the request file existed, deleting it so the
        // scenario runs once. The file content, when present, is returned via
        // the out parameter for scenarios that carry parameters.
        bool Consume(string name, out string? content)
        {
            var path = Path.Combine(baseDirectory, $"{name}.request");
            content = null;
            if (!File.Exists(path))
            {
                return false;
            }

            content = File.ReadAllText(path);
            File.Delete(path);
            return true;
        }

        bool Requested(string name) => Consume(name, out _);

        var runTabSmoke = launchArguments.Contains(
                "--tab-smoke",
                StringComparison.Ordinal) ||
            Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, "--tab-smoke", StringComparison.Ordinal));
        runTabSmoke |= Requested("tab-smoke");
        var runWorkspaceSmoke = Requested("workspace-smoke");
        var runStartupSmoke = Requested("startup-smoke");
        var runRecoverySmoke = Requested("recovery-smoke");
        var verifyRecoverySmoke = Requested("recovery-restore");
        var runFileActivationSmoke = Requested("file-activation-smoke");
        var runTitleBarSmoke = Requested("titlebar-smoke");
        var runMultiWindowSmoke = Requested("multi-window-smoke");
        var runMultiWindowExitSmoke = Requested("multi-window-exit-smoke");
        var runMultiWindowDirtyExitSmoke = Requested("multi-window-dirty-exit-smoke");
        var runSuspensionSmoke = Requested("suspension-smoke");
        var runImageExportSmoke = Requested("image-export-smoke");
        var runDocumentSafetySmoke = Requested("document-safety-smoke");
        var runCloseDecisionsSmoke = Requested("close-decisions-smoke");
        Consume("performance-smoke", out var performanceRequest);
        var performanceTabCount = ParsePerformanceTabCount(performanceRequest);
        var performanceSuspendInactive = PerformanceModeIs(performanceRequest, ":suspend");
        var performanceUnloadInactive = PerformanceModeIs(performanceRequest, ":unload");
        // The localization request was already read by the constructor.
        Requested("localization-smoke");
        var runLocalizationSmoke = localizationSmokeLanguage is not null;
        if (runCloseDecisionsSmoke)
        {
            var closeDecisionsResultPath = Path.Combine(
                baseDirectory,
                "close-decisions-smoke.result");
            if (File.Exists(closeDecisionsResultPath))
            {
                File.Delete(closeDecisionsResultPath);
            }
        }

        // The first active scenario selects the workspace-state file, keeping
        // the scenarios isolated from each other and from the real workspace.
        (bool Active, string Name)[] stateSelection =
        [
            (runWorkspaceSmoke, "workspace-smoke"),
            (runTabSmoke, "tab-smoke"),
            (runStartupSmoke, "startup-smoke"),
            (runFileActivationSmoke, "file-activation-smoke"),
            (runTitleBarSmoke, "titlebar-smoke"),
            (runMultiWindowSmoke, "multi-window-smoke"),
            (runMultiWindowExitSmoke || runMultiWindowDirtyExitSmoke, "multi-window-exit-smoke"),
            (performanceTabCount > 0, "performance-smoke"),
            (runSuspensionSmoke, "suspension-smoke"),
            (runImageExportSmoke, "image-export-smoke"),
            (runDocumentSafetySmoke, "document-safety-smoke"),
            (runCloseDecisionsSmoke, "close-decisions-smoke"),
            (runLocalizationSmoke, "localization-smoke"),
            (runRecoverySmoke || verifyRecoverySmoke, "recovery-smoke"),
        ];
        var selectedState = stateSelection.FirstOrDefault(scenario => scenario.Active);
        return new DesktopSmokeOptions(
            RunTabSmoke: runTabSmoke,
            WorkspaceStatePath: selectedState.Active ? StatePath(selectedState.Name) : null,
            RunRecoverySmoke: runRecoverySmoke,
            VerifyRecoverySmoke: verifyRecoverySmoke,
            RunTitleBarSmoke: runTitleBarSmoke,
            PerformanceTabCount: performanceTabCount,
            RunSuspensionSmoke: runSuspensionSmoke,
            PerformanceSuspendInactive: performanceSuspendInactive,
            PerformanceUnloadInactive: performanceUnloadInactive,
            RunMultiWindowSmoke: runMultiWindowSmoke,
            RunMultiWindowExitSmoke: runMultiWindowExitSmoke,
            RunMultiWindowDirtyExitSmoke: runMultiWindowDirtyExitSmoke,
            RunImageExportSmoke: runImageExportSmoke,
            RunDocumentSafetySmoke: runDocumentSafetySmoke,
            RunCloseDecisionsSmoke: runCloseDecisionsSmoke,
            RunLocalizationSmoke: runLocalizationSmoke,
            LocalizationSmokeLanguage: localizationSmokeLanguage);
#else
        _ = launchArguments;
        return DesktopSmokeOptions.None;
#endif
    }

#if DEBUG
    private static int ParsePerformanceTabCount(string? request) =>
        request is not null &&
        int.TryParse(request.Split(':')[0], out var count) &&
        count is >= 1 and <= 20
            ? count
            : 0;

    private static bool PerformanceModeIs(string? request, string suffix) =>
        request is not null &&
        request.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
#endif
}
