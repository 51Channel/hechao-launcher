namespace Hechao.Distribution.Tests;

public sealed class ManifestValidatorTests
{
    [Theory]
    [InlineData("../server.properties")]
    [InlineData("mods/../../server.properties")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("mods\\example.jar")]
    [InlineData("mods/CON.jar")]
    [InlineData(".hechao/cache/object")]
    [InlineData(".hechao-install.json")]
    public void Validate_RejectsUnsafeManagedPaths(string path)
    {
        var manifest = ManifestTestData.CreateManifest("content"u8.ToArray(), path);

        Assert.Throws<ManifestFormatException>(() => ManifestValidator.Validate(manifest));
    }

    [Fact]
    public void Validate_RejectsPlainHttpForRemoteHost()
    {
        var manifest = ManifestTestData.CreateManifest("content"u8.ToArray());
        var changed = manifest with
        {
            Files = [manifest.Files[0] with { Url = "http://download.hechao.world/object" }]
        };

        Assert.Throws<ManifestFormatException>(() => ManifestValidator.Validate(changed));
    }

    [Fact]
    public void Validate_AllowsPlainHttpForLoopbackDevelopment()
    {
        var manifest = ManifestTestData.CreateManifest("content"u8.ToArray());
        var changed = manifest with
        {
            Files = [manifest.Files[0] with { Url = "http://127.0.0.1:8080/object" }]
        };

        ManifestValidator.Validate(changed);
    }
}
