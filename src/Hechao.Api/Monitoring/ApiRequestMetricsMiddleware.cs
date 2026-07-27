using System.Diagnostics;

namespace Hechao.Api.Monitoring;

public sealed class ApiRequestMetricsMiddleware(
    RequestDelegate next,
    ApiRequestMetricsCollector collector,
    TimeProvider timeProvider)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        await next(context);
        collector.Record(
            timeProvider.GetUtcNow(),
            context.Request.Path.Value ?? string.Empty,
            context.Response.StatusCode,
            Stopwatch.GetElapsedTime(startedAt));
    }
}
