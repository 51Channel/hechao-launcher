using Hechao.Api.PackageImports;

namespace Hechao.Api.Tests;

public sealed class PackageImportOptionsTests
{
    [Fact]
    public void IsValid_DisabledConfigurationNeedsNoSecretsOrStorage()
    {
        Assert.True(new PackageImportOptions().IsValid());
    }

    [Fact]
    public void IsValid_EnabledConfigurationRequiresStorageAndTokenDigest()
    {
        var root = Path.Combine(Path.GetTempPath(), "hechao-package-options");
        var valid = new PackageImportOptions
        {
            Enabled = true,
            StorageRoot = root,
            PublisherTokenSha256 = new string('a', 64)
        };

        Assert.True(valid.IsValid());
        var missingToken = new PackageImportOptions
        {
            Enabled = valid.Enabled,
            StorageRoot = valid.StorageRoot,
            PublisherTokenSha256 = string.Empty
        };
        Assert.False(missingToken.IsValid());
    }
}
