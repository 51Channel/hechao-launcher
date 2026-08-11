using Hechao.Api.Authentication;

namespace Hechao.Api.Tests;

public sealed class ForumMembershipEligibilityContractTests
{
    [Fact]
    public void Eligibility_RequiresAnActiveAccountAndVerifiedMinecraftIdentity()
    {
        var verifiedAt = DateTimeOffset.UtcNow;
        var eligible = new ForumMembershipEligibilityResponse(
            Guid.NewGuid(),
            true,
            Guid.NewGuid(),
            "HechaoPlayer",
            verifiedAt);
        var inactive = eligible with { AccountActive = false };
        var unlinked = eligible with
        {
            MinecraftUuid = null,
            MinecraftName = null,
            MinecraftVerifiedAt = null
        };

        Assert.True(eligible.Eligible);
        Assert.False(inactive.Eligible);
        Assert.False(unlinked.Eligible);
    }

    [Fact]
    public void ProgramMapsProtectedLoopbackEligibilityEndpoint()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "Hechao.Api", "Program.cs"));
        var repository = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Hechao.Api",
            "Authentication",
            "AuthenticationRepository.cs"));

        Assert.Contains(
            "/v1/internal/forum/accounts/membership-eligibility",
            program,
            StringComparison.Ordinal);
        Assert.Contains("ValidateForumBridgeRequest", program, StringComparison.Ordinal);
        Assert.Contains("GetForumMembershipEligibilityAsync", repository, StringComparison.Ordinal);
        Assert.Contains("i.verified_at", repository, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Hechao.Launcher.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
