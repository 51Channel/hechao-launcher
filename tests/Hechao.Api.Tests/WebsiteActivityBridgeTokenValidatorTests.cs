using System.Security.Cryptography;
using System.Text;
using Hechao.Api.ActivityPlans;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class WebsiteActivityBridgeTokenValidatorTests
{
    [Fact]
    public void ValidatorRequiresExactTokenAndActor()
    {
        const string token = "website-activity-test-token-000001";
        var validator = new WebsiteActivityBridgeTokenValidator(
            Options.Create(new WebsiteActivityBridgeOptions
            {
                InternalTokenSha256 = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                ActorUserId = Guid.NewGuid()
            }));

        Assert.True(validator.IsConfigured);
        Assert.True(validator.IsValid(token));
        Assert.False(validator.IsValid(token + "-wrong"));
    }

    [Fact]
    public void ValidatorIsDisabledWithoutConfiguredActor()
    {
        const string token = "website-activity-test-token-000001";
        var validator = new WebsiteActivityBridgeTokenValidator(
            Options.Create(new WebsiteActivityBridgeOptions
            {
                InternalTokenSha256 = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(token)))
            }));

        Assert.False(validator.IsConfigured);
    }
}
