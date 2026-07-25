using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class MicrosoftBrowserCompletionPageTests
{
    [Fact]
    public void CreateOptions_ProvidesBrandedChineseSuccessAndRetryPages()
    {
        var options = MicrosoftBrowserCompletionPage.CreateOptions();

        Assert.Contains("赫朝启动器", options.HtmlMessageSuccess);
        Assert.Contains("验证成功", options.HtmlMessageSuccess);
        Assert.Contains("关闭此标签页", options.HtmlMessageSuccess);
        Assert.Contains("需要重试", options.HtmlMessageError);
        Assert.Contains("绑定 Microsoft 正版身份", options.HtmlMessageError);
        Assert.DoesNotContain("Authentication complete", options.HtmlMessageSuccess);
    }
}
