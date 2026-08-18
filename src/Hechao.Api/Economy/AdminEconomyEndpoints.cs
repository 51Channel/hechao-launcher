namespace Hechao.Api.Economy;

public static class AdminEconomyEndpoints
{
    public static void MapAdminEconomy(this RouteGroupBuilder adminApi)
    {
        adminApi.MapGet("/economy/overview", GetOverviewAsync);
        adminApi.MapGet("/economy/items/history", GetItemHistoryAsync);
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

        var normalizedServerId = NormalizeServerId(serverId);
        if (normalizedServerId.Invalid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["serverId"] = ["服务器 ID 格式无效。"]
            });
        }

        return Results.Ok(await repository.GetOverviewAsync(
            windowHours,
            normalizedServerId.Value,
            cancellationToken));
    }

    private static async Task<IResult> GetItemHistoryAsync(
        int? hours,
        string? itemId,
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

        var normalizedItemId = itemId?.Trim();
        if (!EconomyRules.IsValidMinecraftItemId(normalizedItemId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["itemId"] = ["物品 ID 格式无效。"]
            });
        }

        var normalizedServerId = NormalizeServerId(serverId);
        if (normalizedServerId.Invalid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["serverId"] = ["服务器 ID 格式无效。"]
            });
        }

        var history = await repository.GetItemHistoryAsync(
            windowHours,
            normalizedItemId!,
            normalizedServerId.Value,
            cancellationToken);
        return history is null
            ? Results.NotFound()
            : Results.Ok(history);
    }

    private static (string? Value, bool Invalid) NormalizeServerId(string? serverId)
    {
        var normalized = string.IsNullOrWhiteSpace(serverId) ? null : serverId.Trim();
        var invalid = normalized is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                "^[a-z0-9][a-z0-9._-]{1,63}$");
        return (normalized, invalid);
    }
}
