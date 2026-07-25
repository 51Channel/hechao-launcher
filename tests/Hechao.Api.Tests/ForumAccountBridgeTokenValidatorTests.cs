using System.Security.Cryptography;
using System.Text;
using Hechao.Api.Authentication;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class ForumAccountBridgeTokenValidatorTests
{
    [Fact]
    public void IsValid_AcceptsConfiguredTokenOnly()
    {
        const string token = "forum-bridge-test-token";
        var validator = new ForumAccountBridgeTokenValidator(
            Options.Create(new ForumAccountBridgeOptions
            {
                InternalTokenSha256 = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(token)))
            }));

        Assert.True(validator.IsConfigured);
        Assert.True(validator.IsValid(token));
        Assert.False(validator.IsValid("another-token"));
        Assert.False(validator.IsValid(null));
    }

    [Fact]
    public void IsValid_FailsClosedWhenUnconfigured()
    {
        var validator = new ForumAccountBridgeTokenValidator(
            Options.Create(new ForumAccountBridgeOptions()));

        Assert.False(validator.IsConfigured);
        Assert.False(validator.IsValid("anything"));
    }
}
