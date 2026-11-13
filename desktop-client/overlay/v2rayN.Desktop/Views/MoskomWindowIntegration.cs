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
        if (advanced is Control control) root.Children.Add(new ContentControl { Content = control, IsVisible = false, IsEnabled = false });
        root.Children.Add(new MoskomLauncherView(this, vm)); Content = root;
        Title = MoskomLauncherRuntime.DisplayName;
        Background = Brush.Parse("#0A1020"); Width = 500; Height = 900; MinWidth = 420; MinHeight = 720;
        if (MoskomLauncherRuntime.SmokeTest)
        {
            Opened += (_, _) => DispatcherTimer.RunOnce(async () =>
            {
                try
                {
                    // Exercise a real subscription import against loopback only. No live keys/network.
                    var uris = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"vless://11111111-1111-4111-8111-111111111111@127.0.0.1:{4400 + i}?encryption=none&security=none&type=tcp#TestServer-{i}"));
                    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
                    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                    var served = Task.Run(async () =>
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        using var client = await listener.AcceptTcpClientAsync(timeout.Token); var stream = client.GetStream();
                        var buffer = new byte[8192]; _ = await stream.ReadAsync(buffer, timeout.Token);
                        var body = Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(uris)));
                        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header, timeout.Token); await stream.WriteAsync(body, timeout.Token);
                    });
                    try { await MoskomLauncherRuntime.ImportAsync($"http://127.0.0.1:{port}/subscription"); await served; }
                    finally { listener.Stop(); }
                    var imported = vm.StatusBarViewModel.Servers.Count == 30;
                    await MoskomLauncherRuntime.ImportAsync("vless://22222222-2222-4222-8222-222222222222@127.0.0.1:5443?encryption=none&security=none&type=tcp#Standalone-Test");
                    var allServersVisible = vm.StatusBarViewModel.Servers.Count == 31;
                    if (allServersVisible) { await MoskomLauncherRuntime.SelectServerAsync(vm.StatusBarViewModel.Servers.Last()); await MoskomLauncherRuntime.SelectServerAsync(vm.StatusBarViewModel.Servers.First()); }
                    var routes = await AppManager.Instance.RoutingItems();
                    if (routes.Count >= 2) { await MoskomLauncherRuntime.SelectRoutingAsync(routes[1]); await MoskomLauncherRuntime.SelectRoutingAsync(routes[0]); }
                    var blocked = false;
                    try { await MoskomLauncherRuntime.Connection!.ConnectAsync(); } catch (InvalidOperationException) { blocked = true; }
                    await MoskomLauncherRuntime.Connection!.DisconnectAsync();
                    var website = await MoskomWebsiteSmoke.RunAsync(this, vm);
                    await Task.Delay(200);
                    var report = new
                    {
                        title = Title,
                        controls = this.GetVisualDescendants().OfType<Control>().Select(c => c.Name).Where(n => n != null && n.StartsWith("Moskom")).Distinct().ToArray(),
                        routing = vm.StatusBarViewModel.RoutingItems.Select(r => r.Remarks).ToArray(),
                        profiles = vm.StatusBarViewModel.Servers.Count,
                        subscriptionImported = imported,
                        allServersVisible,
                        smokeConnectionBlocked = blocked,
                        routingRulesLoaded = routes.All(r => r.RuleNum > 0),
                        proxyUnchanged = AppManager.Instance.Config.SystemProxyItem.SysProxyType == ESysProxyType.Unchanged,
                        tunDisabled = !AppManager.Instance.Config.TunModeItem.EnableTun,
                        status = MoskomLauncherRuntime.Connection?.State.ToString()
                        , website
                        , vectorIcon = this.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any(p => p.Name == "MoskomPowerIcon" && p.Data != null)
                    };
                    var folder = Environment.GetEnvironmentVariable("MOSKOM_SMOKE_OUTPUT") ?? Utils.StartupPath(); Directory.CreateDirectory(folder);
                    await File.WriteAllTextAsync(System.IO.Path.Combine(folder, "smoke.json"), System.Text.Json.JsonSerializer.Serialize(report));
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height)); bitmap.Render(this); bitmap.Save(System.IO.Path.Combine(folder, "launcher.png"));
                    if (report.routing.Length < 3 || !report.proxyUnchanged || !report.tunDisabled || report.status != "Disconnected" || !report.subscriptionImported || !report.allServersVisible || !report.smokeConnectionBlocked || !report.routingRulesLoaded || !report.vectorIcon || !website.LoginForm || !website.MultipleSubscriptions || !website.RotatedLink || !website.ExpiredSkipped || !website.RevokedRemoved || !website.SessionEncrypted || !website.LogoutPreservedManual || !website.AvailabilityChecked) Environment.ExitCode = 3;
                }
                catch (Exception ex)
                {
                    Environment.ExitCode = 4;
                    var folder = Environment.GetEnvironmentVariable("MOSKOM_SMOKE_OUTPUT") ?? Utils.StartupPath();
                    Directory.CreateDirectory(folder); await File.WriteAllTextAsync(System.IO.Path.Combine(folder, "smoke-error.txt"), ex.ToString());
                }
                await AppManager.Instance.AppExitAsync(true);
            }, TimeSpan.FromSeconds(4));
        }
    }
}
