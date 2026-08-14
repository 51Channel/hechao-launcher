using Hechao.Api.ActivityPlans;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class UnmanagedActivityScheduleRulesTests
{
    [Fact]
    public void GetIssues_ExplainsWhyLegacyDirectoryScheduleIsNotAPlan()
    {
        var opensAt = DateTimeOffset.Parse("2026-08-15T09:00:00Z");

        var issues = UnmanagedActivityScheduleRules.GetIssues(
            opensAt,
            closesAt: null,
            packageImportId: null);

        Assert.Equal(
            [
                UnmanagedActivityScheduleIssue.MissingPlanStatus,
                UnmanagedActivityScheduleIssue.MissingClosesAt,
                UnmanagedActivityScheduleIssue.MissingPackageBinding
            ],
            issues);
    }

    [Fact]
    public void GetIssues_KeepsCompleteLegacyRecordReadOnlyUntilExplicitConversion()
    {
        var opensAt = DateTimeOffset.Parse("2026-08-15T09:00:00Z");

        var issues = UnmanagedActivityScheduleRules.GetIssues(
            opensAt,
            opensAt.AddHours(3),
            Guid.NewGuid());

        Assert.Equal(
            [UnmanagedActivityScheduleIssue.MissingPlanStatus],
            issues);
    }
}
