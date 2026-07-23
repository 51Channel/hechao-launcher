using System.Security.Cryptography;
using System.Text;
using Hechao.Api.Monitoring;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class ServerHeartbeatTokenValidatorTests
{
    [Fact]
    public void IsValid_AcceptsConfiguredTokenOnly()
    {
        const string token = "heartbeat-test-token";
        var options = Options.Create(new ServerHeartbeatOptions
        {
            InternalTokenSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(token)))
        });
        var validator = new ServerHeartbeatTokenValidator(options);

        Assert.True(validator.IsConfigured);
        Assert.True(validator.IsValid(token));
        Assert.False(validator.IsValid("another-token"));
        Assert.False(validator.IsValid(null));
    }

    [Fact]
    public void IsValid_FailsClosedWhenUnconfigured()
    {
        var validator = new ServerHeartbeatTokenValidator(
            Options.Create(new ServerHeartbeatOptions()));

        Assert.False(validator.IsConfigured);
        Assert.False(validator.IsValid("anything"));
    }
}
