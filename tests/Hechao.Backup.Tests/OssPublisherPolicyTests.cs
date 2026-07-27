using System.Text.Json;
using Xunit;

namespace Hechao.Backup.Tests;

public sealed class OssPublisherPolicyTests
{
    [Fact]
    public void PublisherPolicy_IsLimitedToApprovedObjectPrefixes()
    {
        var policyPath = Path.Combine(
            AppContext.BaseDirectory,
            "DeploymentAssets",
            "hechao-launcher-publisher-policy.json");
        using var policy = JsonDocument.Parse(File.ReadAllText(policyPath));

        var root = policy.RootElement;
        AssertExactSet(
            ["Version", "Statement"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("1", root.GetProperty("Version").GetString());

        var statements = root.GetProperty("Statement").EnumerateArray().ToArray();
        var statement = Assert.Single(statements);
        AssertExactSet(
            ["Effect", "Action", "Resource"],
            statement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Allow", statement.GetProperty("Effect").GetString());

        AssertExactSet(
            [
                "oss:GetObject",
                "oss:PutObject"
            ],
            statement
                .GetProperty("Action")
                .EnumerateArray()
                .Select(value => value.GetString()!));

        AssertExactSet(
            [
                "acs:oss:*:*:hechaoworld/objects/*",
                "acs:oss:*:*:hechaoworld/releases/launcher/*",
                "acs:oss:*:*:hechaoworld/backups/database/*",
                "acs:oss:*:*:hechaoworld/backups/services/*",
                "acs:oss:*:*:hechaoworld/backups/recovery/*"
            ],
            statement
                .GetProperty("Resource")
                .EnumerateArray()
                .Select(value => value.GetString()!));
    }

    private static void AssertExactSet(
        IEnumerable<string> expectedValues,
        IEnumerable<string> actualValues)
    {
        var expected = expectedValues.ToHashSet(StringComparer.Ordinal);
        var actualList = actualValues.ToArray();
        var actual = actualList.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected.Count, actualList.Length);
        Assert.True(
            expected.SetEquals(actual),
            $"Expected [{string.Join(", ", expected.Order())}] but found " +
            $"[{string.Join(", ", actual.Order())}].");
    }
}
