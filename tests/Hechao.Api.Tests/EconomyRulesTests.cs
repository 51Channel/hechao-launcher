using Hechao.Api.Economy;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class EconomyRulesTests
{
    [Fact]
    public void ProductIds_AllowSafeVanillaAndModdedNamespaces()
    {
        Assert.True(EconomyRules.IsValidMinecraftItemId("minecraft:iron_ingot"));
        Assert.True(EconomyRules.IsValidMinecraftItemId("create:brass_ingot"));
        Assert.True(EconomyRules.IsValidMinecraftItemId("example_mod:parts/brass_sheet"));
        Assert.False(EconomyRules.IsValidMinecraftItemId("minecraft:Iron_Ingot"));
        Assert.False(EconomyRules.IsValidMinecraftItemId("Example:iron_ingot"));
        Assert.False(EconomyRules.IsValidMinecraftItemId("../iron_ingot"));
    }

    [Fact]
    public void ProductEndpoints_KeepLegacyRoutesAndProvideSlashSafeQueryRoutes()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Hechao.Api",
            "Economy",
            "EconomyEndpoints.cs"));

        Assert.Contains("MapPut(\"/products\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/products/disable\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPut(\"/products/{itemId}\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/products/{itemId}/disable\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transfer_RejectsSelfTransferAndSubCentAmount()
    {
        var player = Guid.NewGuid();
        Assert.False(EconomyRules.IsValidTransfer(
            new EconomyTransferRequest(
                "transfer:12345678",
                player,
                player,
                1m,
                null),
            100m));
        Assert.False(EconomyRules.IsValidTransfer(
            new EconomyTransferRequest(
                "transfer:12345679",
                player,
                Guid.NewGuid(),
                0.001m,
                null),
            100m));
    }

    [Fact]
    public void Fingerprint_IsStableButRequestSensitive()
    {
        var player = Guid.Parse("5e951687-bff6-4da9-bf48-31968c857fcb");
        var first = EconomyRules.Fingerprint("Transfer", player, 12.3m, "note");
        var second = EconomyRules.Fingerprint("Transfer", player, 12.30m, "note");
        var changed = EconomyRules.Fingerprint("Transfer", player, 12.31m, "note");

        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void TokenValidator_FailsClosedAndChecksServerAllowlist()
    {
        const string token = "economy-test-token-000000000000001";
        var digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(token)));
        var validator = new EconomyServiceTokenValidator(Options.Create(
            new EconomyServiceOptions
            {
                InternalTokenSha256 = digest,
                AllowedServerIds = ["skyrealm"]
            }));

        Assert.Equal(
            EconomyAuthenticationStatus.Allowed,
            validator.Validate($"Bearer {token}", "skyrealm"));
        Assert.Equal(
            EconomyAuthenticationStatus.ServerNotAllowed,
            validator.Validate($"Bearer {token}", "activity"));
        Assert.Equal(
            EconomyAuthenticationStatus.InvalidCredentials,
            validator.Validate($"Bearer {new string('x', 32)}", "skyrealm"));

        var disabled = new EconomyServiceTokenValidator(
            Options.Create(new EconomyServiceOptions()));
        Assert.Equal(
            EconomyAuthenticationStatus.NotConfigured,
            disabled.Validate($"Bearer {token}", "skyrealm"));
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
