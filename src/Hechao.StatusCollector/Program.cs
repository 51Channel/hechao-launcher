using System.Net.Http.Headers;
using System.Reflection;

namespace Hechao.StatusCollector;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var configPath = ReadConfigPath(args);
            var configuration = CollectorConfiguration.Load(configPath);
            var token = HeartbeatTokenStore.Read(configuration.TokenPath);

            using var cancellationSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationSource.Cancel();
            };

            var collector = new ServerHeartbeatCollector(new MinecraftStatusClient());
            var heartbeat = await collector.CollectAsync(
                configuration,
                cancellationSource.Token);

            using var httpHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            using var httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Hechao.StatusCollector", version));

            var apiClient = new ServerHeartbeatApiClient(httpClient);
            var response = await apiClient.SendAsync(
                new Uri(configuration.ApiEndpoint),
                token,
                heartbeat,
                cancellationSource.Token);
            Console.WriteLine(
                $"reported={response.ImportedServers} received_at={response.ReceivedAt:O}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("The heartbeat collection was cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Heartbeat collection failed: {exception.Message}");
            return 1;
        }
    }

    private static string ReadConfigPath(string[] args)
    {
        if (args.Length == 2 &&
            string.Equals(args[0], "--config", StringComparison.OrdinalIgnoreCase))
        {
            return args[1];
        }

        if (args.Length == 0)
        {
            return Path.Combine(AppContext.BaseDirectory, "server-heartbeats.json");
        }

        throw new ArgumentException("Usage: Hechao.StatusCollector [--config <path>]");
    }
}
