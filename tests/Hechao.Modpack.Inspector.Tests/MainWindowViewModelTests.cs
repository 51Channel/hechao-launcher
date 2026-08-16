using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Threading;
using Hechao.Modpack;
using Xunit;

namespace Hechao.Modpack.Inspector.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-modpack-inspector-vm-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public Task InitialState_IsReadyForArchiveSelection() =>
        RunOnStaThreadAsync(() =>
        {
            var viewModel = new MainWindowViewModel();

            Assert.True(viewModel.CanSelectArchive);
            Assert.False(viewModel.CanReinspect);
            Assert.False(viewModel.CanExport);
            Assert.Equal("等待检查", viewModel.ResultTitle);
            return Task.CompletedTask;
        });

    [Fact]
    public Task InspectAsync_ExposesReviewStateAndFiltersChecks() =>
        RunOnStaThreadAsync(async () =>
        {
            var archive = CreateFabricArchiveWithoutCoreDeclaration();
            var viewModel = new MainWindowViewModel();

            await viewModel.InspectAsync(archive);

            Assert.NotNull(viewModel.Report);
            Assert.Equal(DeploymentReadiness.ReviewRequired, viewModel.Report!.Readiness);
            Assert.Equal("需要人工复核", viewModel.ResultTitle);
            Assert.True(viewModel.CanExport);
            Assert.Contains(
                viewModel.Report.Checks,
                check => check.Code == "SERVER_CORE_UNDECLARED");

            viewModel.SetFilter("Warning");
            var visible = viewModel.FilteredChecks.Cast<CheckItemViewModel>().ToArray();
            Assert.NotEmpty(visible);
            Assert.All(visible, item =>
                Assert.Equal(DeploymentCheckStatus.Warning, item.Status));
        });

    private string CreateFabricArchiveWithoutCoreDeclaration()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "review-required.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "hechao-pack.json", """
            {
              "schemaVersion":1,
              "id":"review-required",
              "displayName":"待复核整合包",
              "version":"1.0.0",
              "minecraftVersion":"1.21.1",
              "javaMajorVersion":21,
              "loader":"Fabric",
              "loaderVersion":"0.16.14",
              "clientRoot":"client",
              "serverRoot":"server",
              "sharedRoot":"shared"
            }
            """);
        Add(archive, "client/versions/1.21.1/1.21.1.json", "{}");
        Add(archive, "server/server.properties", "server-ip=127.0.0.1\nonline-mode=false\n");
        Add(archive, "server/eula.txt", "eula=true\n");
        Add(archive, "server/user_jvm_args.txt", "-Xms1024M\n-Xmx4096M\n");
        Add(
            archive,
            "server/start.bat",
            "@echo off\nif not defined HECHAO_MANAGED_START pause\njava @user_jvm_args.txt -jar fabric-server-launch.jar nogui\n");
        Add(archive, "server/fabric-server-launch.jar", "fabric");
        return path;
    }

    private static void Add(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static Task RunOnStaThreadAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
