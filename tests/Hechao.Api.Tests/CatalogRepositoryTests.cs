using Hechao.Api.Catalog;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class CatalogRepositoryTests
{
    [Theory]
    [InlineData("activity", ServerCatalogSection.Activity)]
    [InlineData("survival2", ServerCatalogSection.Permanent)]
    [InlineData("Activity", ServerCatalogSection.Permanent)]
    public void ResolveCatalogSectionMapsOnlyActivityTargetToActivity(
        string velocityTarget,
        ServerCatalogSection expected)
    {
        Assert.Equal(expected, CatalogRepository.ResolveCatalogSection(velocityTarget));
    }
}
