using System.IO;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace Hechao.Modpack.Inspector.Tests;

[Collection("WPF Rendering")]
public sealed class MainWindowRenderingTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-modpack-inspector-render-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void BlockedReport_RendersAtDefaultAndMinimumWindowSizes()
    {
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;
        var renderedSizes = new List<(int Width, int Height)>();
        var nonBackgroundRatios = new List<double>();
        var archive = CreateBlockedArchive();

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            try
            {
                var app = new App();
                app.InitializeComponent();
                var window = new MainWindow
                {
                    Left = -10_000,
                    Top = -10_000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };
                window.Show();

                dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
                        await viewModel.InspectAsync(archive);
                        var artifactDirectory = Environment.GetEnvironmentVariable(
                            "HECHAO_INSPECTOR_RENDER_DIRECTORY");
                        foreach (var size in new[] { (1180d, 780d), (960d, 640d) })
                        {
                            window.Width = size.Item1;
                            window.Height = size.Item2;
                            window.UpdateLayout();
                            await dispatcher.InvokeAsync(
                                () => { },
                                DispatcherPriority.ApplicationIdle);

                            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                            var bitmap = Render(content);
                            renderedSizes.Add((bitmap.PixelWidth, bitmap.PixelHeight));
                            nonBackgroundRatios.Add(GetNonBackgroundRatio(bitmap));
                            if (!string.IsNullOrWhiteSpace(artifactDirectory))
                            {
                                Directory.CreateDirectory(artifactDirectory);
                                var name = $"modpack-inspector-{(int)size.Item1}x{(int)size.Item2}.png";
                                SavePng(bitmap, Path.Combine(artifactDirectory, name));
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        window.Close();
                        app.Shutdown();
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                });

                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(
            completed.Wait(TimeSpan.FromSeconds(20)),
            "WPF 检查器离屏渲染未在 20 秒内完成。");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        Assert.Equal(2, renderedSizes.Count);
        Assert.All(renderedSizes, size =>
        {
            Assert.True(size.Width >= 900);
            Assert.True(size.Height >= 560);
        });
        Assert.All(nonBackgroundRatios, ratio => Assert.InRange(ratio, 0.01, 0.60));
    }

    private string CreateBlockedArchive()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "industrial-invalid.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "hechao-pack.json", """
            {
              "schemaVersion":1,
              "id":"industrial-neoforge-1.21.1",
              "displayName":"工业季",
              "version":"1.0.0",
              "minecraftVersion":"1.21.1",
              "javaMajorVersion":21,
              "loader":"NeoForge",
              "loaderVersion":"21.1.228",
              "serverCore":"Arclight",
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
            "@echo off\nif not defined HECHAO_MANAGED_START pause\njava @user_jvm_args.txt @libraries/net/neoforged/neoforge/21.1.228/win_args.txt nogui\n");
        Add(archive, "server/arclight-neoforge-1.21.1.jar", "arclight");
        Add(archive, "server/libraries/net/neoforged/neoforge/21.1.228/win_args.txt", "args");
        return path;
    }

    private static RenderTargetBitmap Render(FrameworkElement content)
    {
        var dpi = VisualTreeHelper.GetDpi(content);
        var width = Math.Max(1, (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(content);
        return bitmap;
    }

    private static double GetNonBackgroundRatio(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var nonBackground = 0L;
        var pixelCount = (long)bitmap.PixelWidth * bitmap.PixelHeight;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            if (red < 235 || green < 235 || blue < 235)
            {
                nonBackground++;
            }
        }

        return nonBackground / (double)pixelCount;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Add(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

[CollectionDefinition("WPF Rendering", DisableParallelization = true)]
public sealed class WpfRenderingCollection;
