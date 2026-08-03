namespace Hechao.ServerControlAgent.Tests;

public sealed class SharedFileReaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-shared-read-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Open_AllowsFileRenameWhileHandleRemainsOpen()
    {
        var source = Path.Combine(root, "ActivityNeoForge");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, "server.properties");
        var destination = Path.Combine(source, "server.properties.moved");
        File.WriteAllText(path, "server-port=25568\n");

        using var stream = SharedFileReader.Open(path);

        File.Move(path, destination);

        Assert.True(File.Exists(destination));
        Assert.Equal('s', (char)stream.ReadByte());
    }

    [Fact]
    public void Editors_ReadExpectedValuesThroughSharedReader()
    {
        Directory.CreateDirectory(root);
        var properties = Path.Combine(root, "server.properties");
        File.WriteAllText(
            properties,
            "max-players=20\nview-distance=10\nsimulation-distance=8\n" +
            "difficulty=normal\nwhite-list=false\n");
        var memory = Path.Combine(root, "user_jvm_args.txt");
        File.WriteAllText(memory, "-Xms2G -Xmx6G\n");

        Assert.Equal(
            new Hechao.Contracts.ServerQuickSettings(20, 10, 8, "normal", false),
            ServerPropertiesEditor.Read(properties));
        Assert.Equal(
            new JvmMemorySettings(2048, 6144),
            JvmMemorySettingsEditor.Read(memory, 8192));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
