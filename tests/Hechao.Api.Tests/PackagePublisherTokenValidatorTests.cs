using System.Security.Cryptography;
using System.Text;
using Hechao.Api.PackageImports;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class PackagePublisherTokenValidatorTests
{
    [Fact]
    public void IsValid_RequiresEnabledFeatureAndExactTokenDigest()
    {
        const string token = "publisher-token-with-sufficient-randomness";
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var validator = new PackagePublisherTokenValidator(
            Options.Create(new PackageImportOptions
            {
                Enabled = true,
                PublisherTokenSha256 = digest
            }));

        Assert.True(validator.IsConfigured);
        Assert.True(validator.IsValid(token));
        Assert.False(validator.IsValid(token + "-changed"));
    }

    [Fact]
    public void IsValid_FailsClosedWhenFeatureIsDisabled()
    {
        const string token = "publisher-token-with-sufficient-randomness";
        var validator = new PackagePublisherTokenValidator(
            Options.Create(new PackageImportOptions
            {
                Enabled = false,
                PublisherTokenSha256 = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(token)))
            }));

        Assert.False(validator.IsConfigured);
        Assert.False(validator.IsValid(token));
    }
}
