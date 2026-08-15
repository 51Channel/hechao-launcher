using Hechao.Api.ServerControl;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class DeploymentSlotRulesTests
{
    [Fact]
    public void Validate_AcceptsSafeDynamicSlotRequest()
    {
        var request = new AdminCreateDeploymentSlotRequest(
            "activity-ready-check",
            "就绪检查槽",
            "activity",
            "CREATE activity-ready-check",
            "为新活动创建独立部署槽");

        Assert.Empty(DeploymentSlotRules.Validate(request));
    }

    [Theory]
    [InlineData("activity")]
    [InlineData("survival2")]
    [InlineData("activity-Bad")]
    [InlineData("activity-a")]
    public void Validate_RejectsUnsafeDynamicSlotId(string serverId)
    {
        var request = new AdminCreateDeploymentSlotRequest(
            serverId,
            "测试槽",
            "activity",
            $"CREATE {serverId}",
            "创建测试部署槽");

        Assert.Contains("serverId", DeploymentSlotRules.Validate(request).Keys);
    }

    [Fact]
    public void Validate_RejectsDynamicSlotAsProvisioningTemplate()
    {
        var request = new AdminCreateDeploymentSlotRequest(
            "activity-autumn",
            "秋季活动槽",
            "activity-summer",
            "CREATE activity-autumn",
            "创建秋季活动部署槽");

        Assert.Contains(
            "templateServerId",
            DeploymentSlotRules.Validate(request).Keys);
    }
}
