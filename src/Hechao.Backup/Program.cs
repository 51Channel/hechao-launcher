using System.Text.Json;

namespace Hechao.Backup;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            var options = CommandOptions.Parse(args[1..]);
            switch (args[0])
            {
                case "keygen":
                    var keyId = BackupEnvelope.GenerateKeyPair(
                        options.Required("public-key"),
                        options.Required("private-key"),
                        options.Required("passphrase-file"));
                    PrintJson(new { keyId });
                    return 0;

                case "encrypt":
                    var encrypted = BackupEnvelope.Encrypt(
                        options.Required("input"),
                        options.Required("output"),
                        options.Required("public-key"));
                    PrintJson(encrypted);
                    return 0;

                case "decrypt":
                    var decrypted = BackupEnvelope.Decrypt(
                        options.Required("input"),
                        options.Required("output"),
                        options.Required("private-key"),
                        options.Required("passphrase-file"));
                    PrintJson(decrypted);
                    return 0;

                case "inspect":
                    PrintJson(
                        BackupEnvelope.Inspect(
                            options.Required("input")));
                    return 0;

                case "upload":
                    await using (var uploadClient = CreateClient(options))
                    {
                        var result = await uploadClient.UploadAsync(
                            options.Required("bucket"),
                            options.Required("key"),
                            options.Required("input"),
                            CancellationToken.None);
                        PrintJson(result);
                    }
                    return 0;

                case "download":
                    await using (var downloadClient = CreateClient(options))
                    {
                        var sha256 = await downloadClient.DownloadAsync(
                            options.Required("bucket"),
                            options.Required("key"),
                            options.Required("output"),
                            options.Optional("expected-sha256"),
                            CancellationToken.None);
                        PrintJson(new { sha256 });
                    }
                    return 0;

                default:
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Hechao backup operation failed: {exception.Message}");
            return 1;
        }
    }

    private static AsyncDisposableClient CreateClient(CommandOptions options) =>
        new(
            OssBackupClient.FromEnvironment(
                options.Required("region"),
                options.Required("endpoint")));

    private static void PrintJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            """
            Hechao.Backup 0.1.0

            keygen  --public-key <pem> --private-key <p8> --passphrase-file <path>
            encrypt --input <dump> --output <hcbackup> --public-key <pem>
            decrypt --input <hcbackup> --output <dump> --private-key <p8> --passphrase-file <path>
            inspect --input <hcbackup>
            upload   --input <hcbackup> --bucket <name> --region <region> --endpoint <https-url> --key <key>
            download --output <hcbackup> --bucket <name> --region <region> --endpoint <https-url> --key <key> [--expected-sha256 <hex>]

            OSS commands read OSS_ACCESS_KEY_ID and OSS_ACCESS_KEY_SECRET.
            """);
    }

    private sealed class AsyncDisposableClient(
        OssBackupClient client) : IAsyncDisposable
    {
        internal Task<OssBackupUploadResult> UploadAsync(
            string bucket,
            string key,
            string inputPath,
            CancellationToken cancellationToken) =>
            client.UploadAsync(
                bucket,
                key,
                inputPath,
                cancellationToken);

        internal Task<string> DownloadAsync(
            string bucket,
            string key,
            string outputPath,
            string? expectedSha256,
            CancellationToken cancellationToken) =>
            client.DownloadAsync(
                bucket,
                key,
                outputPath,
                expectedSha256,
                cancellationToken);

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CommandOptions(
        IReadOnlyDictionary<string, string> values)
    {
        internal static CommandOptions Parse(string[] args)
        {
            if (args.Length % 2 != 0)
            {
                throw new ArgumentException(
                    "Every command option requires a value.");
            }

            var values = new Dictionary<string, string>(
                StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                var name = args[index];
                if (!name.StartsWith("--", StringComparison.Ordinal) ||
                    name.Length < 3 ||
                    !values.TryAdd(name[2..], args[index + 1]))
                {
                    throw new ArgumentException(
                        $"Invalid or repeated command option: {name}");
                }
            }

            return new CommandOptions(values);
        }

        internal string Required(string name) =>
            values.TryGetValue(name, out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException(
                    $"Missing required option --{name}.");

        internal string? Optional(string name) =>
            values.TryGetValue(name, out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
    }
}
