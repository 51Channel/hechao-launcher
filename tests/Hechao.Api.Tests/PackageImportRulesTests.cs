using Hechao.Api.PackageImports;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class PackageImportRulesTests
{
    [Fact]
    public void ValidateUpload_RejectsPathsAndOversizedArchives()
    {
        var options = new PackageImportOptions
        {
            MaximumUploadBytes = 64 * 1024 * 1024
        };

        var errors = PackageImportRules.Validate(
            new AdminPackageUploadCreateRequest(
                "../activity.zip",
                options.MaximumUploadBytes + 1),
            options);

        Assert.Contains("fileName", errors.Keys);
        Assert.Contains("totalBytes", errors.Keys);
    }

    [Fact]
    public void ValidateConfirmation_RequiresExactPhraseAndCleanAnalysis()
    {
        var importId = Guid.NewGuid();
        var import = CreateImport(importId, blocking: false);
        var request = new AdminPackageImportConfirmRequest(
            import.Revision,
            "summer-fabric-1.20.1",
            "夏日活动",
            "1.0.0",
            PackageImportRules.ActivityServerId,
            PreserveWorldData: false,
            SyncServerCatalog: true,
            "夏日活动",
            AccessTier.Participant,
            4096,
            $"发布并部署 {importId:D} 到 {PackageImportRules.ActivityServerId}",
            DeployServer: true);

        Assert.Empty(PackageImportRules.Validate(request, import));
        Assert.Contains(
            "confirmation",
            PackageImportRules.Validate(
                request with { Confirmation = "确认" },
                import).Keys);
        Assert.Contains(
            "analysis",
            PackageImportRules.Validate(request, CreateImport(importId, blocking: true)).Keys);
        Assert.Empty(PackageImportRules.Validate(
            request with { MaximumMemoryMiB = 32768 },
            import));
        Assert.Contains(
            "maximumMemoryMiB",
            PackageImportRules.Validate(
                request with { MaximumMemoryMiB = 65792 },
                import).Keys);
        Assert.Empty(PackageImportRules.Validate(
            request with
            {
                DeployServer = false,
                Confirmation = $"发布并入库 {importId:D}"
            },
            import));
    }

    [Fact]
    public void IsActivityTarget_RejectsSurvivalAndAcceptsOwl5ActivitySlot()
    {
        var target = new AdminServerControlTargetRecord(
            PackageImportRules.ActivityServerId,
            "范街",
            PackageImportRules.ActivityAgentId,
            PackageImportRules.ActivityConflictGroup,
            PackageImportRules.ActivityPort,
            true,
            DateTimeOffset.UtcNow,
            false,
            null,
            new ServerQuickSettings(20, 10, 10, "normal", true, 2048, 4096, 8192),
            ["list"],
            string.Empty,
            null,
            null,
            PackageDeploymentEnabled: true);

        Assert.True(PackageImportRules.IsActivityTarget(target));
        Assert.False(PackageImportRules.IsActivityTarget(
            target with { ServerId = "fanstreet" }));
        Assert.False(PackageImportRules.IsActivityTarget(
            target with { AgentId = "owl9" }));
        Assert.False(PackageImportRules.IsActivityTarget(
            target with { PackageDeploymentEnabled = false }));
        Assert.False(PackageImportRules.IsActivityTarget(
            target with { ConflictGroup = "owl5-survival-slot", Port = 25565 }));
    }

    [Fact]
    public void IsPackageDeploymentTarget_AcceptsAnyExplicitValidTarget()
    {
        var activity = CreateControlTarget(
            PackageImportRules.ActivityServerId,
            PackageImportRules.ActivityAgentId,
            PackageImportRules.ActivityPort,
            PackageImportRules.ActivityConflictGroup);
        var survival = CreateControlTarget(
            "survival2",
            "owl5",
            25565,
            "owl5-survival-slot");

        Assert.True(PackageImportRules.IsPackageDeploymentTarget(activity));
        Assert.True(PackageImportRules.IsPackageDeploymentTarget(survival));
        Assert.False(PackageImportRules.IsPackageDeploymentTarget(
            survival with { PackageDeploymentEnabled = false }));
        Assert.False(PackageImportRules.IsPackageDeploymentTarget(
            survival with { AgentId = "invalid agent" }));
        Assert.False(PackageImportRules.IsPackageDeploymentTarget(
            survival with { Port = 0 }));
    }

    [Fact]
    public void ResolvePackageDeploymentMemoryGuidance_UsesHostCapacity()
    {
        var guidance = PackageImportRules.ResolvePackageDeploymentMemoryGuidance(
            "survival2",
            PackageImportRules.ActivityAgentId,
            25565,
            packageDeploymentEnabled: true,
            hostTotalMemoryMiB: 32768);

        Assert.Equal(new ServerMemoryGuidance(32768, 4096, 16384), guidance);
    }

    [Fact]
    public void ResolvePackageDeploymentMemoryGuidance_RequiresExplicitValidTargetAndCapacity()
    {
        static ServerMemoryGuidance? Resolve(
            string serverId = PackageImportRules.ActivityServerId,
            string agentId = PackageImportRules.ActivityAgentId,
            int port = PackageImportRules.ActivityPort,
            bool packageDeploymentEnabled = true,
            int? hostTotalMemoryMiB = 16384) =>
            PackageImportRules.ResolvePackageDeploymentMemoryGuidance(
                serverId,
                agentId,
                port,
                packageDeploymentEnabled,
                hostTotalMemoryMiB);

        Assert.Equal(new ServerMemoryGuidance(16384, 4096, 8192), Resolve());
        Assert.Null(Resolve(packageDeploymentEnabled: false));
        Assert.NotNull(Resolve(
            serverId: "survival2",
            agentId: "owl9",
            port: 25565));
        Assert.Null(Resolve(serverId: "Invalid Target"));
        Assert.Null(Resolve(agentId: "invalid agent"));
        Assert.Null(Resolve(port: 0));
        Assert.Null(Resolve(hostTotalMemoryMiB: null));
    }

    [Fact]
    public void ValidatePublisherHeartbeat_RejectsClockDriftAndInvalidAgent()
    {
        var now = DateTimeOffset.Parse("2026-08-03T08:00:00Z");
        var errors = PackageImportRules.ValidatePublisherHeartbeat(
            new PackagePublisherHeartbeatRequest(
                "Publisher Main",
                "1.0.0",
                now.AddMinutes(-11)),
            now);

        Assert.Contains("agentId", errors.Keys);
        Assert.Contains("capturedAt", errors.Keys);

        var activeIdErrors = PackageImportRules.ValidatePublisherHeartbeat(
            new PackagePublisherHeartbeatRequest(
                "publisher-main",
                "1.0.0",
                now,
                Guid.Empty),
            now);
        Assert.Contains("activeImportId", activeIdErrors.Keys);
    }

    [Fact]
    public void ValidatePublisherCompletion_RequiresManifestOnlyOnSuccess()
    {
        var success = new PackagePublisherCompletionRequest(
            "publisher-main",
            1,
            PackagePublisherJobOutcome.Succeeded,
            "PUBLISHED",
            "发布完成。",
            Convert.ToBase64String([1, 2, 3]),
            1,
            2,
            3);

        Assert.Empty(PackageImportRules.ValidatePublisherCompletion(success));
        Assert.Contains(
            "manifestEnvelopeBase64",
            PackageImportRules.ValidatePublisherCompletion(
                success with { ManifestEnvelopeBase64 = null }).Keys);
        Assert.Contains(
            "manifestEnvelopeBase64",
            PackageImportRules.ValidatePublisherCompletion(
                success with
                {
                    Outcome = PackagePublisherJobOutcome.Failed,
                    ResultCode = "PUBLISH_FAILED"
                }).Keys);
    }

    [Fact]
    public void ValidatePublisherProgress_RequiresBoundedRealTotals()
    {
        var valid = new PackagePublisherProgressRequest(
            "publisher-main",
            1,
            PackagePublisherProgressPhase.PublishingObjects,
            40,
            100,
            4096,
            8192);

        Assert.Empty(PackageImportRules.ValidatePublisherProgress(valid));
        Assert.Contains(
            "objects",
            PackageImportRules.ValidatePublisherProgress(
                valid with { CompletedObjects = 101 }).Keys);
        Assert.Contains(
            "bytes",
            PackageImportRules.ValidatePublisherProgress(
                valid with { ProcessedBytes = 8193 }).Keys);
        Assert.Contains(
            "totalBytes",
            PackageImportRules.ValidatePublisherProgress(
                valid with
                {
                    Phase = PackagePublisherProgressPhase.DownloadingArchive,
                    CompletedObjects = 0,
                    TotalObjects = 0,
                    ProcessedBytes = 0,
                    TotalBytes = 0
                }).Keys);
    }

    private static AdminPackageImportRecord CreateImport(Guid importId, bool blocking)
    {
        var issue = blocking
            ? new PackageImportIssueRecord(
                "BLOCKED",
                PackageImportIssueSeverity.Blocking,
                "blocked",
                null)
            : null;
        var analysis = new PackageImportAnalysisRecord(
            "Canonical",
            new PackageImportDetectedMetadataRecord(
                "summer-fabric-1.20.1",
                "夏日活动",
                "1.0.0",
                "1.20.1",
                17,
                "Fabric",
                "0.16.14",
                20,
                "start.bat"),
            new PackageImportPartRecord(new string('a', 64), 100, 200, 2),
            new PackageImportPartRecord(new string('b', 64), 100, 200, 2),
            1,
            1,
            1,
            [],
            issue is null ? [] : [issue]);
        return new AdminPackageImportRecord(
            importId,
            "summer.zip",
            1000,
            1000,
            new string('c', 64),
            PackageImportStatus.AwaitingReview,
            analysis,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid(),
            "管理员",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            3);
    }

    private static AdminServerControlTargetRecord CreateControlTarget(
        string serverId,
        string agentId,
        int port,
        string? conflictGroup) =>
        new(
            serverId,
            serverId,
            agentId,
            conflictGroup,
            port,
            true,
            DateTimeOffset.UtcNow,
            false,
            null,
            new ServerQuickSettings(20, 10, 10, "normal", true, 2048, 4096, 8192),
            ["list"],
            string.Empty,
            null,
            null,
            PackageDeploymentEnabled: true);
}
