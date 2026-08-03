using Hechao.Api.ServerControl;
using Hechao.Contracts;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class ServerControlRulesTests
{
    [Fact]
    public void Validate_AcceptsConfirmedMinecraftConsoleCommand()
    {
        var request = new AdminServerControlRequest(
            ServerControlAction.ConsoleCommand,
            "activity",
            "检查当前在线玩家",
            "list");

        var errors = ServerControlRules.Validate("activity", request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsMissingSecondConfirmation()
    {
        var request = new AdminServerControlRequest(
            ServerControlAction.Stop,
            "wrong-server",
            "结束本次活动并停服");

        var errors = ServerControlRules.Validate("activity", request);

        Assert.Contains("confirmation", errors);
    }

    [Theory]
    [InlineData("list\r\nstop")]
    [InlineData("list\0stop")]
    [InlineData("")]
    public void Validate_RejectsMultilineOrEmptyConsoleCommand(string command)
    {
        var request = new AdminServerControlRequest(
            ServerControlAction.ConsoleCommand,
            "activity",
            "检查服务器状态",
            command);

        var errors = ServerControlRules.Validate("activity", request);

        Assert.Contains("consoleCommand", errors);
    }

    [Fact]
    public void Validate_RejectsSettingsOutsideSafeRanges()
    {
        var request = new AdminServerControlRequest(
            ServerControlAction.ApplySettings,
            "activity",
            "调整活动服快捷设置",
            Settings: new ServerQuickSettings(
                0,
                33,
                1,
                "impossible",
                false));

        var errors = ServerControlRules.Validate("activity", request);

        Assert.Contains("settings", errors);
    }

    [Fact]
    public void Validate_AcceptsSettingsWithManagedMemory()
    {
        var request = new AdminServerControlRequest(
            ServerControlAction.ApplySettings,
            "activity",
            "调整活动服人数与启动内存",
            Settings: new ServerQuickSettings(
                60,
                10,
                8,
                "normal",
                false,
                InitialMemoryMiB: 2048,
                MaximumMemoryMiB: 6144,
                MaximumAllowedMemoryMiB: 8192));

        var errors = ServerControlRules.Validate("activity", request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsApplySettingsWithoutManagedMemory()
    {
        var request = new AdminServerControlRequest(
            ServerControlAction.ApplySettings,
            "activity",
            "只提交旧版快捷设置",
            Settings: new ServerQuickSettings(60, 10, 8, "normal", false));

        var errors = ServerControlRules.Validate("activity", request);

        Assert.Contains("settings", errors);
    }

    [Fact]
    public void Validate_RejectsAdministratorForgedPackageDeployment()
    {
        var request = new AdminServerControlRequest(
            ServerControlAction.DeployPackage,
            "activity",
            "尝试绕过整合包确认门");

        var errors = ServerControlRules.Validate("activity", request);

        Assert.Contains("action", errors);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(481)]
    public void Options_RejectPackageDeploymentLeaseOutsideBoundaries(int minutes)
    {
        var options = new ServerControlOptions
        {
            Enabled = true,
            PackageDeploymentClaimLeaseMinutes = minutes,
            AgentTokenSha256 = new Dictionary<string, string>
            {
                ["owl5"] = new string('a', 64)
            }
        };

        Assert.False(options.IsValid());
    }

    [Fact]
    public void Validate_RejectsDuplicateHeartbeatTargets()
    {
        var target = new ServerControlAgentTargetHeartbeat(
            "activity",
            null,
            25568,
            false,
            null,
            null,
            ["list"],
            string.Empty,
            DateTimeOffset.UtcNow);
        var request = new ServerControlAgentHeartbeatRequest(
            "owl5",
            "0.1.0",
            DateTimeOffset.UtcNow,
            [target, target]);

        var errors = ServerControlRules.Validate(request);

        Assert.Contains("targets", errors);
    }

    [Fact]
    public void Validate_RejectsHeartbeatConsoleTailWithNullCharacter()
    {
        var target = new ServerControlAgentTargetHeartbeat(
            "activity",
            null,
            25568,
            true,
            4120,
            new ServerQuickSettings(60, 10, 8, "normal", false),
            ["list"],
            "normal line\0invalid",
            DateTimeOffset.UtcNow);
        var request = new ServerControlAgentHeartbeatRequest(
            "owl5",
            "0.1.0",
            DateTimeOffset.UtcNow,
            [target]);

        var errors = ServerControlRules.Validate(request);

        Assert.Contains("targets", errors);
    }

    [Fact]
    public void Validate_RejectsMoreThanOneActivePackageDeployment()
    {
        var target = new ServerControlAgentTargetHeartbeat(
            "activity",
            "owl5-activity-slot",
            25568,
            false,
            null,
            null,
            ["list"],
            string.Empty,
            DateTimeOffset.UtcNow,
            PackageDeploymentEnabled: true);
        var request = new ServerControlAgentHeartbeatRequest(
            "owl5",
            "0.3.0",
            DateTimeOffset.UtcNow,
            [target],
            [Guid.NewGuid(), Guid.NewGuid()]);

        var errors = ServerControlRules.Validate(request);

        Assert.Contains("activeDeploymentCommandIds", errors);
    }

    [Fact]
    public void TokenValidator_BindsTokenToExactAgent()
    {
        const string token = "abcdefghijklmnopqrstuvwxyz_0123456789-ABCDE";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(token)));
        var validator = new ServerControlTokenValidator(
            Options.Create(new ServerControlOptions
            {
                Enabled = true,
                AgentTokenSha256 = new Dictionary<string, string>
                {
                    ["owl5"] = hash
                }
            }));

        Assert.True(validator.IsValid("owl5", token));
        Assert.False(validator.IsValid("owl9", token));
        Assert.False(validator.IsValid("owl5", token + "wrong"));
    }

    [Fact]
    public void Options_RequireAtLeastOneAgentWhenEnabled()
    {
        var options = new ServerControlOptions { Enabled = true };

        Assert.False(options.IsValid());
    }
}
