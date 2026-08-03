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
            "fanstreet",
            PreserveWorldData: false,
            SyncServerCatalog: true,
            "夏日活动",
            AccessTier.Participant,
            4096,
            $"发布并部署 {importId:D}");

        Assert.Empty(PackageImportRules.Validate(request, import));
        Assert.Contains(
            "confirmation",
            PackageImportRules.Validate(
                request with { Confirmation = "确认" },
                import).Keys);
        Assert.Contains(
            "analysis",
            PackageImportRules.Validate(request, CreateImport(importId, blocking: true)).Keys);
    }

    [Fact]
    public void IsActivityTarget_RejectsSurvivalAndAcceptsOwl5ActivitySlot()
    {
        var target = new AdminServerControlTargetRecord(
            "fanstreet",
            "范街",
            "owl5",
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
            null);

        Assert.True(PackageImportRules.IsActivityTarget(target));
        Assert.False(PackageImportRules.IsActivityTarget(
            target with { ConflictGroup = "owl5-survival-slot", Port = 25565 }));
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
}
