namespace Hechao.Publisher.Tests;

public sealed class PublisherProductInfoTests
{
    [Fact]
    public void UserAgent_UsesAssemblyVersion()
    {
        Assert.Equal("Hechao.Publisher", PublisherProductInfo.ProductName);
        Assert.Equal(
            $"{PublisherProductInfo.ProductName}/{PublisherProductInfo.Version}",
            PublisherProductInfo.UserAgent);
        Assert.Matches(@"^\d+\.\d+\.\d+$", PublisherProductInfo.Version);
    }
}
