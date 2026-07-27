using System.Security.Cryptography;
using System.Text;
using Hechao.Api.Monitoring;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class OperationalAlertTokenValidatorTests
{
    [Fact]
    public void IsValid_UsesConfiguredSha256Digest()
    {
        const string token = "alert-monitor-token-with-enough-entropy";
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var validator = new OperationalAlertTokenValidator(
            Options.Create(new OperationalAlertOptions
            {
                InternalTokenSha256 = digest
            }));

        Assert.True(validator.IsConfigured);
        Assert.True(validator.IsValid(token));
        Assert.False(validator.IsValid(token + "-wrong"));
    }

    [Fact]
    public void IsValid_RejectsMissingConfiguration()
    {
        var validator = new OperationalAlertTokenValidator(
            Options.Create(new OperationalAlertOptions()));

        Assert.False(validator.IsConfigured);
        Assert.False(validator.IsValid("anything"));
    }
}
