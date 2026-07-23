namespace Hechao.Distribution;

public sealed class AtomicProfileDirectorySwitcher(Action? beforeActivate = null)
{
    public void Switch(string stagingDirectory, string activeDirectory, string previousDirectory)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var stalePreviousDirectory = previousDirectory + ".stale-" + suffix;
        var previousWasStashed = false;
        var activeWasMoved = false;

        try
        {
            if (Directory.Exists(previousDirectory))
            {
                Directory.Move(previousDirectory, stalePreviousDirectory);
                previousWasStashed = true;
            }

            if (Directory.Exists(activeDirectory))
            {
                Directory.Move(activeDirectory, previousDirectory);
                activeWasMoved = true;
            }

            beforeActivate?.Invoke();
            Directory.Move(stagingDirectory, activeDirectory);
            TryDeleteDirectory(stalePreviousDirectory);
        }
        catch (Exception switchFailure)
        {
            try
            {
                if (!Directory.Exists(activeDirectory) && activeWasMoved && Directory.Exists(previousDirectory))
                {
                    Directory.Move(previousDirectory, activeDirectory);
                    activeWasMoved = false;
                }

                if (previousWasStashed && !Directory.Exists(previousDirectory) && Directory.Exists(stalePreviousDirectory))
                {
                    Directory.Move(stalePreviousDirectory, previousDirectory);
                }
            }
            catch (Exception rollbackFailure)
            {
                throw new ProfileRollbackException(
                    "The client switch failed and the previous installation could not be restored.",
                    new AggregateException(switchFailure, rollbackFailure));
            }

            throw;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class ProfileRollbackException(string message, Exception innerException)
    : IOException(message, innerException);
