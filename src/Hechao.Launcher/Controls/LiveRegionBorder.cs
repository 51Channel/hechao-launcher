using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Hechao.Launcher.Controls;

public sealed class LiveRegionBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new LiveRegionBorderAutomationPeer(this);

    public void RaiseLiveRegionChanged()
    {
        UIElementAutomationPeer.CreatePeerForElement(this)?
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private sealed class LiveRegionBorderAutomationPeer(LiveRegionBorder owner)
        : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(LiveRegionBorder);

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Text;
    }
}
