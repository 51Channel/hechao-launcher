namespace Hechao.ServerControlAgent.Tests;

public sealed class JvmMemorySettingsEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "hechao-memory-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Read_ParsesMixedMegabyteAndGigabyteArguments()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "start.bat");
        File.WriteAllText(
            path,
            "@echo off\r\njava -Xms512M -Xmx6G -jar server.jar nogui\r\n");

        var settings = JvmMemorySettingsEditor.Read(path, 8192);

        Assert.Equal(new JvmMemorySettings(512, 6144), settings);
    }

    [Fact]
    public void Apply_ChangesOnlyMemoryArgumentsAndCreatesBackup()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "user_jvm_args.txt");
        var original = "# 保留原始注释\r\n-Xms2G\r\n-Xmx6G\r\n-Dfile.encoding=UTF-8\r\n";
        File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(original));
        var backupRoot = Path.Combine(_root, "backups");

        JvmMemorySettingsEditor.Apply(
            path,
            backupRoot,
            "activity",
            initialMemoryMiB: 3072,
            maximumMemoryMiB: 7168,
            maximumAllowedMemoryMiB: 8192);

        var updated = File.ReadAllText(path);
        Assert.Contains("# 保留原始注释", updated, StringComparison.Ordinal);
        Assert.Contains("-Xms3072M", updated, StringComparison.Ordinal);
        Assert.Contains("-Xmx7168M", updated, StringComparison.Ordinal);
        Assert.Contains("-Dfile.encoding=UTF-8", updated, StringComparison.Ordinal);
        var backup = Assert.Single(Directory.EnumerateFiles(
            backupRoot,
            "user_jvm_args.txt",
            SearchOption.AllDirectories));
        Assert.Equal(original, File.ReadAllText(backup));
    }

    [Fact]
    public void EnsureCanApply_RejectsDuplicateArguments()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "start.ps1");
        File.WriteAllText(path, "java -Xms2G -Xms3G -Xmx6G -jar server.jar");

        var exception = Assert.Throws<InvalidDataException>(() =>
            JvmMemorySettingsEditor.EnsureCanApply(path, 2048, 6144, 8192));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RejectsMemoryAboveTargetLimitWithoutChangingFile()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "start.bat");
        const string original = "java -Xms2G -Xmx4G -jar server.jar";
        File.WriteAllText(path, original);

        Assert.Throws<InvalidDataException>(() =>
            JvmMemorySettingsEditor.Apply(
                path,
                Path.Combine(_root, "backups"),
                "pvp-purpur",
                initialMemoryMiB: 4096,
                maximumMemoryMiB: 8192,
                maximumAllowedMemoryMiB: 6144));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.False(Directory.Exists(Path.Combine(_root, "backups")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
