using Hechao.Api.ServerControl;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class ServerControlCommandPlannerTests
{
    [Fact]
    public void Start_StopsEveryConflictBeforeStartingTarget()
    {
        var target = Target("fanstreet", "owl5", online: false);
        var commands = ServerControlCommandPlanner.Build(
            target,
            [
                target,
                Target("yugong", "owl5", online: true),
                Target("legacy-event", "owl6", online: true)
            ],
            Request(ServerControlAction.Start));

        Assert.Collection(
            commands,
            item => AssertCommand(
                item,
                0,
                "yugong",
                ServerControlCommandKind.Stop),
            item => AssertCommand(
                item,
                0,
                "legacy-event",
                ServerControlCommandKind.Stop),
            item => AssertCommand(
                item,
                1,
                "fanstreet",
                ServerControlCommandKind.Start));
    }

    [Fact]
    public void Restart_StopsTargetAndConflictsInSameBarrier()
    {
        var target = Target("fanstreet", "owl5", online: true);
        var commands = ServerControlCommandPlanner.Build(
            target,
            [target, Target("yugong", "owl5", online: true)],
            Request(ServerControlAction.Restart));

        Assert.Collection(
            commands,
            item => AssertCommand(
                item,
                0,
                "yugong",
                ServerControlCommandKind.Stop),
            item => AssertCommand(
                item,
                0,
                "fanstreet",
                ServerControlCommandKind.Stop),
            item => AssertCommand(
                item,
                1,
                "fanstreet",
                ServerControlCommandKind.Start));
    }

    [Fact]
    public void ConsoleCommand_PreservesOnlyStructuredMinecraftPayload()
    {
        var target = Target("activity", "owl5", online: true);
        var commands = ServerControlCommandPlanner.Build(
            target,
            [target],
            new AdminServerControlRequest(
                ServerControlAction.ConsoleCommand,
                "activity",
                "检查当前在线玩家",
                "  list  "));

        var command = Assert.Single(commands);
        AssertCommand(
            command,
            0,
            "activity",
            ServerControlCommandKind.ConsoleCommand);
        Assert.Equal("list", command.ConsoleCommand);
        Assert.Null(command.Settings);
    }

    private static ServerControlPlanningTarget Target(
        string serverId,
        string agentId,
        bool online) =>
        new(serverId, agentId, online);

    private static AdminServerControlRequest Request(
        ServerControlAction action) =>
        new(action, "fanstreet", "切换当前活动服务器");

    private static void AssertCommand(
        ServerControlPlannedCommand command,
        int sequence,
        string serverId,
        ServerControlCommandKind kind)
    {
        Assert.Equal(sequence, command.Sequence);
        Assert.Equal(serverId, command.ServerId);
        Assert.Equal(kind, command.Kind);
    }
}
