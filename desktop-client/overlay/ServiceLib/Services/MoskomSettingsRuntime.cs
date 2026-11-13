namespace ServiceLib.Services;

public sealed class MoskomPreferences
{
    public string? SelectedSubscriptionId { get; set; }
    public string Dns { get; set; } = "auto";
    public string CustomDns { get; set; } = "";
    public string AppliedBundle { get; set; } = "";
    public DateTimeOffset? RulesUpdatedAt { get; set; }
    public string? DeferredVersion { get; set; }
    public DateTimeOffset? DeferredUntil { get; set; }
}

public static partial class MoskomLauncherRuntime
{
    public static MoskomPreferences Preferences { get; private set; } = new();
    public static event Action<MoskomAppRelease>? ApplicationUpdateFound;
    public static event Action? SettingsChanged;
    private static readonly SemaphoreSlim UpdateCheckGate = new(1, 1);
    private static string PreferencesFile => Path.Combine(Utils.StartupPath(), "launcher-settings.json");
    private static string RoutingRoot => Path.Combine(Utils.StartupPath(), "routing");
    private static string RoutingPointer => Path.Combine(RoutingRoot, "active.json");
    private static string BundledRules => Path.Combine(Utils.GetBaseDirectory(), "preset-russia");
    private static HttpClient? FixtureDownloads;

