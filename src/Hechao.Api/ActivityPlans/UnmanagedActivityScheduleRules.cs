using Hechao.Contracts;

namespace Hechao.Api.ActivityPlans;

internal static class UnmanagedActivityScheduleRules
{
    public static IReadOnlyList<UnmanagedActivityScheduleIssue> GetIssues(
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt,
        Guid? packageImportId)
    {
        var issues = new List<UnmanagedActivityScheduleIssue>
        {
            UnmanagedActivityScheduleIssue.MissingPlanStatus
        };
        if (opensAt is null)
        {
            issues.Add(UnmanagedActivityScheduleIssue.MissingOpensAt);
        }

        if (closesAt is null)
        {
            issues.Add(UnmanagedActivityScheduleIssue.MissingClosesAt);
        }

        if (packageImportId is null)
        {
            issues.Add(UnmanagedActivityScheduleIssue.MissingPackageBinding);
        }

        return issues;
    }
}
