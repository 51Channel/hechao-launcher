using Hechao.Api.ActivityPlans;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class ActivityPlanRulesTests
{
    [Fact]
    public void Overlaps_UsesHalfOpenIntervals()
    {
        var firstStart = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var firstEnd = firstStart.AddHours(2);

        Assert.False(ActivityPlanRules.Overlaps(
            firstStart,
            firstEnd,
            firstEnd,
            firstEnd.AddHours(1)));
        Assert.True(ActivityPlanRules.Overlaps(
            firstStart,
            firstEnd,
            firstEnd.AddMinutes(-1),
            firstEnd.AddHours(1)));
    }

    [Fact]
    public void Validate_RequiresBoundedScheduleAndCompletedPackageReference()
    {
        var opensAt = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var request = new AdminActivityPlanCreateRequest(
            "夏日活动",
            "提前下载客户端。",
            opensAt,
            opensAt.AddHours(3),
            20,
            AccessTier.Participant,
            Guid.NewGuid());

        Assert.Empty(ActivityPlanRules.Validate(request));
        Assert.Contains(
            "schedule",
            ActivityPlanRules.Validate(request with
            {
                ClosesAt = opensAt.AddDays(ActivityPlanRules.MaximumDurationDays + 1)
            }).Keys);
        Assert.Contains(
            "packageImportId",
            ActivityPlanRules.Validate(request with
            {
                PackageImportId = Guid.Empty
            }).Keys);
    }

    [Fact]
    public void ValidateDeployment_RequiresExactPlanConfirmation()
    {
        var request = new AdminActivityPlanDeployRequest(
            2,
            "DEPLOY summer-event",
            "为夏日企划部署已审核整合包");

        Assert.Empty(ActivityPlanRules.Validate("summer-event", request));
        Assert.Contains(
            "confirmation",
            ActivityPlanRules.Validate(
                "summer-event",
                request with { Confirmation = "summer-event" }).Keys);
    }
}
