using Hechao.Contracts;

namespace Hechao.Api.ServerControl;

internal sealed record ServerControlPlanningTarget(
    string ServerId,
    string AgentId,
    bool Online);

internal sealed record ServerControlPlannedCommand(
    int Sequence,
    string ServerId,
    string AgentId,
    ServerControlCommandKind Kind,
    string? ConsoleCommand = null,
    ServerQuickSettings? Settings = null);

internal static class ServerControlCommandPlanner
{
    internal static IReadOnlyList<ServerControlPlannedCommand> Build(
        ServerControlPlanningTarget target,
        IReadOnlyList<ServerControlPlanningTarget> affectedTargets,
        AdminServerControlRequest request)
    {
        var commands = new List<ServerControlPlannedCommand>();
        switch (request.Action)
        {
            case ServerControlAction.Start:
                AddConflictStops(commands, target, affectedTargets);
                if (!target.Online)
                {
                    commands.Add(Create(
                        commands.Count > 0 ? 1 : 0,
                        target,
                        ServerControlCommandKind.Start));
                }
                break;
            case ServerControlAction.Stop:
                commands.Add(Create(
                    0,
                    target,
                    ServerControlCommandKind.Stop));
                break;
            case ServerControlAction.Restart:
                AddConflictStops(commands, target, affectedTargets);
                if (target.Online)
                {
                    commands.Add(Create(
                        0,
                        target,
                        ServerControlCommandKind.Stop));
                }

                commands.Add(Create(
                    commands.Count > 0 ? 1 : 0,
                    target,
                    ServerControlCommandKind.Start));
                break;
            case ServerControlAction.ConsoleCommand:
                commands.Add(Create(
                    0,
                    target,
                    ServerControlCommandKind.ConsoleCommand,
                    request.ConsoleCommand!.Trim()));
                break;
            case ServerControlAction.ApplySettings:
                commands.Add(Create(
                    0,
                    target,
                    ServerControlCommandKind.ApplySettings,
                    settings: request.Settings));
                break;
            case ServerControlAction.DeleteServerFiles:
                commands.Add(Create(
                    0,
                    target,
                    ServerControlCommandKind.DeleteServerFiles));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Action,
                    "Unsupported server control action.");
        }

        return commands;
    }

    private static void AddConflictStops(
        ICollection<ServerControlPlannedCommand> commands,
        ServerControlPlanningTarget target,
        IEnumerable<ServerControlPlanningTarget> affectedTargets)
    {
        foreach (var item in affectedTargets.Where(item =>
                     !string.Equals(
                         item.ServerId,
                         target.ServerId,
                         StringComparison.Ordinal)))
        {
            commands.Add(Create(
                0,
                item,
                ServerControlCommandKind.Stop));
        }
    }

    private static ServerControlPlannedCommand Create(
        int sequence,
        ServerControlPlanningTarget target,
        ServerControlCommandKind kind,
        string? consoleCommand = null,
        ServerQuickSettings? settings = null) =>
        new(
            sequence,
            target.ServerId,
            target.AgentId,
            kind,
            consoleCommand,
            settings);
}
