using Hechao.Api.Catalog;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class ClientProfileReleaseResolverTests
{
    private const string ProfileId = "activity-neoforge-1.21.11";

    [Fact]
    public void Resolve_AnonymousAlwaysReceivesProduction()
    {
        var result = ClientProfileReleaseResolver.Resolve(
            ProfileId,
            userId: null,
            accessTier: null,
            CreateCandidates(testPercentage: 100, grayPercentage: 100));

        Assert.Equal("production", result?.Version);
    }

    [Fact]
    public void Resolve_AdministratorReceivesEnabledTestBeforeGray()
    {
        var result = ClientProfileReleaseResolver.Resolve(
            ProfileId,
            Guid.Parse("8d0ad076-95d0-4464-b6d8-b36a35856f16"),
            AccessTier.Administrator,
            CreateCandidates(testPercentage: 100, grayPercentage: 100));

        Assert.Equal("test", result?.Version);
    }

    [Fact]
    public void Resolve_MemberNeverReceivesTest()
    {
        var result = ClientProfileReleaseResolver.Resolve(
            ProfileId,
            Guid.Parse("8d0ad076-95d0-4464-b6d8-b36a35856f16"),
            AccessTier.Member,
            CreateCandidates(testPercentage: 100, grayPercentage: 0));

        Assert.Equal("production", result?.Version);
    }

    [Fact]
    public void Resolve_SkipsPausedCandidate()
    {
        var candidates = CreateCandidates(
            testPercentage: 100,
            grayPercentage: 100)
            .Select(item => item.Channel == ClientProfileReleaseChannel.Test
                ? item with { IsPaused = true }
                : item)
            .ToArray();

        var result = ClientProfileReleaseResolver.Resolve(
            ProfileId,
            Guid.Parse("8d0ad076-95d0-4464-b6d8-b36a35856f16"),
            AccessTier.Administrator,
            candidates);

        Assert.Equal("gray", result?.Version);
    }

    [Fact]
    public void GetStableBucket_IsStableAndBounded()
    {
        var userId = Guid.Parse("8d0ad076-95d0-4464-b6d8-b36a35856f16");

        var first = ClientProfileReleaseResolver.GetStableBucket(
            userId,
            ProfileId,
            ClientProfileReleaseChannel.Gray);
        var second = ClientProfileReleaseResolver.GetStableBucket(
            userId,
            ProfileId,
            ClientProfileReleaseChannel.Gray);

        Assert.Equal(first, second);
        Assert.InRange(first, 0, 99);
    }

    private static IReadOnlyList<ClientProfileReleaseCandidate> CreateCandidates(
        int testPercentage,
        int grayPercentage)
    {
        var now = DateTimeOffset.Parse("2026-07-27T00:00:00Z");
        return
        [
            new(
                ClientProfileReleaseChannel.Test,
                testPercentage,
                "test",
                3,
                new string('a', 64),
                now,
                IsPaused: false),
            new(
                ClientProfileReleaseChannel.Gray,
                grayPercentage,
                "gray",
                2,
                new string('b', 64),
                now,
                IsPaused: false),
            new(
                ClientProfileReleaseChannel.Production,
                100,
                "production",
                1,
                new string('c', 64),
                now,
                IsPaused: false)
        ];
    }
}
