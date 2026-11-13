using ServiceLib.Services;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace v2rayN.Desktop.Views;

internal static class MoskomWebsiteSmoke
{
    internal sealed record Report(bool LoginForm, bool MultipleSubscriptions, bool RotatedLink, bool ExpiredSkipped, bool RevokedRemoved, bool SessionEncrypted, bool LogoutPreservedManual, bool AvailabilityChecked);
    public static async Task<Report> RunAsync(Window owner, MainWindowViewModel vm)
    {
        using var stop = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; var root = new Uri($"http://127.0.0.1:{port}/");
        var revision = 0; var loggedIn = false;
        var worker = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stop.Token); }
                catch (Exception) when (stop.IsCancellationRequested) { break; }
                using (client)
                {
                    var stream = client.GetStream(); var data = new List<byte>(); var buffer = new byte[4096];
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    string request; int end;
                    do { var n = await stream.ReadAsync(buffer, timeout.Token); if (n == 0) throw new IOException("Fixture request ended."); data.AddRange(buffer.Take(n)); request = Encoding.UTF8.GetString(data.ToArray()); end = request.IndexOf("\r\n\r\n", StringComparison.Ordinal); } while (end < 0 && data.Count < 65536);
                    var lengthLine = request.Split("\r\n").FirstOrDefault(s => s.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    var length = lengthLine == null ? 0 : int.Parse(lengthLine.Split(':')[1].Trim());
                    while (data.Count < end + 4 + length) { var n = await stream.ReadAsync(buffer, timeout.Token); if (n == 0) break; data.AddRange(buffer.Take(n)); }
                    request = Encoding.UTF8.GetString(data.ToArray());
                    var path = request.Split(' ')[1]; var cookie = ""; var status = "200 OK"; string body;
                    object User() => new { id = 41, first_name = "Тестовый", last_name = "Клиент", email = "client@example.test" };
                    var authenticated = loggedIn && request.Contains("internet_account=smoke-session-opaque", StringComparison.Ordinal);
                    if (path.Contains("action=me")) body = JsonSerializer.Serialize(new { user = authenticated ? User() : null, csrf = "fixture-csrf", version = 17 });
                    else if (path.Contains("action=login"))
                    {
                        if (!request.Contains("X-CSRF-Token: fixture-csrf", StringComparison.OrdinalIgnoreCase) || !request.Contains("Origin: " + root.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) throw new IOException("Missing CSRF or Origin.");
                        loggedIn = true; cookie = "Set-Cookie: internet_account=smoke-session-opaque; Path=/; HttpOnly\r\n";
                        body = JsonSerializer.Serialize(new { user = User(), csrf = "fixture-csrf" });
                    }
                    else if (path.Contains("action=logout")) { loggedIn = false; body = "{\"csrf\":\"fixture-csrf\"}"; }
                    else if (path.Contains("action=subscriptions"))
                    {
                        if (!authenticated) { status = "401 Unauthorized"; body = "{\"error\":\"Сначала войдите\"}"; }
                        else
                        {
                            var rows = new List<object>();
                            if (revision < 2)
                            {
                                rows.Add(new { id = 801, name = "VPN-A", url = new Uri(root, revision == 0 ? "sub/a" : "sub/a2").AbsoluteUri, content_updated_at = revision == 0 ? "2026-11-13 20:00:00" : "2026-11-13 20:01:00" });
                                rows.Add(new { id = 802, name = "VPN-B", url = new Uri(root, "sub/b").AbsoluteUri, content_updated_at = "2026-11-13 20:00:00" });
                                rows.Add(new { id = 803, name = "Expired", is_expired = true, expires_at = "2020-01-01 00:00:00" });
                            }
                            body = JsonSerializer.Serialize(new { subscriptions = rows });
                        }
                    }
                    else if (path.StartsWith("/sub/", StringComparison.Ordinal))
                    {
                        var p = path.EndsWith("b", StringComparison.Ordinal) ? 6542 : path.EndsWith("a2", StringComparison.Ordinal) ? 6543 : 6541;
                        body = Convert.ToBase64String(Encoding.UTF8.GetBytes($"vless://33333333-3333-4333-8333-333333333333@127.0.0.1:{p}?encryption=none&security=none&type=tcp#Cabinet-Test"));
                    }
                    else if (path.Contains("service-notice.php")) body = "{\"enabled\":true,\"level\":\"ok\",\"message\":\"Статус и подписки синхронизируются с личным кабинетом.\"}";
                    else if (path.Contains("server-status.php"))
                    {
                        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                        body = JsonSerializer.Serialize(new { enabled = true, source_status = "ok", reported_at = stamp, nodes = new[] { new { name = "Тестовый сервер", status = "up", latency_ms = 258, checked_at = stamp } } });
                    }
                    else { status = "404 Not Found"; body = "{}"; }
                    var bytes = Encoding.UTF8.GetBytes(body); var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json; charset=utf-8\r\n{cookie}Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, timeout.Token); await stream.WriteAsync(bytes, timeout.Token);
                }
            }
        });
        try
        {
            MoskomLauncherRuntime.UseWebsiteFixture(root);
            var initial = vm.StatusBarViewModel.Servers.Count;
            var dialogTask = MoskomWebsiteViews.ShowAccountAsync(owner);
            await Task.Delay(150);
            var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            var dialog = desktop.Windows.First(w => w != owner && w.Title!.StartsWith("Личный кабинет", StringComparison.Ordinal));
            T Find<T>(string name) where T : Control => dialog.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
            var email = Find<TextBox>("MoskomEmail"); var password = Find<TextBox>("MoskomPassword");
            email.Text = "client@example.test"; password.Text = "fixture-password";
            var form = password.PasswordChar == '*' && Find<Button>("MoskomLoginSubmit") != null;
            var folder = Environment.GetEnvironmentVariable("MOSKOM_SMOKE_OUTPUT")!;
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)dialog.Bounds.Width, (int)dialog.Bounds.Height))) { bitmap.Render(dialog); bitmap.Save(Path.Combine(folder, "login.png")); }
            Find<Button>("MoskomLoginSubmit").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await dialogTask.WaitAsync(TimeSpan.FromSeconds(25));
            var groups = (await AppManager.Instance.SubItems()).Where(g => g.Memo?.StartsWith("moskom-cabinet:", StringComparison.Ordinal) == true).ToList();
            var multiple = MoskomLauncherRuntime.CabinetUser?.Id == 41 && groups.Count == 2 && vm.StatusBarViewModel.Servers.Count == initial + 2;
            var expired = groups.All(g => !g.Memo!.EndsWith(":803", StringComparison.Ordinal));
            var bId = groups.First(g => g.Memo!.EndsWith(":802", StringComparison.Ordinal)).Id;
            var sealedFile = File.ReadAllBytes(Path.Combine(Utils.StartupPath(), "cabinet-session.dat"));
            using var saved = new MoskomWebsiteClient(root); MoskomSessionVault.Load(saved);
            var encrypted = !Encoding.UTF8.GetString(sealedFile).Contains("smoke-session-opaque", StringComparison.Ordinal) && saved.Cookies.GetCookies(root)["internet_account"]?.Value == "smoke-session-opaque";
            revision = 1; await MoskomLauncherRuntime.SyncCabinetAsync(false);
            groups = (await AppManager.Instance.SubItems()).Where(g => g.Memo?.StartsWith("moskom-cabinet:", StringComparison.Ordinal) == true).ToList();
            var rotated = groups.Count == 2 && groups.Any(g => g.Url.EndsWith("/a2", StringComparison.Ordinal)) && groups.Any(g => g.Id == bId);
            revision = 2; await MoskomLauncherRuntime.SyncCabinetAsync(false);
            var revoked = !(await AppManager.Instance.SubItems()).Any(g => g.Memo?.StartsWith("moskom-cabinet:", StringComparison.Ordinal) == true) && vm.StatusBarViewModel.Servers.Count == initial;
            await MoskomLauncherRuntime.LogoutCabinetAsync();
            var logout = MoskomLauncherRuntime.CabinetUser == null && !File.Exists(Path.Combine(Utils.StartupPath(), "cabinet-session.dat")) && vm.StatusBarViewModel.Servers.Count == initial;
            MoskomLauncherRuntime.UseWebsiteFixture(root); await MoskomLauncherRuntime.RefreshServiceAsync();
            var check = await MoskomLauncherRuntime.CheckAvailabilityAsync();
            var availability = check.Milliseconds == null && MoskomLauncherRuntime.ServerMonitor?.Nodes.Single().LatencyMs == 258;
            return new(form, multiple, rotated, expired, revoked, encrypted, logout, availability);
        }
        finally { stop.Cancel(); listener.Stop(); try { await worker; } catch (OperationCanceledException) { } }
    }
}
