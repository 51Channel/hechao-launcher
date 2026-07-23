using Hechao.Api.Admin;

namespace Hechao.Api.Tests;

public sealed class AdminWebTokenGeneratorTests
{
    [Fact]
    public void Create_ReturnsUniqueUrlSafeTokens()
    {
        var generator = new AdminWebTokenGenerator();

        var tokens = Enumerable.Range(0, 64)
            .Select(_ => generator.Create())
            .ToArray();

        Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token =>
        {
            Assert.True(AdminWebTokenGenerator.IsShapeValid(token));
            Assert.Equal(43, token.Length);
            Assert.DoesNotContain("+", token, StringComparison.Ordinal);
            Assert.DoesNotContain("/", token, StringComparison.Ordinal);
            Assert.DoesNotContain("=", token, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void IsShapeValid_RejectsInvalidTokens(string? token)
    {
        Assert.False(AdminWebTokenGenerator.IsShapeValid(token));
    }

    [Fact]
    public void Hash_IsStableAndTokenSpecific()
    {
        var first = AdminWebTokenGenerator.Hash(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var repeated = AdminWebTokenGenerator.Hash(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var second = AdminWebTokenGenerator.Hash(
            "baaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Equal(32, first.Length);
        Assert.Equal(first, repeated);
        Assert.NotEqual(first, second);
    }
}
