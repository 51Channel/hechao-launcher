using Hechao.Api.Admin;
using Hechao.Api.Distribution;
using Hechao.Contracts;
using Hechao.Distribution;
using Microsoft.Extensions.Options;

namespace Hechao.Api.PackageImports;

public enum PackagePublisherCompletionStatus
{
    Success,
    NotFound,
    ClaimConflict
}

public sealed record PackagePublisherCompletionResult(
    PackagePublisherCompletionStatus Status,
    AdminPackageImportRecord? Import = null);

public sealed class PackagePublisherCompletionService(
    PackageImportRepository packageImports,
    AdminProfileReleaseRepository profiles,
    ProfileManifestStore manifestStore,
    DistributionTrustBundleProvider trustBundleProvider,
    IOptions<DistributionOptions> distributionOptions,
    ILogger<PackagePublisherCompletionService> logger)
{
    public async Task<PackagePublisherCompletionResult> CompleteAsync(
        Guid importId,
        PackagePublisherCompletionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var claim = await packageImports.GetPublisherClaimAsync(
            importId,
            request.AgentId,
            request.AttemptCount,
            now,
            cancellationToken);
        if (claim.Status != PackagePublisherClaimStatus.Valid)
        {
            return new PackagePublisherCompletionResult(
                claim.Status == PackagePublisherClaimStatus.NotFound
                    ? PackagePublisherCompletionStatus.NotFound
                    : PackagePublisherCompletionStatus.ClaimConflict);
        }

        if (request.Outcome == PackagePublisherJobOutcome.Failed)
        {
            var failed = await packageImports.CompletePublisherFailureAsync(
                importId,
                request.AgentId,
                request.AttemptCount,
                request.ResultCode,
                request.ResultMessage,
                retryable: true,
                now,
                cancellationToken);
            return Map(failed);
        }

        var package = claim.Import!;
        var validation = TryValidateManifest(package, request, out var manifest);
        if (validation is not null)
        {
            logger.LogWarning(
                "Package publisher result for {ImportId} was rejected: {Reason}",
                importId,
                validation);
            var rejected = await packageImports.CompletePublisherFailureAsync(
                importId,
                request.AgentId,
                request.AttemptCount,
                "PUBLISH_RESULT_REJECTED",
                validation,
                retryable: false,
                now,
                cancellationToken);
            return Map(rejected);
        }

        var profile = await profiles.GetDetailAsync(
            manifest!.ProfileId,
            cancellationToken);
        if (profile is null)
        {
            var created = await profiles.CreateProfileAsync(
                new AdminClientProfileCreateRequest(
                    manifest.ProfileId,
                    package.Plan!.ProfileDisplayName),
                package.CreatedBy,
                sourceIp: null,
                cancellationToken);
            if (created.Status is not (
                    AdminProfileMutationStatus.Success or
                    AdminProfileMutationStatus.DuplicateId))
            {
                throw new InvalidOperationException(
                    $"Unable to create client profile: {created.Status}.");
            }
        }

        StoredProfileManifest? storedManifest = null;
        try
        {
            storedManifest = await manifestStore.StoreReleaseAsync(
                manifest.ProfileId,
                manifest.ManifestSha256,
                manifest.Envelope,
                cancellationToken);
            var imported = await profiles.ImportReleaseAsync(
                manifest,
                package.CreatedBy,
                sourceIp: null,
                cancellationToken);
            if (imported.Status == AdminProfileMutationStatus.DuplicateVersion)
            {
                manifestStore.DeleteStoredRelease(storedManifest);
                var rejected = await packageImports.CompletePublisherFailureAsync(
                    importId,
                    request.AgentId,
                    request.AttemptCount,
                    "PROFILE_VERSION_CONFLICT",
                    "该客户端档案版本号已对应另一份签名清单，正式通道未发生变化。",
                    retryable: false,
                    now,
                    cancellationToken);
                return Map(rejected);
            }

            if (imported.Status != AdminProfileMutationStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Unable to import client profile release: {imported.Status}.");
            }
        }
        catch
        {
            if (storedManifest is not null)
            {
                manifestStore.DeleteStoredRelease(storedManifest);
            }

            throw;
        }

        var completed = await packageImports.CompletePublisherSuccessAsync(
            importId,
            request.AgentId,
            request.AttemptCount,
            manifest.ManifestSha256,
            request.UploadedObjects,
            request.ExistingObjects,
            request.UploadedBytes,
            now,
            cancellationToken);
        return Map(completed);
    }

    private string? TryValidateManifest(
        AdminPackageImportRecord package,
        PackagePublisherCompletionRequest request,
        out ValidatedProfileReleaseManifest? manifest)
    {
        manifest = null;
        if (package.Analysis is null || package.Plan is null ||
            string.IsNullOrWhiteSpace(request.ManifestEnvelopeBase64) ||
            request.ManifestEnvelopeBase64.Length >
            ((distributionOptions.Value.MaximumManifestBytes + 2L) / 3L * 4L) + 8L)
        {
            return "发布代理没有返回有效的签名清单。";
        }

        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(request.ManifestEnvelopeBase64);
            manifest = ProfileReleaseManifestValidator.Validate(
                envelope,
                package.Plan.ProfileId,
                trustBundleProvider.TrustBundle);
        }
        catch (Exception exception) when (
            exception is FormatException or ManifestFormatException or
                ManifestIntegrityException or ManifestSignatureException or
                OverflowException)
        {
            return "签名清单未通过 API 信任根与完整性校验。";
        }

        if (!string.Equals(manifest.Version, package.Plan.Version, StringComparison.Ordinal) ||
            !string.Equals(
                manifest.MinecraftVersion,
                package.Analysis.Metadata.MinecraftVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.JavaVersion,
                package.Analysis.Metadata.JavaMajorVersion.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Loader,
                package.Analysis.Metadata.Loader,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.LoaderVersion,
                package.Analysis.Metadata.LoaderVersion,
                StringComparison.Ordinal))
        {
            return "签名清单元数据与管理员确认的识别结果不一致。";
        }

        return null;
    }

    private static PackagePublisherCompletionResult Map(
        PackagePublisherMutationResult result) =>
        new(
            result.Status switch
            {
                PackagePublisherMutationStatus.Success =>
                    PackagePublisherCompletionStatus.Success,
                PackagePublisherMutationStatus.NotFound =>
                    PackagePublisherCompletionStatus.NotFound,
                _ => PackagePublisherCompletionStatus.ClaimConflict
            },
            result.Import);
}
