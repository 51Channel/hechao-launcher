using System.Security.Cryptography;
using System.Text;
using Hechao.Api.Velocity;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class VelocityAuthorizationTokenValidatorTests
{
    [Fact]
    public void IsValid_AcceptsConfiguredTokenOnly()
    {
        const string token = "velocity-test-token";
        var options = Options.Create(new VelocityAuthorizationOptions
        {
            InternalTokenSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(token)))
        });
        var validator = new VelocityAuthorizationTokenValidator(options);

        Assert.True(validator.IsConfigured);
        Assert.True(validator.IsValid(token));
        Assert.False(validator.IsValid("another-token"));
        Assert.False(validator.IsValid(null));
    }

    [Fact]
    public void IsValid_FailsClosedWhenUnconfigured()
    {
        var validator = new VelocityAuthorizationTokenValidator(
            Options.Create(new VelocityAuthorizationOptions()));

        Assert.False(validator.IsConfigured);
        Assert.False(validator.IsValid("anything"));
    }
}
