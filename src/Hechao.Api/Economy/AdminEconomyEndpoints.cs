namespace Hechao.Api.Economy;

public static class AdminEconomyEndpoints
{
    public static void MapAdminEconomy(this RouteGroupBuilder adminApi)
    {
        adminApi.MapGet("/economy/overview", GetOverviewAsync);
    }

    private static async Task<IResult> GetOverviewAsync(
        int? hours,
        string? serverId,
        AdminEconomyRepository repository,
        CancellationToken cancellationToken)
    {
        var windowHours = hours ?? 24;
        if (!AdminEconomyWindow.IsSupported(windowHours))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["hours"] = ["统计窗口只支持 24、168、720 或 2160 小时。"]
            });
        }

        var normalizedServerId = string.IsNullOrWhiteSpace(serverId)
            ? null
            : serverId.Trim();
        if (normalizedServerId is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(
                normalizedServerId,
                "^[a-z0-9][a-z0-9._-]{1,63}$"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["serverId"] = ["服务器 ID 格式无效。"]
            });
        }

        return Results.Ok(await repository.GetOverviewAsync(
            windowHours,
            normalizedServerId,
            cancellationToken));
    }
}
