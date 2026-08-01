using System.Runtime.ExceptionServices;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Hechao.Launcher.Controls;

namespace Hechao.Launcher.Tests;

public sealed class LiveRegionBorderTests
{
    [Fact]
    public void AutomationPeer_ExposesPoliteTextLiveRegion()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var region = new LiveRegionBorder();
                AutomationProperties.SetName(region, "下载完成");
                AutomationProperties.SetLiveSetting(
                    region,
                    AutomationLiveSetting.Polite);

                region.RaiseLiveRegionChanged();
                var peer = UIElementAutomationPeer.CreatePeerForElement(region);

                Assert.NotNull(peer);
                Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
                Assert.Equal(nameof(LiveRegionBorder), peer.GetClassName());
                Assert.Equal("下载完成", peer.GetName());
                Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
