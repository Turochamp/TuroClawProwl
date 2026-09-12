using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;
using TuroClawProwl.Infrastructure.Autostart;
using WinFormsApp = System.Windows.Forms.Application;
using TuroClawProwl.Infrastructure.Browser;
using TuroClawProwl.Infrastructure.Configuration;
using TuroClawProwl.Infrastructure.FileSystem;
using TuroClawProwl.Infrastructure.Gateway;
using TuroClawProwl.Infrastructure.Git;
using TuroClawProwl.Infrastructure.Logging;
using TuroClawProwl.Infrastructure.Security;
using TuroClawProwl.Infrastructure.Google;
using TuroClawProwl.Infrastructure.Ssh;
using TuroClawProwl.Infrastructure.Time;
using TuroClawProwl.Infrastructure.Toast;

namespace TuroClawProwl.App;

internal static class Program
{
    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TuroClawProwl");

    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        WinFormsApp.SetHighDpiMode(HighDpiMode.SystemAware);

        using var guard = new SingleInstanceGuard();
        if (!guard.IsPrimary)
        {
            SingleInstanceGuard.SignalExistingInstanceToShowSettings();
            return 1;
        }

        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(Path.Combine(AppDataDirectory, "logs"));

        using var serilogLogger = ConfigureSerilog();
        using var loggerFactory = new SerilogLoggerFactory(serilogLogger, dispose: false);

        var configStore = new JsonConfigStore(JsonConfigStore.DefaultConfigPath());
        var config = configStore.LoadAsync().GetAwaiter().GetResult();

        var tokenStore = new DpapiTokenStore(Path.Combine(AppDataDirectory, "token.dat"));
        var autostart = new RegistryAutostartManager();
        EnsureFirstRunAutostart(autostart, config);

        var uiContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(uiContext);
        var dispatcher = new WinFormsUiDispatcher(uiContext);

        using var trayController = new TrayController(dispatcher);

        var clock = new WallClock();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var retryPipeline = HttpGatewayClient.BuildDefaultRetryPipeline(
            loggerFactory.CreateLogger("TuroClawProwl.Infrastructure.Gateway.HttpGatewayClient"));
        IGatewayClient gatewayClient = string.IsNullOrWhiteSpace(config.GatewayUrl)
            ? new NullGatewayClient()
            : new HttpGatewayClient(httpClient, tokenStore, new Uri(config.GatewayUrl), retryPipeline);

        var gitRunner = new GitProcessRunner();
        var sshRunner = new OpenSshRunner();
        var browserLauncher = new DefaultBrowserLauncher();
        var toasts = new ToastNotificationsToastService();

        var sshTarget = new SshTarget(config.SshHost, config.SshUser);
        var hubRepoPath = BundleSourcePathsResolver.ResolveHubRepoPath(config);
        var watchedPaths = BundleSourcePathsResolver.Resolve(config, loggerFactory);

        var healthUseCase = new HandleHealthPollUseCase(
            gatewayClient, clock, toasts, trayController,
            loggerFactory.CreateLogger<HandleHealthPollUseCase>());
        var resolveTodayUseCase = new ResolveTodayFileStatusesUseCase(
            gitRunner, trayController,
            loggerFactory.CreateLogger<ResolveTodayFileStatusesUseCase>());
        var pushUseCase = new PushTodayFilesUseCase(
            gitRunner, toasts,
            loggerFactory.CreateLogger<PushTodayFilesUseCase>());
        var openControlUiUseCase = new OpenControlUiUseCase(
            new Uri(config.GatewayUrl), browserLauncher, tokenStore,
            loggerFactory.CreateLogger<OpenControlUiUseCase>());
        var restartUseCase = new RestartGatewayUseCase(sshTarget, sshRunner, toasts);

        var trackedForOrchestrator = TodayPathsResolver.ToOrchestratorPairs(watchedPaths);

        using var orchestrator = new AppOrchestrator(
            config, healthUseCase, resolveTodayUseCase, pushUseCase,
            openControlUiUseCase, restartUseCase,
            trackedForOrchestrator,
            loggerFactory.CreateLogger<AppOrchestrator>());

