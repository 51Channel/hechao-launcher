using Hechao.Distribution;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Tests;

public sealed class DownloadJobViewModelTests
{
    [Fact]
    public void CheckingPhase_IsNotPresentedAsNetworkDownload()
    {
        var job = CreateRunningJob(ClientInstallPhase.Checking, 1024);

        job.Update(ClientInstallPhase.Checking, 5, 512, 1024, "mods/example.jar");

        Assert.Equal("正在检查本地文件", job.StatusText);
        Assert.Equal("已检查 512 B / 1 KB", job.ProgressText);
        Assert.Equal(0, job.BytesPerSecond);
    }

    [Fact]
    public void DownloadingPhase_ShowsOnlyIncrementalTransferSize()
    {
        var job = CreateRunningJob(ClientInstallPhase.Checking, 1024 * 1024 * 1024L);

        job.Update(ClientInstallPhase.Downloading, 10, 0, 27_450, string.Empty);
        job.Update(ClientInstallPhase.Downloading, 80, 27_450, 27_450, "mods/changed.jar");

        Assert.Equal("正在增量下载", job.StatusText);
        Assert.Equal("26.8 KB / 26.8 KB", job.ProgressText);
    }

    [Theory]
    [InlineData(ClientInstallPhase.Staging, "正在准备客户端", "正在准备客户端文件")]
    [InlineData(ClientInstallPhase.Switching, "正在切换版本", "正在安全切换版本")]
    [InlineData(ClientInstallPhase.PreparingRuntime, "正在准备配套 Java", "正在准备配套 Java")]
    public void NonNetworkPhase_UsesPhaseSpecificText(
        ClientInstallPhase phase,
        string expectedStatus,
        string expectedProgress)
    {
        var job = CreateRunningJob(ClientInstallPhase.Checking, 1024);

        job.Update(phase, 90, 1024, 1024, string.Empty);

        Assert.Equal(expectedStatus, job.StatusText);
        Assert.Equal(expectedProgress, job.ProgressText);
        Assert.Equal(0, job.BytesPerSecond);
    }

    private static DownloadJobViewModel CreateRunningJob(
        ClientInstallPhase phase,
        long totalBytes) =>
        new(
            Guid.NewGuid(),
            "industrial-neoforge-1.21.1",
            "工业季",
            "1.0.12",
            DateTimeOffset.UtcNow,
            DownloadJobStatus.Running,
            0,
            totalBytes,
            string.Empty,
            phase: phase);
}
