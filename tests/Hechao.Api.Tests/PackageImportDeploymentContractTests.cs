using Hechao.Api.PackageImports;

namespace Hechao.Api.Tests;

public sealed class PackageImportDeploymentContractTests
{
    [Fact]
    public void SystemdSandbox_AllowsPackageAndReleaseManifestWrites()
    {
        var unit = ReadRepositoryFile(
            "deploy",
            "linux",
            "hechao-launcher-api.service");
        var readWritePaths = unit
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single(line => line.StartsWith("ReadWritePaths=", StringComparison.Ordinal));

        Assert.Contains(
            "-/var/lib/hechao-launcher-api/package-imports",
            readWritePaths,
            StringComparison.Ordinal);
        Assert.Contains(
            "-/var/lib/hechao-launcher-api/manifests",
            readWritePaths,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionSetup_KeepsManifestRootReadOnlyAndOwnsReleaseSubtree()
    {
        var script = ReadRepositoryFile(
            "deploy",
            "linux",
            "configure-distribution.sh");

        Assert.Contains(
            "install -d -o root -g hechao-api -m 0750 \"$manifest_directory\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "install -d -o hechao-api -g hechao-api -m 0750 \"$manifest_release_directory\"",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Orchestration_OnlyQueuesDeployAndPublishesTestChannel()
    {
        var source = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "PackageImports",
            "PackageImportOrchestrationRepository.cs");
        var queueStart = source.IndexOf(
            "private static async Task InsertDeploymentOperationAsync",
            StringComparison.Ordinal);
        var queueEnd = source.IndexOf(
            "private static async Task<bool> IsUsableReleaseAsync",
            queueStart,
            StringComparison.Ordinal);
        var queue = source[queueStart..queueEnd];

        Assert.Contains("'DeployPackage'", queue, StringComparison.Ordinal);
        Assert.DoesNotContain("'Start'", queue, StringComparison.Ordinal);
        Assert.DoesNotContain("'Stop'", queue, StringComparison.Ordinal);
        Assert.Contains("channel = 'test'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("channel = 'production'", source, StringComparison.Ordinal);
        Assert.Contains("SET is_active = true", source, StringComparison.Ordinal);
        Assert.Contains("enabled_for_test", source, StringComparison.Ordinal);

        var endpoints = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "PackageImports",
            "PackageImportEndpoints.cs");
        Assert.Contains(
            "GetPublisherAgentStateAsync",
            endpoints,
            StringComparison.Ordinal);
        Assert.Contains(
            "target.Target.ActiveOperation is not null",
            endpoints,
            StringComparison.Ordinal);
        Assert.DoesNotContain("内存硬上限", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("hardLimit", source, StringComparison.Ordinal);
        Assert.Contains(
            "server_files_present",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogSynchronization_IsHiddenClosedActivityEntry()
    {
        var source = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "PackageImports",
            "PackageImportOrchestrationRepository.cs");

        Assert.Contains("'Closed'", source, StringComparison.Ordinal);
        Assert.Contains("is_visible = false", source, StringComparison.Ordinal);
        Assert.Contains("short_name = $3", source, StringComparison.Ordinal);
        Assert.Contains("icon_glyph = $4", source, StringComparison.Ordinal);
        Assert.Contains("announcement = ''", source, StringComparison.Ordinal);
        Assert.Contains(
            "PackageImportRules.ActivityVelocityTarget",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackageArchiveDownload_IsRangeEnabledAndLeaseAuthorized()
    {
        var endpoint = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "ServerControl",
            "ServerControlPackageEndpoints.cs");
        var repository = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "ServerControl",
            "ServerControlRepository.cs");

        Assert.Contains("enableRangeProcessing: true", endpoint, StringComparison.Ordinal);
        Assert.Contains("command.claim_expires_at >= $3", repository, StringComparison.Ordinal);
        Assert.Contains("PackageImportRules.IsActivityTarget(", repository, StringComparison.Ordinal);
        Assert.Contains("package.status = 'DeployingServer'", repository, StringComparison.Ordinal);
        Assert.Contains("id = ANY($3) THEN $4", repository, StringComparison.Ordinal);
        Assert.Contains("ELSE LEAST(claim_expires_at, $2)", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void PublisherHeartbeat_RenewsOnlyTheActivelyProcessedImport()
    {
        var repository = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "PackageImports",
            "PackageImportRepository.cs");

        Assert.Contains("WHEN id = $3 THEN $2", repository, StringComparison.Ordinal);
        Assert.Contains(
            "ELSE LEAST(publisher_lease_expires_at, $4)",
            repository,
            StringComparison.Ordinal);
        Assert.Contains("request.ActiveImportId", repository, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), Path.Combine(segments)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "Hechao.Launcher.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
