using Hechao.Api.Monitoring;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class OperationalAlertRulesTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-27T08:00:00Z");

    [Fact]
    public void Validate_AcceptsBoundedPlatformMonitorEvent()
    {
        var errors = OperationalAlertRules.Validate(
            new InternalOperationalAlertEventRequest(
                "certificate:launcher-api.hechao.world",
                "Certificate.Expiry",
                OperationalAlertSource.Certificate,
                OperationalAlertSeverity.Warning,
                true,
                "证书将在 30 天内到期",
                "launcher-api.hechao.world 剩余 29 天。",
                Now),
            Now);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsUnsafeFingerprintAndTimestamp()
    {
        var errors = OperationalAlertRules.Validate(
            new InternalOperationalAlertEventRequest(
                "../../secret",
                "bad-code!",
                OperationalAlertSource.Infrastructure,
                OperationalAlertSeverity.Critical,
                true,
                "x",
                "x",
                Now.AddDays(-2)),
            Now);

        Assert.Contains("fingerprint", errors.Keys);
        Assert.Contains("code", errors.Keys);
        Assert.Contains("title", errors.Keys);
        Assert.Contains("summary", errors.Keys);
        Assert.Contains("observedAt", errors.Keys);
    }

    [Fact]
    public void Validate_RejectsUndefinedEnums()
    {
        var errors = OperationalAlertRules.Validate(
            new InternalOperationalAlertEventRequest(
                "platform:test",
                "Infrastructure.Test",
                (OperationalAlertSource)999,
                (OperationalAlertSeverity)999,
                true,
                "测试告警",
                "测试摘要。",
                Now),
            Now);

        Assert.Contains("source", errors.Keys);
        Assert.Contains("severity", errors.Keys);
    }

    [Fact]
    public void EvaluateApiErrors_UsesCountAndRateFloor()
    {
        Assert.Null(OperationalAlertRules.EvaluateApiErrors(100, 2, Now));

        var warning =
            OperationalAlertRules.EvaluateApiErrors(40, 3, Now);
        var critical =
            OperationalAlertRules.EvaluateApiErrors(20, 10, Now);

        Assert.Equal(
            OperationalAlertSeverity.Warning,
            warning?.Severity);
        Assert.Equal(
            OperationalAlertSeverity.Critical,
            critical?.Severity);
    }

    [Fact]
    public void EvaluateDownloadFailures_RequiresEnoughSamples()
    {
        Assert.Null(
            OperationalAlertRules.EvaluateDownloadFailures(4, 4, Now));
        Assert.Null(
            OperationalAlertRules.EvaluateDownloadFailures(20, 2, Now));

        var alert =
            OperationalAlertRules.EvaluateDownloadFailures(10, 3, Now);

        Assert.Equal(
            "distribution:client-download-failures",
            alert?.Fingerprint);
    }
}
