using Hechao.ServerControlAgent;
using System.Reflection;
using System.Text.Json;

if (args.Length != 2 ||
    !string.Equals(args[0], "--config", StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        "Usage: Hechao.ServerControlAgent --config <absolute-json-path>");
    return 64;
}

try
{
    var configuration = ServerControlAgentConfiguration.Load(args[1]);
    Directory.CreateDirectory(configuration.StateDirectory);
    var log = new AgentLog(Path.Combine(
        configuration.StateDirectory,
        "logs",
        "server-control-agent.log"));
    var token = ProtectedTokenStore.Read(configuration.TokenPath);
    var handler = new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression =
            System.Net.DecompressionMethods.GZip |
            System.Net.DecompressionMethods.Deflate |
            System.Net.DecompressionMethods.Brotli,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseProxy = false
    };
    using var httpClient = new HttpClient(handler)
    {
        BaseAddress = new Uri(
            configuration.ApiBaseUrl.TrimEnd('/') + "/",
            UriKind.Absolute),
        Timeout = Timeout.InfiniteTimeSpan
    };
    var agentVersion = Assembly.GetExecutingAssembly()
        .GetName()
        .Version?
        .ToString(3) ?? "0.0.0";
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
        $"Hechao.ServerControlAgent/{agentVersion}");
    var processRunner = new ProcessRunner();
    var hostMemory = HostMemoryCapacity.Capture();
    var backupRoot = Path.Combine(
        configuration.StateDirectory,
        "backups");
    var runtimeMarkerDirectory = Path.Combine(
        configuration.StateDirectory,
        "runtime");
    Directory.CreateDirectory(runtimeMarkerDirectory);
    var dynamicSlotStore = new DynamicDeploymentSlotStore(configuration);
    var allConfigurations = configuration.Targets
        .Concat(dynamicSlotStore.Snapshot())
        .ToArray();
    configuration.ValidateDynamicTargets(dynamicSlotStore.Snapshot());
    var sharedPorts = allConfigurations
        .GroupBy(target => target.Port)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToHashSet();
    ServerTargetRuntime CreateRuntime(ServerControlTargetConfiguration target) =>
        new(
            target,
            configuration.ConsoleSubmitScript,
            backupRoot,
            runtimeMarkerDirectory,
            sharedPorts.Contains(target.Port),
            processRunner,
            managedMaximumMemoryMiB:
                hostMemory.ResolveManagedMaximumMemoryMiB(target));
    var targetRegistry = new ServerTargetRegistry(
        allConfigurations
            .Select(CreateRuntime)
            .ToArray());
    var slotProvisioner = new DynamicDeploymentSlotProvisioner(
        configuration,
        dynamicSlotStore,
        targetRegistry,
        CreateRuntime,
        processRunner,
        backupRoot,
        runtimeMarkerDirectory);
    var worker = new ServerControlWorker(
        configuration,
        new AgentApiClient(httpClient, configuration.AgentId, token),
        targetRegistry,
        slotProvisioner,
        new CommandReceiptStore(configuration.StateDirectory),
        log,
        hostMemory.TotalMemoryMiB);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => cancellation.Cancel();
    log.WriteBestEffort("INFO", "agent_started", configuration.AgentId);
    await worker.RunAsync(cancellation.Token);
    log.WriteBestEffort("INFO", "agent_stopped", configuration.AgentId);
    return 0;
}
catch (Exception exception) when (
    exception is IOException or UnauthorizedAccessException or
        InvalidDataException or InvalidOperationException or
        System.Security.Cryptography.CryptographicException or
        JsonException)
{
    Console.Error.WriteLine(AgentLog.Sanitize(exception.Message, 1000));
    return 1;
}
