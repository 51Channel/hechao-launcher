using System.Security.Cryptography;

namespace Hechao.Distribution;

public static class SignedManifestCodec
{
    public const int CurrentEnvelopeSchemaVersion = 1;
    public const string Algorithm = "ECDSA_P256_SHA256";
    public const int MaximumEnvelopeBytes = 8 * 1024 * 1024;

    public static SignedManifestEnvelope Sign(ClientManifest manifest, string keyId, ECDsa privateKey)
    {
        ManifestValidator.Validate(manifest);
        ValidateKeyId(keyId);
        if (privateKey.KeySize != 256)
        {
            throw new ArgumentException("The signing key must use the NIST P-256 curve.", nameof(privateKey));
        }

        var payload = ManifestJson.SerializeManifest(manifest);
        var signature = privateKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return new SignedManifestEnvelope(
            CurrentEnvelopeSchemaVersion,
            Algorithm,
            keyId,
            Convert.ToBase64String(payload),
            Convert.ToBase64String(signature));
    }

    public static VerifiedClientManifest Verify(
        ReadOnlySpan<byte> envelopeJson,
        ManifestTrustBundle trustBundle)
    {
        if (envelopeJson.Length is 0 or > MaximumEnvelopeBytes)
        {
            throw new ManifestFormatException("The signed manifest envelope has an invalid size.");
        }

        ValidateTrustBundle(trustBundle);

        SignedManifestEnvelope envelope;
        try
        {
            envelope = ManifestJson.DeserializeEnvelope(envelopeJson);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or FormatException)
        {
            throw new ManifestFormatException("The signed manifest envelope is malformed.", exception);
        }

        if (envelope.SchemaVersion != CurrentEnvelopeSchemaVersion ||
            !string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal))
        {
            throw new ManifestSignatureException("The manifest uses an unsupported signature format.");
        }

        ValidateKeyId(envelope.KeyId);
        var trustedKey = trustBundle.Keys.SingleOrDefault(key =>
            string.Equals(key.KeyId, envelope.KeyId, StringComparison.Ordinal));
        if (trustedKey is null || !string.Equals(trustedKey.Algorithm, Algorithm, StringComparison.Ordinal))
        {
            throw new ManifestSignatureException($"The manifest signing key is not trusted: {envelope.KeyId}.");
        }

        try
        {
            var payload = Convert.FromBase64String(envelope.PayloadBase64);
            var signature = Convert.FromBase64String(envelope.SignatureBase64);
            var publicKeyBytes = Convert.FromBase64String(trustedKey.PublicKeyBase64);

            using var publicKey = ECDsa.Create();
            publicKey.ImportSubjectPublicKeyInfo(publicKeyBytes, out var bytesRead);
            if (bytesRead != publicKeyBytes.Length || publicKey.KeySize != 256 ||
                !publicKey.VerifyData(
                    payload,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                throw new ManifestSignatureException("The manifest signature is invalid.");
            }

            var manifest = ManifestJson.DeserializeManifest(payload);
            ManifestValidator.Validate(manifest);
            var digest = Convert.ToHexString(SHA256.HashData(envelopeJson)).ToLowerInvariant();
            return new VerifiedClientManifest(manifest, digest, envelope.KeyId);
        }
        catch (ManifestFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or System.Text.Json.JsonException)
        {
            throw new ManifestSignatureException("The manifest signature could not be verified.", exception);
        }
    }

    public static ClientManifest ReadDigestAnchored(
        ReadOnlySpan<byte> envelopeJson,
        string expectedEnvelopeSha256)
    {
        // The trusted catalog digest anchors the envelope; clients still verify its ECDSA signature.
        if (envelopeJson.Length is 0 or > MaximumEnvelopeBytes)
        {
            throw new ManifestFormatException("The signed manifest envelope has an invalid size.");
        }

        byte[] expectedDigest;
        try
        {
            expectedDigest = Convert.FromHexString(expectedEnvelopeSha256);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ManifestIntegrityException("The expected manifest digest is invalid.", exception);
        }

        var actualDigest = SHA256.HashData(envelopeJson);
        if (expectedDigest.Length != actualDigest.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest))
        {
            throw new ManifestIntegrityException("The signed manifest digest does not match the catalog.");
        }

        try
        {
            var envelope = ManifestJson.DeserializeEnvelope(envelopeJson);
            if (envelope.SchemaVersion != CurrentEnvelopeSchemaVersion ||
                !string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal))
            {
                throw new ManifestSignatureException("The manifest uses an unsupported signature format.");
            }

            ValidateKeyId(envelope.KeyId);
            var payload = Convert.FromBase64String(envelope.PayloadBase64);
            var signature = Convert.FromBase64String(envelope.SignatureBase64);
            if (signature.Length != 64)
            {
                throw new ManifestSignatureException("The manifest signature has an invalid size.");
            }

            var manifest = ManifestJson.DeserializeManifest(payload);
            ManifestValidator.Validate(manifest);
            return manifest;
        }
        catch (ManifestFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
        {
            throw new ManifestFormatException("The signed manifest envelope is malformed.", exception);
        }
    }

    public static ManifestTrustKey ExportTrustKey(string keyId, ECDsa key)
    {
        ValidateKeyId(keyId);
        if (key.KeySize != 256)
        {
            throw new ArgumentException("The signing key must use the NIST P-256 curve.", nameof(key));
        }

        return new ManifestTrustKey(
            keyId,
            Algorithm,
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    private static void ValidateTrustBundle(ManifestTrustBundle trustBundle)
    {
        if (trustBundle.SchemaVersion != 1)
        {
            throw new ManifestSignatureException("The manifest trust bundle has an unsupported schema version.");
        }

        if (trustBundle.Keys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() != trustBundle.Keys.Count)
        {
            throw new ManifestSignatureException("The manifest trust bundle contains duplicate key identifiers.");
        }
    }

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) ||
            keyId.Length > 80 ||
            keyId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ManifestSignatureException("The manifest signing key identifier is invalid.");
        }
    }
}
