using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using WpfMqtt1.Models;
using WpfMqtt1.Services;
using WpfMqtt1.Views;
using WpfMqtt1.Views.Pages;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WpfMqtt1.Tests;

public sealed class LayoutRegressionTests
{
    [Fact]
    public void BrokersPage_ConstrainsToViewport_AndEditorScrollViewerCanScrollToEnd()
    {
        Exception? threadEx = null;
        double pageActualHeight = 0;
        double windowHeight = 760;
        double scrollableHeight = 0;
        double maxScrollOffsetReached = 0;

        var thread = new Thread(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_Layout_" + Guid.NewGuid().ToString("N"));
            try
            {
                var app = Application.Current ?? new Application();
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(app.Dispatcher));

                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary
                    {
                        Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark
                    });
                    app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                    app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                    app.Resources["ConnectionStateToBrushConverter"] = new WpfMqtt1.Converters.ConnectionStateToBrushConverter();
                    app.Resources["EnumDisplayConverter"] = new WpfMqtt1.Converters.EnumDisplayConverter();
                    app.Resources["NullToVisibilityConverter"] = new WpfMqtt1.Converters.NullToVisibilityConverter();
                    app.Resources["InverseBooleanConverter"] = new WpfMqtt1.Converters.InverseBooleanConverter();
                    app.Resources["DoubleToIntConverter"] = new WpfMqtt1.Converters.DoubleToIntConverter();
                }

                var services = new ServiceCollection();
                App.ConfigureServices(services);
                services.AddSingleton<IAppPathsService>(new AppPathsService(tempDir));

                using var provider = services.BuildServiceProvider();
                var window = (MainWindow)provider.GetRequiredService<INavigationWindow>();
                window.Width = 1280;
                window.Height = windowHeight;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -10000;
                window.Top = -10000;
                window.ShowActivated = false;
                window.Show();

                var store = provider.GetRequiredService<IBrokerProfileStore>();
                store.UpsertAsync(new BrokerProfile
                {
                    Name = "Test Broker",
                    Host = "localhost",
                    Transport = BrokerTransportType.WebSocketSecure,
                    Port = 443,
                    AllowUntrustedCertificates = true
                }).GetAwaiter().GetResult();

                var brokersPage = provider.GetRequiredService<BrokersPage>();
                var loadTask = brokersPage.ViewModel.LoadBrokersAsync();
                while (!loadTask.IsCompleted)
                {
                    app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    Thread.Sleep(10);
                }
                loadTask.GetAwaiter().GetResult();

                window.Navigate(typeof(BrokersPage));
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                window.UpdateLayout();
                brokersPage.UpdateLayout();

                pageActualHeight = brokersPage.ActualHeight;
                var scrollViewer = (ScrollViewer)brokersPage.FindName("EditorScrollViewer");
                Assert.NotNull(scrollViewer);
                scrollableHeight = scrollViewer.ScrollableHeight;
                scrollViewer.ScrollToEnd();
                scrollViewer.UpdateLayout();
                maxScrollOffsetReached = scrollViewer.VerticalOffset;

                window.Close();
                app.Dispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(threadEx);
        Assert.True(pageActualHeight > 0 && pageActualHeight < windowHeight,
            $"Expected BrokersPage height ({pageActualHeight}) to fit inside window ({windowHeight}).");
        Assert.True(scrollableHeight > 0,
            $"Expected EditorScrollViewer to have positive ScrollableHeight, got {scrollableHeight}.");
        Assert.True(Math.Abs(maxScrollOffsetReached - scrollableHeight) < 1.0,
            $"Expected ScrollToEnd to reach {scrollableHeight}, reached {maxScrollOffsetReached}.");
    }
}
