using Hechao.Distribution;

internal sealed record DistributionReleaseValidationResult(
    string ProfileId,
    string Version,
    DateTimeOffset PublishedAt,
    string KeyId,
    string ManifestSha256,
    int LogicalFileCount,
    long LogicalBytes,
    int ObjectCount,
    long ObjectBytes);

internal static class DistributionReleaseValidator
{
    public static DistributionReleaseValidationResult Validate(
        string distributionDirectory,
        string manifestPath,
        string trustBundlePath)
    {
        var envelopePath = RequireRegularFile(manifestPath, "signed manifest");
        var trustPath = RequireRegularFile(trustBundlePath, "manifest trust bundle");
        var envelopeBytes = File.ReadAllBytes(envelopePath);
        var trustBundle = ManifestJson.DeserializeTrustBundle(File.ReadAllBytes(trustPath));
        var verified = SignedManifestCodec.Verify(envelopeBytes, trustBundle);
        var objects = OssDistributionUploader.ValidateAndEnumerateObjects(
            distributionDirectory);
        var objectsByDigest = objects.ToDictionary(
            item => item.Digest,
            StringComparer.OrdinalIgnoreCase);
        var referencedDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long logicalBytes = 0;

        foreach (var file in verified.Manifest.Files)
        {
            var digest = file.Sha256.ToLowerInvariant();
            if (!objectsByDigest.TryGetValue(digest, out var distributionObject))
            {
                throw new PublisherUsageException(
                    $"Manifest object is missing from the distribution: {digest}");
            }

            if (distributionObject.Length != file.Size)
            {
                throw new PublisherUsageException(
                    $"Manifest object size does not match the distribution: {digest}");
            }

            ValidateObjectUrl(file.Url, digest);
            referencedDigests.Add(digest);
            logicalBytes = checked(logicalBytes + file.Size);
        }

        var unreferencedObject = objects.FirstOrDefault(
            item => !referencedDigests.Contains(item.Digest));
        if (unreferencedObject is not null)
        {
            throw new PublisherUsageException(
                $"Distribution object is not referenced by the manifest: {unreferencedObject.Digest}");
        }

        long objectBytes = 0;
        foreach (var item in objects)
        {
            objectBytes = checked(objectBytes + item.Length);
        }

        return new DistributionReleaseValidationResult(
            verified.Manifest.ProfileId,
            verified.Manifest.Version,
            verified.Manifest.PublishedAt,
            verified.KeyId,
            verified.EnvelopeSha256,
            verified.Manifest.Files.Count,
            logicalBytes,
            objects.Count,
            objectBytes);
    }

    private static string RequireRegularFile(string path, string description)
    {
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists ||
            file.Length <= 0 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new PublisherUsageException($"The {description} is invalid: {fullPath}");
        }

        return fullPath;
    }

    private static void ValidateObjectUrl(string value, string digest)
    {
        var uri = new Uri(value, UriKind.Absolute);
        var expectedSuffix = $"/objects/{digest[..2]}/{digest}";
        if (!uri.AbsolutePath.EndsWith(
                expectedSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PublisherUsageException(
                $"Manifest object URL does not match its digest: {digest}");
        }
    }
}
