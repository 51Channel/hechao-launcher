using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.Monitoring;

public static partial class OperationalAlertRules
{
    public static bool IsValidFingerprint(string value) =>
        FingerprintRegex().IsMatch(value ?? string.Empty);

    public static IReadOnlyDictionary<string, string[]> Validate(
        InternalOperationalAlertEventRequest request,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(request.Source))
        {
            errors["source"] = ["告警来源无效。"];
        }

        if (!Enum.IsDefined(request.Severity))
        {
            errors["severity"] = ["告警级别无效。"];
        }

        if (!IsValidFingerprint(request.Fingerprint))
        {
            errors["fingerprint"] =
                ["告警指纹必须是 3–160 位小写字母、数字或 : . _ -。"];
        }

        if (!CodeRegex().IsMatch(request.Code ?? string.Empty))
        {
            errors["code"] =
                ["告警代码必须是 3–80 位字母、数字或点号。"];
        }

        if (string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length is < 2 or > 120)
        {
            errors["title"] = ["告警标题长度必须在 2–120 个字符之间。"];
        }

        if (string.IsNullOrWhiteSpace(request.Summary) ||
            request.Summary.Trim().Length is < 2 or > 500)
        {
            errors["summary"] = ["告警摘要长度必须在 2–500 个字符之间。"];
        }

        if (request.ObservedAt < now.AddDays(-1) ||
            request.ObservedAt > now.AddMinutes(5))
        {
            errors["observedAt"] = ["告警时间不在允许范围内。"];
        }

        return errors;
    }

    public static OperationalAlertCandidate? EvaluateApiErrors(
        long requestCount,
        long serverErrorCount,
        DateTimeOffset observedAt)
    {
        var rate = Percentage(serverErrorCount, requestCount);
        if (serverErrorCount < 3 &&
            (requestCount < 20 || rate < 5))
        {
            return null;
        }

        var severity = serverErrorCount >= 10 || rate >= 20
            ? OperationalAlertSeverity.Critical
            : OperationalAlertSeverity.Warning;
        return new OperationalAlertCandidate(
            "api:server-errors",
            "Api.ServerErrors",
            OperationalAlertSource.Api,
            severity,
            "API 服务器错误升高",
            $"最近窗口发生 {serverErrorCount} 次 5xx，错误率 {rate:0.##}%。",
            observedAt);
    }

    public static OperationalAlertCandidate? EvaluateApiLatency(
        long requestCount,
        long totalDurationMilliseconds,
        int maximumDurationMilliseconds,
        DateTimeOffset observedAt)
    {
        var average = requestCount == 0
            ? 0
            : totalDurationMilliseconds / (double)requestCount;
        if (requestCount < 10 ||
            (average < 1000 && maximumDurationMilliseconds < 5000))
        {
            return null;
        }

        var severity = average >= 2500 || maximumDurationMilliseconds >= 10000
            ? OperationalAlertSeverity.Critical
            : OperationalAlertSeverity.Warning;
        return new OperationalAlertCandidate(
            "api:latency",
            "Api.Latency",
            OperationalAlertSource.Api,
            severity,
            "API 响应延迟升高",
            $"最近窗口平均 {average:0} ms，最大 {maximumDurationMilliseconds} ms，共 {requestCount} 次请求。",
            observedAt);
    }

    public static OperationalAlertCandidate? EvaluateLoginFailures(
        long failureCount,
        DateTimeOffset observedAt) =>
        failureCount < 5
            ? null
            : new OperationalAlertCandidate(
                "authentication:login-failures",
                "Authentication.LoginFailures",
                OperationalAlertSource.Authentication,
                failureCount >= 20
                    ? OperationalAlertSeverity.Critical
                    : OperationalAlertSeverity.Warning,
                "赫朝账号登录失败升高",
                $"最近窗口发生 {failureCount} 次登录失败，请检查异常登录或客户端配置。",
                observedAt);

    public static OperationalAlertCandidate? EvaluateDownloadFailures(
        long attemptCount,
        long failureCount,
        DateTimeOffset observedAt)
    {
        var rate = Percentage(failureCount, attemptCount);
        if (attemptCount < 5 || failureCount < 3 || rate < 20)
        {
            return null;
        }

        return new OperationalAlertCandidate(
            "distribution:client-download-failures",
            "Distribution.ClientDownloadFailures",
            OperationalAlertSource.Distribution,
            failureCount >= 10 || rate >= 50
                ? OperationalAlertSeverity.Critical
                : OperationalAlertSeverity.Warning,
            "客户端下载失败升高",
            $"最近窗口 {attemptCount} 次安装或修复中有 {failureCount} 次失败，失败率 {rate:0.##}%。",
            observedAt);
    }

    public static OperationalAlertCandidate? EvaluateObjectEndpointFailures(
        long failureCount,
        DateTimeOffset observedAt) =>
        failureCount < 2
            ? null
            : new OperationalAlertCandidate(
                "distribution:object-endpoint-failures",
                "Distribution.ObjectEndpointFailures",
                OperationalAlertSource.Distribution,
                failureCount >= 5
                    ? OperationalAlertSeverity.Critical
                    : OperationalAlertSeverity.Warning,
                "对象下载授权接口异常",
                $"最近窗口对象下载授权接口发生 {failureCount} 次 5xx。",
                observedAt);

    private static double Percentage(long value, long total) =>
        total <= 0 ? 0 : value * 100d / total;

    [GeneratedRegex("^[a-z0-9][a-z0-9:._-]{2,159}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9.]{2,79}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();
}

public sealed record OperationalAlertCandidate(
    string Fingerprint,
    string Code,
    OperationalAlertSource Source,
    OperationalAlertSeverity Severity,
    string Title,
    string Summary,
    DateTimeOffset ObservedAt);
