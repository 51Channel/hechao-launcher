namespace Hechao.Api.ServerControl;

internal static class ServerControlTargetVisibility
{
    public static bool IncludeInOverview(
        bool serverFilesPresent,
        bool deletionCleanupPending,
        bool hasActiveOperation) =>
        serverFilesPresent || deletionCleanupPending || hasActiveOperation;
}
