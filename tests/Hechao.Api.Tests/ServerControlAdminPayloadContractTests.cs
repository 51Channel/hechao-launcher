using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class ServerControlAdminPayloadContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public void Overview_ExcludesConsoleAndHistoricalOperations()
    {
        var overview = new AdminServerControlOverview(
            DateTimeOffset.UnixEpoch,
            45,
            [new AdminServerControlTargetSummaryRecord(
                "activity",
                "活动服",
                "owl5",
                "owl5-activity-slot",
                25568,
                true,
                DateTimeOffset.UnixEpoch,
                true,
                1234,
                new ServerQuickSettings(30, 10, 8, "normal", false),
                CreateOperation())]);

        var json = JsonSerializer.Serialize(overview, JsonOptions);

        Assert.Contains("\"activeOperation\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("consoleTail", json, StringComparison.Ordinal);
        Assert.DoesNotContain("consoleCapturedAt", json, StringComparison.Ordinal);
        Assert.DoesNotContain("allowedCommandPrefixes", json, StringComparison.Ordinal);
        Assert.DoesNotContain("recentOperations", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetDetail_IncludesOnlySelectedConsoleAndHistory()
    {
        var operation = CreateOperation();
        var detail = new AdminServerControlTargetDetail(
            DateTimeOffset.UnixEpoch,
            45,
            new AdminServerControlTargetRecord(
                "activity",
                "活动服",
                "owl5",
                "owl5-activity-slot",
                25568,
                true,
                DateTimeOffset.UnixEpoch,
                true,
                1234,
                new ServerQuickSettings(30, 10, 8, "normal", false),
                ["list"],
                "Done (1.0s)!",
                DateTimeOffset.UnixEpoch,
                operation),
            [operation]);

        var json = JsonSerializer.Serialize(detail, JsonOptions);

        Assert.Contains("\"consoleTail\":\"Done (1.0s)!\"", json, StringComparison.Ordinal);
        Assert.Contains("\"allowedCommandPrefixes\":[\"list\"]", json, StringComparison.Ordinal);
        Assert.Contains("\"recentOperations\"", json, StringComparison.Ordinal);
    }

    private static AdminServerControlOperationRecord CreateOperation() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "activity",
            "活动服",
            ServerControlAction.Start,
            ServerControlOperationStatus.Running,
            "开始活动",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            []);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
