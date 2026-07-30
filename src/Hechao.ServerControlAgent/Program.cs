using Hechao.ServerControlAgent;
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
        Timeout = TimeSpan.FromSeconds(30)
    };
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Hechao.ServerControlAgent/0.1.0");
    var processRunner = new ProcessRunner();
    var backupRoot = Path.Combine(
        configuration.StateDirectory,
        "backups");
    var runtimeMarkerDirectory = Path.Combine(
        configuration.StateDirectory,
        "runtime");
    Directory.CreateDirectory(runtimeMarkerDirectory);
    var sharedPorts = configuration.Targets
        .GroupBy(target => target.Port)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToHashSet();
    var targets = configuration.Targets
        .Select(target => new ServerTargetRuntime(
            target,
            configuration.ConsoleSubmitScript,
            backupRoot,
            runtimeMarkerDirectory,
            sharedPorts.Contains(target.Port),
            processRunner))
        .ToArray();
    var worker = new ServerControlWorker(
        configuration,
        new AgentApiClient(httpClient, configuration.AgentId, token),
        targets,
        new CommandReceiptStore(configuration.StateDirectory),
        log);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => cancellation.Cancel();
    log.Write("INFO", "agent_started", configuration.AgentId);
    await worker.RunAsync(cancellation.Token);
    log.Write("INFO", "agent_stopped", configuration.AgentId);
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
