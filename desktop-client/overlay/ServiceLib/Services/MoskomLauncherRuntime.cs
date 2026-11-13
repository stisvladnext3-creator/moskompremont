using System.Net;
using ServiceLib.ViewModels;
using System.Net.Sockets;
using System.Globalization;
using ServiceLib.Handler.SysProxy;

namespace ServiceLib.Services;

public static partial class MoskomLauncherRuntime
{
    public const string DisplayName = "МосКомпРемонт VPN";
    public static bool AllowCoreReload { get; set; }
    public static bool SmokeTest => Environment.GetEnvironmentVariable("MOSKOM_SMOKE_TEST") == "1";
    public static MainWindowViewModel? ViewModel { get; private set; }
    public static MoskomConnection? Connection { get; private set; }
    public static event Action? ListsChanged;
    private static readonly SemaphoreSlim Operations = new(1, 1);
    private static readonly CancellationTokenSource Lifetime = new();
    private static string PresetPath(string file) => Path.Combine(Utils.GetBaseDirectory(), "preset-russia", file);

    public static async Task PrepareAsync(Config config)
    {
        // A separate application data directory is selected by Utils.StartupPath in this build.
        // Never inherit or overwrite a user's unrelated v2rayN installation.
        if (OperatingSystem.IsWindows() && !SmokeTest) MoskomProxyLease.RecoverAfterCrash();
        config.SystemProxyItem.SysProxyType = ESysProxyType.Unchanged;
        config.TunModeItem.EnableTun = false;
        config.GuiItem.AutoUpdateInterval = 0;
        config.UiItem.CurrentLanguage = "ru";
        config.UiItem.CurrentTheme = "Dark";
        config.UiItem.AutoHideStartup = false;
        CultureInfo.CurrentUICulture = new("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = new("ru-RU");
        var stamp = Utils.GetConfigPath("moskom-preset-v1.json");
        if (!File.Exists(stamp))
        {
            var template = JsonUtils.Deserialize<RoutingTemplate>(await File.ReadAllTextAsync(PresetPath("template.json")))
                ?? throw new InvalidDataException("Не найден комплект Russia.");
            config.ConstItem.GeoSourceUrl = Global.GeoFilesSources[1];
            config.ConstItem.SrsSourceUrl = Global.SingboxRulesetSources[1];
            config.ConstItem.RouteRulesTemplateSourceUrl = Global.RoutingRulesSources[1];
            foreach (var core in new[] { ECoreType.Xray, ECoreType.sing_box })
            {
                var dns = JsonUtils.Deserialize<DNSItem>(await File.ReadAllTextAsync(PresetPath(core == ECoreType.Xray ? "v2ray.json" : "sing_box.json")))
                    ?? throw new InvalidDataException("Повреждены DNS-настройки Russia.");
                dns.Enabled = false; dns.CoreType = core;
                await ConfigHandler.SaveDNSItems(config, dns);
            }
            config.SimpleDNSItem = JsonUtils.Deserialize<SimpleDNSItem>(await File.ReadAllTextAsync(PresetPath("simple_dns.json")))
                ?? throw new InvalidDataException("Повреждены DNS-настройки Russia.");
            var existing = await AppManager.Instance.RoutingItems();
            foreach (var item in existing.Where(r => r.Remarks.StartsWith(template.Version + "-", StringComparison.Ordinal)))
                await SQLiteHelper.Instance.DeleteAsync(item);
            RoutingItem? first = null;
            for (var i = 0; i < template.RoutingItems.Length; i++)
            {
                var item = template.RoutingItems[i];
                item.Remarks = template.Version + "-" + item.Remarks; item.Sort = i + 1; item.Enabled = true; item.Url = "";
                await ConfigHandler.AddBatchRoutingRules(item, await File.ReadAllTextAsync(PresetPath($"route-{i}.json")));
                first ??= item;
            }
            if (first != null) await ConfigHandler.SetDefaultRouting(config, first);
            await ConfigHandler.SaveConfig(config);
            await File.WriteAllTextAsync(stamp, "{\"preset\":\"Russia\",\"source\":\"runetfreedom\",\"schema\":1}");
        }
        await ConfigHandler.SaveConfig(config);
    }
    public static void Attach(MainWindowViewModel vm)
    {
        ViewModel = vm;
        Connection = new MoskomConnection(new Engine(vm), SmokeTest ? new NoProxy() : new WindowsLease());
        _ = MonitorCoreAsync(Lifetime.Token);
    }
    private sealed class WindowsLease : IMoskomProxyLease
    {
        public Task AcquireAsync(CancellationToken ct)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Эта сборка подключает системный прокси Windows.");
            return new MoskomProxyLease().AcquireAsync(ct);
        }
        public Task RestoreAsync() { if (OperatingSystem.IsWindows()) return new MoskomProxyLease().RestoreAsync(); return Task.CompletedTask; }
    }
    private sealed class NoProxy : IMoskomProxyLease
    {
        public Task AcquireAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RestoreAsync() => Task.CompletedTask;
    }
    private sealed class Engine(MainWindowViewModel vm) : IMoskomEngine
    {
        public async Task StartAndVerifyAsync(CancellationToken token)
        {
            var config = AppManager.Instance.Config;
            var selected = await ConfigHandler.GetDefaultServer(config);
            if (selected == null) throw new InvalidOperationException("Сначала добавьте ссылку кнопкой «+» и выберите сервер.");
            if (SmokeTest) throw new InvalidOperationException("В проверочном режиме подключение отключено.");
            AllowCoreReload = true;
            await vm.Reload();
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            var ready = false;
            for (var i = 0; i < 25; i++)
            {
                token.ThrowIfCancellationRequested();
                if (await HasSocksAsync(port, token)) { ready = true; break; }
                await Task.Delay(200, token);
            }
            if (!ready) throw new InvalidOperationException("Ядро не запустилось. Попробуйте другой сервер.");
            // A listening local port alone does not mean that the VPN server works.
            using var handler = new SocketsHttpHandler { Proxy = new WebProxy($"socks5://127.0.0.1:{port}"), UseProxy = true, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(4) };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            foreach (var url in new[] { "https://www.gstatic.com/generate_204", "https://www.apple.com/library/test/success.html", "https://www.msftconnecttest.com/connecttest.txt" })
            {
                try { using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token); if (response.IsSuccessStatusCode) return; }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                catch (HttpRequestException) { }
            }
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Сервер не прошёл проверку. Выберите другой сервер и повторите подключение.");
        }
        public async Task StopAsync() { AllowCoreReload = false; await vm.StopForLauncherAsync(); }
    }
    private static async Task<bool> HasSocksAsync(int port, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(700);
            using var tcp = new TcpClient(); await tcp.ConnectAsync("127.0.0.1", port, timeout.Token);
            var stream = tcp.GetStream(); await stream.WriteAsync(new byte[] { 5, 1, 0 }, timeout.Token);
            var reply = new byte[2]; await stream.ReadExactlyAsync(reply, timeout.Token); return reply[0] == 5 && reply[1] == 0;
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException) { return false; }
    }
    private static async Task MonitorCoreAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (Connection?.IsConnected != true) continue;
                if (!await HasSocksAsync(AppManager.Instance.GetLocalPort(EInboundProtocol.socks), token)) await Connection.DisconnectAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch { if (Connection != null) await Connection.DisconnectAsync(); }
    }
    public static async Task ImportAsync(string text)
    {
        if (!await Operations.WaitAsync(0)) return;
        try
        {
            if (text.IsNullOrEmpty() || text.Length > 1024 * 1024) throw new InvalidOperationException("Скопируйте ссылку-подписку или ключ подключения.");
            var vm = ViewModel ?? throw new InvalidOperationException("Приложение ещё запускается.");
            var config = AppManager.Instance.Config;
            var wasOn = Connection?.IsConnected == true;
            if (wasOn) await Connection!.DisconnectAsync();
            var before = (await AppManager.Instance.SubItems() ?? []).Select(s => s.Id).ToHashSet();
            var count = await ConfigHandler.AddBatchServers(config, text.Trim(), "", false);
            if (count <= 0) throw new InvalidOperationException("В буфере нет поддерживаемой ссылки.");
            var url = text.Trim();
            var subs = await AppManager.Instance.SubItems() ?? [];
            var refresh = subs.Where(s => !before.Contains(s.Id) && !s.Url.IsNullOrEmpty()).ToList();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var sub = subs.FirstOrDefault(s => s.Url == url);
                if (sub == null) throw new InvalidOperationException("Не удалось добавить подписку.");
                if (!refresh.Any(s => s.Id == sub.Id)) refresh.Add(sub);
            }
            foreach (var sub in refresh)
            {
                await vm.UpdateSubscriptionProcess(sub.Id, false);
                var profiles = await AppManager.Instance.ProfileItems(sub.Id) ?? [];
                if (profiles.Count == 0) throw new InvalidOperationException("Подписка добавлена, но список серверов не получен. Проверьте ссылку и нажмите «Обновить».");
                config.SubIndexId = sub.Id;
                await ConfigHandler.SetDefaultServerIndex(config, profiles.First().IndexId);
            }
            await vm.RefreshForLauncherAsync();
            await EnsureServerAsync(); ListsChanged?.Invoke();
            if (wasOn) await Connection!.ConnectAsync();
        }
        finally { Operations.Release(); }
    }
    public static async Task UpdateAsync()
    {
        if (CabinetUser != null) await SyncCabinetAsync(true);
        var vm = ViewModel ?? throw new InvalidOperationException("Приложение ещё запускается.");
        if (!await Operations.WaitAsync(0)) throw new InvalidOperationException("Дождитесь завершения обновления.");
        try
        {
            var manual = (await AppManager.Instance.SubItems() ?? []).Where(g => !string.IsNullOrEmpty(g.Url) && g.Memo?.StartsWith(CabinetPrefix, StringComparison.Ordinal) != true).ToList();
            if (manual.Count == 0) return;
            var wasOn = Connection?.IsConnected == true;
            if (wasOn) await Connection!.DisconnectAsync();
            foreach (var group in manual) await vm.UpdateSubscriptionProcess(group.Id, false);
            await vm.RefreshForLauncherAsync(); await EnsureServerAsync(); ListsChanged?.Invoke();
            if (wasOn) await Connection!.ConnectAsync();
        }
        finally { Operations.Release(); }
    }
    private static async Task EnsureServerAsync()
    {
        var vm = ViewModel!; var config = AppManager.Instance.Config;
        if ((await ConfigHandler.GetDefaultServer(config)) == null)
        {
            var first = vm.StatusBarViewModel.Servers.FirstOrDefault(s => !s.ID.IsNullOrEmpty());
            if (first != null) await ConfigHandler.SetDefaultServerIndex(config, first.ID);
        }
    }
    public static async Task SelectServerAsync(ComboItem item)
    {
        if (item.ID.IsNullOrEmpty()) return;
        if (!await Operations.WaitAsync(0)) throw new InvalidOperationException("Дождитесь завершения обновления.");
        try
        {
            var wasOn = Connection?.IsConnected == true;
            if (wasOn) await Connection!.DisconnectAsync();
            await ConfigHandler.SetDefaultServerIndex(AppManager.Instance.Config, item.ID);
            if (wasOn) await Connection!.ConnectAsync();
        }
        finally { Operations.Release(); }
    }
    public static async Task SelectRoutingAsync(RoutingItem item)
    {
        if (!await Operations.WaitAsync(0)) throw new InvalidOperationException("Дождитесь завершения обновления.");
        try
        {
            var wasOn = Connection?.IsConnected == true;
            if (wasOn) await Connection!.DisconnectAsync();
            await ConfigHandler.SetDefaultRouting(AppManager.Instance.Config, item);
            if (wasOn) await Connection!.ConnectAsync();
        }
        finally { Operations.Release(); }
    }
    public static async Task ToggleConnectionAsync()
    {
        if (!await Operations.WaitAsync(0)) throw new InvalidOperationException("Дождитесь завершения обновления.");
        try { if (Connection!.IsConnected) await Connection.DisconnectAsync(); else await Connection.ConnectAsync(); }
        finally { Operations.Release(); }
    }
    public static async Task ExitAsync()
    {
        Lifetime.Cancel(); AllowCoreReload = false;
        if (Connection != null) await Connection.DisconnectAsync();
        var config = AppManager.Instance.Config;
        config.SystemProxyItem.SysProxyType = ESysProxyType.Unchanged; config.TunModeItem.EnableTun = false;
    }
}
