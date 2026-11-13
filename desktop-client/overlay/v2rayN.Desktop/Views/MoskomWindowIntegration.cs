using ServiceLib.Services;
using Avalonia.VisualTree;

namespace v2rayN.Desktop.Views;

public partial class MainWindow
{
    public void EnableMoskomLauncher(MainWindowViewModel vm)
    {
        MoskomLauncherRuntime.Attach(vm);
        var advanced = Content; Content = null;
        var root = new Grid();
        if (advanced is Control control) root.Children.Add(new ContentControl { Content = control, IsVisible = false });
        root.Children.Add(new MoskomLauncherView(this, vm)); Content = root;
        Title = MoskomLauncherRuntime.DisplayName;
        Background = Brush.Parse("#0A1020"); Width = 480; Height = 790; MinWidth = 420; MinHeight = 650;
        if (MoskomLauncherRuntime.SmokeTest)
        {
            Opened += (_, _) => DispatcherTimer.RunOnce(async () =>
            {
                try
                {
                    var report = new
                    {
                        title = Title,
                        controls = this.GetVisualDescendants().OfType<Control>().Select(c => c.Name).Where(n => n != null && n.StartsWith("Moskom")).Distinct().ToArray(),
                        routing = vm.StatusBarViewModel.RoutingItems.Select(r => r.Remarks).ToArray(),
                        profiles = vm.StatusBarViewModel.Servers.Count,
                        proxyUnchanged = AppManager.Instance.Config.SystemProxyItem.SysProxyType == ESysProxyType.Unchanged,
                        tunDisabled = !AppManager.Instance.Config.TunModeItem.EnableTun,
                        status = MoskomLauncherRuntime.Connection?.State.ToString()
                    };
                    var folder = Environment.GetEnvironmentVariable("MOSKOM_SMOKE_OUTPUT") ?? Utils.StartupPath(); Directory.CreateDirectory(folder);
                    await File.WriteAllTextAsync(System.IO.Path.Combine(folder, "smoke.json"), System.Text.Json.JsonSerializer.Serialize(report));
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height)); bitmap.Render(this); bitmap.Save(System.IO.Path.Combine(folder, "launcher.png"));
                    if (report.routing.Length < 3 || !report.proxyUnchanged || !report.tunDisabled || report.status != "Disconnected") Environment.ExitCode = 3;
                }
                catch (Exception ex) { Environment.ExitCode = 4; Console.Error.WriteLine(ex.GetType().Name); }
                await AppManager.Instance.AppExitAsync(true);
            }, TimeSpan.FromSeconds(4));
        }
    }
}
