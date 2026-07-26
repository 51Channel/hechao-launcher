using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.Telemetry;

public static partial class LauncherTelemetryRules
{
    public const int MaximumBatchSize = 50;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    public static IReadOnlyDictionary<string, string[]> Validate(
        LauncherTelemetryBatchRequest request,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Events is null ||
            request.Events.Count is < 1 or > MaximumBatchSize)
        {
            errors["events"] =
            [
                $"事件批次必须包含 1 到 {MaximumBatchSize} 条记录。"
            ];
            return errors;
        }

        var eventIds = new HashSet<Guid>();
        for (var index = 0; index < request.Events.Count; index++)
        {
            var item = request.Events[index];
            var prefix = $"events[{index}]";
            if (item.EventId == Guid.Empty || !eventIds.Add(item.EventId))
            {
                errors[$"{prefix}.eventId"] = ["事件编号无效或在批次内重复。"];
            }

            if (!Enum.IsDefined(item.Type))
            {
                errors[$"{prefix}.type"] = ["事件类型无效。"];
            }

            if (!Enum.IsDefined(item.Outcome))
            {
                errors[$"{prefix}.outcome"] = ["事件结果无效。"];
            }

            if (!Enum.IsDefined(item.FailureCode) ||
                (item.Outcome == LauncherTelemetryOutcome.Success &&
                 item.FailureCode != LauncherTelemetryFailureCode.None) ||
                (item.Outcome != LauncherTelemetryOutcome.Success &&
                 item.FailureCode == LauncherTelemetryFailureCode.None))
            {
                errors[$"{prefix}.failureCode"] = ["失败分类与事件结果不一致。"];
            }

            var launcherVersion = item.LauncherVersion?.Trim() ?? string.Empty;
            if (launcherVersion.Length is < 1 or > 40 ||
                !VersionPattern().IsMatch(launcherVersion))
            {
                errors[$"{prefix}.launcherVersion"] = ["启动器版本无效。"];
            }

            if (item.OccurredAt < now - MaximumAge ||
                item.OccurredAt > now + MaximumClockSkew)
            {
                errors[$"{prefix}.occurredAt"] = ["事件时间超出允许范围。"];
            }

            var hasProfileId = !string.IsNullOrWhiteSpace(item.ProfileId);
            var hasProfileVersion = !string.IsNullOrWhiteSpace(item.ProfileVersion);
            if (hasProfileId != hasProfileVersion)
            {
                errors[$"{prefix}.profile"] = ["档案 ID 与版本必须同时提供。"];
            }
            else if (hasProfileId)
            {
                if (!ProfileIdPattern().IsMatch(item.ProfileId!.Trim()))
                {
                    errors[$"{prefix}.profileId"] = ["客户端档案 ID 无效。"];
                }

                if (item.ProfileVersion!.Trim().Length is < 1 or > 40)
                {
                    errors[$"{prefix}.profileVersion"] = ["客户端档案版本无效。"];
                }
            }

            if (item.DurationMilliseconds is < 0 or > 86_400_000)
            {
                errors[$"{prefix}.durationMilliseconds"] = ["事件耗时无效。"];
            }

            if (item.Bytes is < 0 or > 1_099_511_627_776)
            {
                errors[$"{prefix}.bytes"] = ["事件字节数无效。"];
            }
        }

        return errors;
    }

    public static bool IsSupportedWindow(int hours) => hours is 24 or 168 or 720;

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+_-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdPattern();
}