        var publisherVersion = "TuroClawProwl/" +
            (typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        var bundleRequest = new StateBundleRequest(
            hubRepoPath, publisherVersion, ConfigIntervals.EffectiveHeartbeatInterval(config));

        var reportBundleUseCase = new ReportBundlePublishUseCase(
            trayController, toasts, clock,
            loggerFactory.CreateLogger<ReportBundlePublishUseCase>());

        IBundlePublisher bundlePublisher = string.IsNullOrWhiteSpace(hubRepoPath)
            ? new UnconfiguredBundlePublisher()
            : new GitWorktreeBundlePublisher(
                hubRepoPath,
                BundleSourcePathsResolver.ResolveWorktreePath(config),
                BundleSourcePathsResolver.ResolvePublishBranch(config),
                logger: loggerFactory.CreateLogger<GitWorktreeBundlePublisher>());

        var googleReader = new GwsWorkspaceReader(
            string.IsNullOrWhiteSpace(config.GwsExecutablePath)
                ? "C:/Users/micha/bin/gws.cmd"
                : config.GwsExecutablePath,
            loggerFactory.CreateLogger<GwsWorkspaceReader>());

        var publishBundleUseCase = new PublishStateBundleUseCase(
            new FileSystemSourceFileReader(),
            new GitFileFactsReader(),
            googleReader,
            new JsonFileSnapshotStore(JsonFileSnapshotStore.DefaultDirectory()),
            bundlePublisher,
            clock,
            loggerFactory.CreateLogger<PublishStateBundleUseCase>());

        var publishAndReport = new PublishAndReportStateBundleUseCase(
            publishBundleUseCase, reportBundleUseCase,
            loggerFactory.CreateLogger<PublishAndReportStateBundleUseCase>());

        using var bundleSyncerHandle = new StateBundleSyncerHandle(
            bundleRequest, publishAndReport, loggerFactory);
        bundleSyncerHandle.Start(watchedPaths, ConfigIntervals.EffectiveSnapshotInterval(config));

        using var liveConfigApplier = new LiveConfigApplier(
            config, gatewayClient, openControlUiUseCase, restartUseCase,
            orchestrator, bundleSyncerHandle, googleReader, loggerFactory);
        configStore.ConfigSaved += (_, c) => _ = liveConfigApplier.ApplyAsync(c);

        trayController.PushTodayFilesRequested += async (_, _) => await orchestrator.PushTodayFilesAsync();
        trayController.OpenControlUiRequested += async (_, _) => await orchestrator.OpenControlUiAsync();
        trayController.RestartGatewayRequested += async (_, _) => await orchestrator.RestartGatewayAsync();
        trayController.OpenSettingsRequested += (_, _) => ShowSettings(configStore, tokenStore, autostart);
        trayController.ExitRequested += (_, _) => WinFormsApp.Exit();

        guard.ShowSettingsRequested += (_, _) =>
            dispatcher.PostAsync(() => ShowSettings(configStore, tokenStore, autostart)).GetAwaiter().GetResult();
        guard.StartListening();

        _ = orchestrator.StartAsync();

        // Fire-and-forget, but observed: PublishAndReportStateBundleUseCase
        // guards its own publish and report steps, so this should only ever
        // fault on something neither guard anticipated. Attaching a
        // faulted-only continuation is what stands between that residual
        // case and a silently unobserved task exception -- the exact defect
        // this project exists to remove, reintroduced at the one remaining
        // unguarded call site if this were left bare.
        var startupPublishLogger = loggerFactory.CreateLogger("StartupPublish");
        _ = bundleSyncerHandle.PublishNowAsync().ContinueWith(
            t => startupPublishLogger.LogError(t.Exception, "Startup state bundle publish threw"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        WinFormsApp.Run();
        return 0;
    }

    private static void EnsureFirstRunAutostart(IAutostartManager autostart, TuroClawProwlConfig config)
    {
        if (!config.AutostartEnabled) return;
        if (autostart.IsEnabled()) return;

        var exePath = Environment.ProcessPath ?? WinFormsApp.ExecutablePath;
        autostart.Enable(exePath);
    }

    private static void ShowSettings(IConfigStore configStore, ITokenStore tokenStore, IAutostartManager autostart)
    {
        using var form = new SettingsForm(configStore, tokenStore, autostart);
        form.ShowDialog();
    }

    private static Serilog.Core.Logger ConfigureSerilog()
    {
#if DEBUG
        var minLevel = LogEventLevel.Debug;
#else
        var minLevel = LogEventLevel.Information;
#endif
        return new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .Destructure.With(new SensitivePropertyMaskingPolicy())
            .Filter.With(new RepeatedMessageDeduplicator(TimeSpan.FromMinutes(5)))
            .WriteTo.File(
                path: Path.Combine(AppDataDirectory, "logs", "prowl-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();
    }
}

internal sealed class NullGatewayClient : IGatewayClient
{
    public Task<GatewayPollResult> GetHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<GatewayPollResult>(new GatewayPollResult.Failure("gateway URL not configured"));
}

internal sealed class UnconfiguredBundlePublisher : IBundlePublisher
{
    public Task<string> GetSourceBranchAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("unknown");

    public Task<BundlePublishResult> PublishAsync(
        BundlePayload payload,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<BundlePublishResult>(new BundlePublishResult.Misconfigured(
            GitWorktreeBundlePublisher.SourceRepoSetting,
            "the hub repo path is not configured"));
}
