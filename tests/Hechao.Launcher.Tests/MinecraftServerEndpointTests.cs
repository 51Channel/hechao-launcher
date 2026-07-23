using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class MinecraftServerEndpointTests
{
    [Theory]
    [InlineData("mc.hehe11.fun", "mc.hehe11.fun", 25565)]
    [InlineData("mc.hehe11.fun:25570", "mc.hehe11.fun", 25570)]
    [InlineData("127.0.0.1", "127.0.0.1", 25565)]
    [InlineData("[::1]:25570", "[::1]", 25570)]
    [InlineData("::1", "[::1]", 25565)]
    public void Parse_AcceptsSupportedEndpointForms(
        string input,
        string expectedHost,
        int expectedPort)
    {
        var endpoint = MinecraftServerEndpoint.Parse(input);

        Assert.Equal(expectedHost, endpoint.Host);
        Assert.Equal(expectedPort, endpoint.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://mc.hehe11.fun")]
    [InlineData("mc.hehe11.fun:")]
    [InlineData("mc.hehe11.fun:0")]
    [InlineData("mc.hehe11.fun:65536")]
    [InlineData("mc.hehe11.fun/path")]
    [InlineData("mc.hehe11.fun bad")]
    [InlineData("[::1")]
    [InlineData("[::1]extra")]
    public void Parse_RejectsInvalidEndpoints(string input)
    {
        Assert.Throws<InvalidOperationException>(() => MinecraftServerEndpoint.Parse(input));
    }
}
