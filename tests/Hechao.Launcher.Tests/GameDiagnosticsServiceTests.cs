using System.IO.Compression;
using System.Text.Json;
using Hechao.Distribution;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class GameDiagnosticsServiceTests
{
    [Fact]
    public async Task RecordExitAsync_PersistsLatestExitAndBoundsHistory()
    {
        using var temporary = new TemporaryDirectory();
        var service = new JsonGameDiagnosticsService(
            Path.Combine(temporary.Path, "launcher"));
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        for (var index = 0; index < 25; index++)
        {
            await service.RecordExitAsync(new GameExitRecord(
                Guid.NewGuid(),
                "base-1.21.11",
                1000 + index,
                index,
                startedAt.AddSeconds(index),
                startedAt.AddSeconds(index + 1)));
        }

        var latest = service.LoadLatestExit();

        Assert.NotNull(latest);
        Assert.Equal(1024, latest.ProcessId);
        Assert.Equal(24, latest.ExitCode);
        using var history = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(temporary.Path, "launcher", "game-exits.json")));
        Assert.Equal(20, history.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task CreateBundleAsync_RedactsSensitiveTextAndExcludesWorldData()
    {
        using var temporary = new TemporaryDirectory();
        var launcherRoot = Path.Combine(temporary.Path, "launcher");
        var dataRoot = Path.Combine(temporary.Path, "game-data");
        var profileId = "base-1.21.11";
        var layout = PrepareProfile(dataRoot, profileId);
        var gameDirectory = layout.GetProfileGameDirectory(profileId);
        var logsDirectory = Directory.CreateDirectory(
            Path.Combine(gameDirectory, "logs")).FullName;
        var crashDirectory = Directory.CreateDirectory(
            Path.Combine(gameDirectory, "crash-reports")).FullName;
        var savesDirectory = Directory.CreateDirectory(
            Path.Combine(gameDirectory, "saves", "private-world")).FullName;
        var playerName = "PlayerSecret";
        var uuid = "12345678-1234-1234-1234-123456789abc";
        var accessToken = "eyJaaaaaaaa.bbbbbbbb.cccccccc";
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var latestLogContent =
            $"Player {playerName} at {dataRoot}\n" +
            $"Windows home {userProfile}\n" +
            $"Authorization: Bearer very-secret-bearer-token\n" +
            $"access_token={accessToken}\n" +
            $"uuid={uuid} email=player@example.com ip=192.168.1.20\n";
        await File.WriteAllTextAsync(
            Path.Combine(logsDirectory, "latest.log"),
            latestLogContent);
        var oldCrash = Path.Combine(crashDirectory, "crash-old.txt");
        var latestCrash = Path.Combine(crashDirectory, "crash-latest.txt");
        await File.WriteAllTextAsync(oldCrash, "old crash");
        await File.WriteAllTextAsync(
            latestCrash,
            $"XBL3.0 x=12345;secret-xbox-token {playerName}");
        File.SetLastWriteTimeUtc(oldCrash, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(latestCrash, DateTime.UtcNow);
        await File.WriteAllTextAsync(
            Path.Combine(savesDirectory, "level.dat"),
            "world-secret-must-never-be-included");

        var exit = new GameExitRecord(
            Guid.NewGuid(),
            profileId,
            4321,
            -1,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);
        var service = new JsonGameDiagnosticsService(launcherRoot);
        var result = await service.CreateBundleAsync(
            new GameDiagnosticBundleRequest(
                dataRoot,
                profileId,
                exit,
                [playerName]));

        Assert.True(result.IncludedLatestLog);
        Assert.True(result.IncludedCrashReport);
        Assert.True(result.Size > 0);
        using var archive = ZipFile.OpenRead(result.BundlePath);
        Assert.NotNull(archive.GetEntry("diagnostic.json"));
        Assert.NotNull(archive.GetEntry("README.txt"));
        Assert.NotNull(archive.GetEntry("logs/latest.log"));
        Assert.NotNull(archive.GetEntry("crash-reports/crash-latest.txt"));
        Assert.Null(archive.GetEntry("crash-reports/crash-old.txt"));
        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.Contains("saves", StringComparison.OrdinalIgnoreCase));

        var combinedText = string.Join(
            "\n",
            await Task.WhenAll(archive.Entries.Select(ReadEntryAsync)));
        Assert.Contains("[REDACTED]", combinedText);
        Assert.DoesNotContain(playerName, combinedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(dataRoot, combinedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userProfile, combinedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(accessToken, combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret-bearer-token", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-xbox-token", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain(uuid, combinedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("player@example.com", combinedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.20", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("world-secret-must-never-be-included", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBundleAsync_TruncatesLargeLogsAndWorksWithoutCrashReport()
    {
        using var temporary = new TemporaryDirectory();
        var launcherRoot = Path.Combine(temporary.Path, "launcher");
        var dataRoot = Path.Combine(temporary.Path, "game-data");
        var profileId = "activity-neoforge";
        var layout = PrepareProfile(dataRoot, profileId);
        var logsDirectory = Directory.CreateDirectory(
            Path.Combine(layout.GetProfileGameDirectory(profileId), "logs")).FullName;
        var logContent = new string('A', 600 * 1024) +
                         "\nBearer final-secret-token\nlast-line";
        await File.WriteAllTextAsync(
            Path.Combine(logsDirectory, "latest.log"),
            logContent);
        var service = new JsonGameDiagnosticsService(launcherRoot);

        var result = await service.CreateBundleAsync(
            new GameDiagnosticBundleRequest(
                dataRoot,
                profileId,
                LastExit: null,
                SensitiveValues: []));

        Assert.True(result.IncludedLatestLog);
        Assert.False(result.IncludedCrashReport);
        using var archive = ZipFile.OpenRead(result.BundlePath);
        var latestLog = await ReadEntryAsync(archive.GetEntry("logs/latest.log")!);
        Assert.StartsWith("[Earlier log content omitted]", latestLog);
        Assert.Contains("last-line", latestLog);
        Assert.DoesNotContain("final-secret-token", latestLog);
    }

    [Fact]
    public async Task CreateBundleAsync_RetainsOnlyTenGeneratedBundles()
    {
        using var temporary = new TemporaryDirectory();
        var launcherRoot = Path.Combine(temporary.Path, "launcher");
        var dataRoot = Path.Combine(temporary.Path, "game-data");
        var profileId = "base-1.21.11";
        PrepareProfile(dataRoot, profileId);
        var service = new JsonGameDiagnosticsService(launcherRoot);
        var request = new GameDiagnosticBundleRequest(
            dataRoot,
            profileId,
            LastExit: null,
            SensitiveValues: []);

        for (var index = 0; index < 12; index++)
        {
            await service.CreateBundleAsync(request);
        }

        Assert.Equal(
            10,
            Directory.EnumerateFiles(
                service.DiagnosticsDirectory,
                "Hechao-Diagnostic-*.zip").Count());
    }

    [Fact]
    public async Task CreateBundleAsync_RejectsExitRecordFromAnotherProfile()
    {
        using var temporary = new TemporaryDirectory();
        var dataRoot = Path.Combine(temporary.Path, "game-data");
        var profileId = "base-1.21.11";
        PrepareProfile(dataRoot, profileId);
        var service = new JsonGameDiagnosticsService(
            Path.Combine(temporary.Path, "launcher"));
        var unrelatedExit = new GameExitRecord(
            Guid.NewGuid(),
            "activity-neoforge",
            1234,
            1,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateBundleAsync(new GameDiagnosticBundleRequest(
                dataRoot,
                profileId,
                unrelatedExit,
                SensitiveValues: [])));
    }

    private static ClientStorageLayout PrepareProfile(
        string dataRoot,
        string profileId)
    {
        var layout = new ClientStorageLayout(dataRoot);
        layout.EnsureBaseDirectories();
        Directory.CreateDirectory(layout.GetProfileGameDirectory(profileId));
        return layout;
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.Launcher.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