    public static string? CachedRulesDirectory
    {
        get
        {
            try
            {
                if (!File.Exists(RoutingPointer)) return null;
                var name = JsonSerializer.Deserialize<string>(File.ReadAllText(RoutingPointer));
                if (name == null || !Regex.IsMatch(name, @"\Abundle-[a-f0-9]{32}\z")) return null;
                var folder = Path.Combine(RoutingRoot, name);
                if (!File.Exists(Path.Combine(folder, "geosite.dat")) || !File.Exists(Path.Combine(folder, "geoip.dat")) || ReadManifest(folder) == null) return null;
                var cached = ReadManifest(folder)!; var bundled = ReadManifest(BundledRules);
                return bundled != null && string.CompareOrdinal(cached.GeoRelease, bundled.GeoRelease) < 0 ? null : folder;
            }
            catch { return null; }
        }
    }
    public static string RoutingAssetDirectory => CachedRulesDirectory ?? Utils.GetBinPath("");
    public static string RoutingSrsDirectory => Path.Combine(CachedRulesDirectory ?? Utils.GetBinPath(""), "srss");
    private static MoskomRouteManifest? ReadManifest(string directory)
    {
        var file = Path.Combine(directory, "manifest.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<MoskomRouteManifest>(File.ReadAllText(file), MoskomRouteDownloads.Json) : null;
    }
    private static void SavePreferences()
    {
        Directory.CreateDirectory(Utils.StartupPath());
        File.WriteAllText(PreferencesFile + ".tmp", JsonSerializer.Serialize(Preferences)); File.Move(PreferencesFile + ".tmp", PreferencesFile, true);
    }
    public static async Task InitializeLauncherSettingsAsync(Config config)
    {
        try { if (File.Exists(PreferencesFile)) Preferences = JsonSerializer.Deserialize<MoskomPreferences>(File.ReadAllText(PreferencesFile)) ?? new(); } catch { Preferences = new(); }
        try { _ = MoskomDnsChoices.RemoteAddress(Preferences.Dns, Preferences.CustomDns); } catch { Preferences.Dns = "auto"; Preferences.CustomDns = ""; }
        var folder = CachedRulesDirectory ?? BundledRules;
        var manifest = ReadManifest(folder) ?? throw new InvalidOperationException("Отсутствует версия пресета Russia.");
        var id = manifest.RoutingCommit + ":" + manifest.RulesCommit + ":" + manifest.GeoRelease;
        if (Preferences.AppliedBundle != id) await ApplyRoutingBundleAsync(folder, manifest, false);
        else await ApplyDnsToConfigAsync(config, folder);
    }
    private static async Task ApplyDnsToConfigAsync(Config config, string folder)
    {
        var simple = JsonUtils.Deserialize<SimpleDNSItem>(await File.ReadAllTextAsync(Path.Combine(folder, "simple_dns.json"))) ?? throw new InvalidOperationException("Некорректный DNS пресета.");
        var remote = MoskomDnsChoices.RemoteAddress(Preferences.Dns, Preferences.CustomDns);
        if (remote != null) simple.RemoteDNS = remote;
        config.SimpleDNSItem = simple;
        foreach (var core in new[] { ECoreType.Xray, ECoreType.sing_box })
        {
            var dns = JsonUtils.Deserialize<DNSItem>(await File.ReadAllTextAsync(Path.Combine(folder, core == ECoreType.Xray ? "v2ray.json" : "sing_box.json"))) ?? throw new InvalidOperationException("Некорректный DNS пресета.");
            dns.Enabled = false; dns.CoreType = core; await ConfigHandler.SaveDNSItems(config, dns);
        }
        await ConfigHandler.SaveConfig(config);
    }
    public static async Task SaveDnsAsync(string choice, string? custom)
    {
        _ = MoskomDnsChoices.RemoteAddress(choice, custom);
        if (!await Operations.WaitAsync(0)) throw new InvalidOperationException("Дождитесь завершения текущего действия.");
        var oldChoice = Preferences.Dns; var oldCustom = Preferences.CustomDns; var wasOn = Connection?.IsConnected == true;
        try
        {
            if (wasOn) await Connection!.DisconnectAsync();
            Preferences.Dns = choice; Preferences.CustomDns = custom?.Trim() ?? "";
            try { await ApplyDnsToConfigAsync(AppManager.Instance.Config, CachedRulesDirectory ?? BundledRules); SavePreferences(); }
            catch { Preferences.Dns = oldChoice; Preferences.CustomDns = oldCustom; await ApplyDnsToConfigAsync(AppManager.Instance.Config, CachedRulesDirectory ?? BundledRules); throw; }
            if (wasOn) await Connection!.ConnectAsync(); SettingsChanged?.Invoke();
        }
        finally { Operations.Release(); }
    }
    private static HttpClient UpdateHttpClient()
    {
        if (FixtureDownloads != null) return FixtureDownloads;
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = Connection?.IsConnected == true, ConnectTimeout = TimeSpan.FromSeconds(15) };
        if (handler.UseProxy) handler.Proxy = new WebProxy($"socks5://127.0.0.1:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
    }
    public static async Task<bool> UpdateRoutingRulesAsync(CancellationToken ct, IProgress<MoskomTransferProgress>? progress)
    {
        var stage = Path.Combine(RoutingRoot, "staging-" + Guid.NewGuid().ToString("N"));
        var client = UpdateHttpClient();
        try
        {
            // Download and validate everything before disconnecting or changing active settings.
            var manifest = await new MoskomRouteDownloads(new(client)).DownloadAsync(stage, ct, progress, ReadManifest(CachedRulesDirectory ?? BundledRules));
            if (manifest == null) return false;
            ct.ThrowIfCancellationRequested();
            if (!await Operations.WaitAsync(0, ct)) throw new InvalidOperationException("Дождитесь завершения текущего действия.");
            var wasOn = Connection?.IsConnected == true;
            try
            {
                if (wasOn) await Connection!.DisconnectAsync();
                await ApplyRoutingBundleAsync(stage, manifest, true);
                if (ViewModel != null) await ViewModel.StatusBarViewModel.RefreshRoutingsMenu();
                ListsChanged?.Invoke(); SettingsChanged?.Invoke();
                if (wasOn) await Connection!.ConnectAsync();
            }
            finally { Operations.Release(); }
            return true;
        }
        finally { if (client != FixtureDownloads) client.Dispose(); if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public static async Task ApplyRoutingBundleAsync(string source, MoskomRouteManifest manifest, bool cache)
    {
        var config = AppManager.Instance.Config;
        var template = JsonUtils.Deserialize<RoutingTemplate>(await File.ReadAllTextAsync(Path.Combine(source, "template.json"))) ?? throw new InvalidOperationException("Некорректный пресет Russia.");
        if (template.RoutingItems.Length is < 1 or > 12) throw new InvalidOperationException("В пресете отсутствуют правила.");
        var texts = new List<string>();
        for (var i = 0; i < template.RoutingItems.Length; i++)
        {
            var text = await File.ReadAllTextAsync(Path.Combine(source, $"route-{i}.json"));
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0) throw new InvalidOperationException("Получены пустые правила.");
            texts.Add(text);
        }
        _ = JsonUtils.Deserialize<SimpleDNSItem>(await File.ReadAllTextAsync(Path.Combine(source, "simple_dns.json"))) ?? throw new InvalidOperationException("Некорректный DNS.");
        var old = await AppManager.Instance.RoutingItems(); var own = old.Where(r => r.Remarks.StartsWith("RUv", StringComparison.Ordinal)).ToList();
        var previous = await ConfigHandler.GetDefaultRouting(config); var oldIndex = config.RoutingBasicItem.RoutingIndexId;
        var oldPointer = File.Exists(RoutingPointer) ? await File.ReadAllTextAsync(RoutingPointer) : null;
        var oldSimple = config.SimpleDNSItem; var oldApplied = Preferences.AppliedBundle; var oldDate = Preferences.RulesUpdatedAt;
        var created = new List<RoutingItem>(); string? newFolder = null;
        try
        {
            for (var i = 0; i < template.RoutingItems.Length; i++)
            {
                var item = template.RoutingItems[i]; item.Id = ""; item.Remarks = template.Version + "-" + item.Remarks; item.Enabled = true; item.Sort = i + 1; item.Url = "";
                await ConfigHandler.AddBatchRoutingRules(item, texts[i]); created.Add(item);
                if (string.IsNullOrEmpty(item.Id) || item.RuleNum < 1) throw new InvalidOperationException("Не удалось сохранить правила.");
            }
            var policy = previous?.Remarks.Contains('-') == true ? previous.Remarks[(previous.Remarks.IndexOf('-') + 1)..] : null;
            await ConfigHandler.SetDefaultRouting(config, created.FirstOrDefault(r => r.Remarks == template.Version + "-" + policy) ?? created[0]);
            await ApplyDnsToConfigAsync(config, source);
            if (cache)
            {
                Directory.CreateDirectory(RoutingRoot); var name = "bundle-" + Guid.NewGuid().ToString("N"); newFolder = Path.Combine(RoutingRoot, name);
                Directory.Move(source, newFolder);
                await File.WriteAllTextAsync(RoutingPointer + ".tmp", JsonSerializer.Serialize(name)); File.Move(RoutingPointer + ".tmp", RoutingPointer, true);
            }
            Preferences.AppliedBundle = manifest.RoutingCommit + ":" + manifest.RulesCommit + ":" + manifest.GeoRelease; Preferences.RulesUpdatedAt = manifest.FetchedAt; SavePreferences();
            await ConfigHandler.SaveConfig(config);
        }
        catch
        {
            config.SimpleDNSItem = oldSimple; config.RoutingBasicItem.RoutingIndexId = oldIndex; Preferences.AppliedBundle = oldApplied; Preferences.RulesUpdatedAt = oldDate;
            foreach (var item in created) await SQLiteHelper.Instance.DeleteAsync(item);
            if (previous != null) await ConfigHandler.SetDefaultRouting(config, previous);
            if (oldPointer != null) await File.WriteAllTextAsync(RoutingPointer, oldPointer); else if (File.Exists(RoutingPointer)) File.Delete(RoutingPointer);
            if (newFolder != null && Directory.Exists(newFolder)) Directory.Delete(newFolder, true);
            await ConfigHandler.SaveConfig(config); throw;
        }
        foreach (var item in own) await SQLiteHelper.Instance.DeleteAsync(item);
    }
    public static async Task<MoskomAppRelease?> CheckApplicationUpdateAsync(bool manual)
    {
        if (manual) await UpdateCheckGate.WaitAsync(Lifetime.Token);
        else if (!await UpdateCheckGate.WaitAsync(0)) return null;
        var client = UpdateHttpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var release = await new MoskomAppUpdates(new(client)).CheckAsync(Version.Parse(MoskomBuildInfo.Version), timeout.Token);
            if (release != null && (manual || Preferences.DeferredVersion != release.Version.ToString() || Preferences.DeferredUntil is not { } until || until <= DateTimeOffset.UtcNow)) return release;
            return null;
        }
        finally { if (client != FixtureDownloads) client.Dispose(); UpdateCheckGate.Release(); }
    }
    public static void DeferApplicationUpdate(MoskomAppRelease release)
    {
        Preferences.DeferredVersion = release.Version.ToString(); Preferences.DeferredUntil = DateTimeOffset.UtcNow.AddHours(24); try { SavePreferences(); } catch { }
    }
    public static async Task<string> DownloadApplicationUpdateAsync(MoskomAppRelease release, CancellationToken ct, IProgress<MoskomTransferProgress>? progress)
    {
        var client = UpdateHttpClient();
        try { return await new MoskomAppUpdates(new(client)).DownloadAsync(release, Path.Combine(Utils.GetTempPath(), "updates", release.Version.ToString()), ct, progress); }
        finally { if (client != FixtureDownloads) client.Dispose(); }
    }
    public static async Task InstallApplicationUpdateAsync(string installer)
    {
        if (SmokeTest) throw new InvalidOperationException("Установка обновлений в проверочном режиме отключена.");
        var args = $"/SILENT /SP- /NORESTART /MOSKOMUPDATE=1 /MOSKOMPID={Environment.ProcessId} /DIR=\"{Utils.GetBaseDirectory()}\"";
        _ = Process.Start(new ProcessStartInfo(installer, args) { UseShellExecute = true }) ?? throw new InvalidOperationException("Не удалось запустить установщик.");
        await AppManager.Instance.AppExitAsync(true);
    }
    public static async Task WatchApplicationUpdatesAsync()
    {
        if (SmokeTest) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), Lifetime.Token);
            while (!Lifetime.IsCancellationRequested)
            {
                try { var release = await CheckApplicationUpdateAsync(false); if (release != null) ApplicationUpdateFound?.Invoke(release); } catch { }
                await Task.Delay(TimeSpan.FromHours(6), Lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
    public static void UseSettingsDownloadFixture(HttpClient client)
    {
        if (!SmokeTest) throw new InvalidOperationException("Только для проверки."); FixtureDownloads = client;
    }
}
