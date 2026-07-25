using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Hechao.Launcher.Controls;

namespace Hechao.Launcher.Tests;

public sealed class AnimatedProgressBarTests
{
    [Fact]
    public void ValueChange_AnimatesTheDisplayedRatio()
    {
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;
        var animationsEnabled = false;
        var initialRatio = -1d;
        var midpointRatio = -1d;
        var finalRatio = -1d;

        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var progressBar = new AnimatedProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0
                };
                var window = new Window
                {
                    Width = 240,
                    Height = 40,
                    Left = -10_000,
                    Top = -10_000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = progressBar
                };

                progressBar.Loaded += (_, _) =>
                {
                    animationsEnabled = SystemParameters.ClientAreaAnimation;
                    progressBar.Value = 80;
                    initialRatio = progressBar.DisplayRatio;

                    DispatcherTimer? midpointTimer = null;
                    midpointTimer = new DispatcherTimer(
                        TimeSpan.FromMilliseconds(70),
                        DispatcherPriority.Render,
                        (_, _) =>
                        {
                            midpointTimer!.Stop();
                            midpointRatio = progressBar.DisplayRatio;
                        },
                        dispatcher);
                    midpointTimer.Start();

                    var completionTimer = new DispatcherTimer(
                        TimeSpan.FromMilliseconds(260),
                        DispatcherPriority.Render,
                        (_, _) =>
                        {
                            finalRatio = progressBar.DisplayRatio;
                            window.Close();
                            dispatcher.BeginInvokeShutdown(
                                DispatcherPriority.Background);
                        },
                        dispatcher);
                    completionTimer.Start();
                };

                window.Show();
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
            completed.Wait(TimeSpan.FromSeconds(10)),
            "The WPF progress animation test did not complete.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        Assert.InRange(finalRatio, 0.79d, 0.81d);
        if (animationsEnabled)
        {
            Assert.InRange(initialRatio, 0d, 0.1d);
            Assert.InRange(midpointRatio, 0.1d, 0.79d);
        }
        else
        {
            Assert.InRange(initialRatio, 0.79d, 0.81d);
            Assert.InRange(midpointRatio, 0.79d, 0.81d);
        }
    }
}
