using Hechao.Api.Telemetry;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class LauncherTelemetryRulesTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-27T00:00:00Z");

    [Fact]
    public void Validate_AcceptsPrivacyBoundedBatch()
    {
        var request = new LauncherTelemetryBatchRequest(
        [
            new LauncherTelemetryEvent(
                Guid.NewGuid(),
                LauncherTelemetryEventType.Install,
                LauncherTelemetryOutcome.Success,
                LauncherTelemetryFailureCode.None,
                "0.11.13",
                Now.AddMinutes(-2),
                "base-1.21.11",
                "1.0.5",
                120_000,
                833_700_000)
        ]);

        var errors = LauncherTelemetryRules.Validate(request, Now);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsOutcomeFailureMismatchAndFutureTimestamp()
    {
        var request = new LauncherTelemetryBatchRequest(
        [
            new LauncherTelemetryEvent(
                Guid.NewGuid(),
                LauncherTelemetryEventType.Launch,
                LauncherTelemetryOutcome.Success,
                LauncherTelemetryFailureCode.ProcessCreationFailed,
                "0.11.13",
                Now.AddMinutes(6),
                "base-1.21.11",
                "1.0.5",
                1200,
                null)
        ]);

        var errors = LauncherTelemetryRules.Validate(request, Now);

        Assert.Contains("events[0].failureCode", errors.Keys);
        Assert.Contains("events[0].occurredAt", errors.Keys);
    }

    [Fact]
    public void Validate_RejectsDuplicateIdsAndPartialProfileMetadata()
    {
        var eventId = Guid.NewGuid();
        var request = new LauncherTelemetryBatchRequest(
        [
            Event(eventId, profileId: "base-1.21.11", profileVersion: null),
            Event(eventId, profileId: null, profileVersion: null)
        ]);

        var errors = LauncherTelemetryRules.Validate(request, Now);

        Assert.Contains("events[0].profile", errors.Keys);
        Assert.Contains("events[1].eventId", errors.Keys);
    }

    [Theory]
    [InlineData(24, true)]
    [InlineData(168, true)]
    [InlineData(720, true)]
    [InlineData(48, false)]
    public void IsSupportedWindow_UsesFixedOperationalWindows(
        int hours,
        bool expected)
    {
        Assert.Equal(expected, LauncherTelemetryRules.IsSupportedWindow(hours));
    }

    private static LauncherTelemetryEvent Event(
        Guid eventId,
        string? profileId,
        string? profileVersion) =>
        new(
            eventId,
            LauncherTelemetryEventType.LauncherStarted,
            LauncherTelemetryOutcome.Success,
            LauncherTelemetryFailureCode.None,
            "0.11.13",
            Now,
            profileId,
            profileVersion,
            null,
            null);
}
