namespace ServiceLib.Services;

public sealed record MoskomAvailabilityResult(string Server, int? Milliseconds, DateTimeOffset CheckedAt);

public static partial class MoskomLauncherRuntime
{
    private const string CabinetPrefix = "moskom-cabinet:";
    public static MoskomWebsiteClient Website { get; private set; } = new();
    public static WebsiteUser? CabinetUser { get; private set; }
    public static string CabinetMessage { get; private set; } = "Подписки из кабинета можно получать автоматически.";
    public static WebsiteNotice? ServiceNotice { get; private set; }
    public static WebsiteMonitor? ServerMonitor { get; private set; }
    public static string ServiceError { get; private set; } = "";
    public static DateTimeOffset? ServiceLoadedAt { get; private set; }
    public static event Action? WebsiteChanged;
    private static int _websiteStarted;
    private static bool _restorePending = true;
    private static readonly Dictionary<string, string?> CabinetRevisions = [];

    public static async Task StartWebsiteAsync()
    {
        if (SmokeTest || Interlocked.Exchange(ref _websiteStarted, 1) != 0) return;
        MoskomSessionVault.Load(Website);
        var service = RefreshServiceAsync();
        try { await RestoreCabinetAsync(); } catch { CabinetMessage = "Не удалось связаться с кабинетом. Вход будет проверен повторно."; }
        await service;
        _ = PollWebsiteAsync(Lifetime.Token);
    }
    private static async Task PollWebsiteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RefreshServiceAsync();
                try
                {
                    if (_restorePending) await RestoreCabinetAsync();
                    else if (CabinetUser != null) await SyncCabinetAsync(false);
                }
                catch (MoskomWebsiteException e) when (e.Status == HttpStatusCode.Unauthorized) { await ExpireCabinetAsync(); }
                catch { CabinetMessage = "Синхронизация временно недоступна. Повторим через минуту."; WebsiteChanged?.Invoke(); }
            }
        }
        catch (OperationCanceledException) { }
    }
    private static async Task RestoreCabinetAsync()
    {
        await Operations.WaitAsync(Lifetime.Token);
        try
        {
            var session = await Website.MeAsync(Lifetime.Token);
            CabinetUser = session.User; _restorePending = false;
            if (CabinetUser != null) { MoskomSessionVault.Save(Website); await SyncCabinetCoreAsync(false); }
            else { await ClearCabinetGroupsCoreAsync(); MoskomSessionVault.Delete(); CabinetMessage = "Войдите, чтобы получить свои подписки с сайта."; }
        }
        finally { Operations.Release(); WebsiteChanged?.Invoke(); }
    }
    public static async Task LoginCabinetAsync(string email, string password)
    {
        await Operations.WaitAsync(Lifetime.Token);
        try
        {
            var session = await Website.LoginAsync(email, password, Lifetime.Token);
            CabinetUser = session.User; _restorePending = false; MoskomSessionVault.Save(Website);
            CabinetMessage = "Вход выполнен. Получаем подписки…"; WebsiteChanged?.Invoke();
            try { await SyncCabinetCoreAsync(true); }
            catch { CabinetMessage = "Вход выполнен. Подписки пока не синхронизированы; нажмите «Обновить серверы»."; }
        }
        finally { Operations.Release(); WebsiteChanged?.Invoke(); }
    }
    public static async Task LogoutCabinetAsync()
    {
        await Operations.WaitAsync(Lifetime.Token);
        try
        {
            try { await Website.LogoutAsync(Lifetime.Token); } catch { /* Local sign-out also works offline. */ }
            await ClearCabinetGroupsCoreAsync();
            CabinetUser = null; CabinetRevisions.Clear(); MoskomSessionVault.Delete();
            Website.Dispose(); Website = new(); _restorePending = false;
            CabinetMessage = "Вы вышли из кабинета. Добавленные через «+» ссылки сохранены.";
        }
        finally { Operations.Release(); WebsiteChanged?.Invoke(); }
    }
    private static async Task ExpireCabinetAsync()
    {
        await LogoutCabinetAsync(); CabinetMessage = "Сессия кабинета завершена. Войдите снова."; WebsiteChanged?.Invoke();
    }
    public static async Task SyncCabinetAsync(bool force)
    {
        if (CabinetUser == null) return;
        if (!await Operations.WaitAsync(0)) { if (force) throw new InvalidOperationException("Дождитесь завершения текущего действия."); return; }
        try
        {
            // The website validates password reset, account removal and remember-token expiry here.
            var session = await Website.MeAsync(Lifetime.Token);
            if (session.User == null) throw new MoskomWebsiteException("Войдите в кабинет снова.", HttpStatusCode.Unauthorized);
            CabinetUser = session.User; MoskomSessionVault.Save(Website);
            await SyncCabinetCoreAsync(force);
        }
        finally { Operations.Release(); WebsiteChanged?.Invoke(); }
    }
    private static async Task SyncCabinetCoreAsync(bool force)
    {
        var user = CabinetUser ?? throw new InvalidOperationException("Сначала войдите в кабинет.");
        // Only a complete, authenticated response is authoritative; HTTP/parse errors never clear data.
        var snapshot = await Website.SubscriptionsAsync(Lifetime.Token);
        var active = snapshot.Where(s => s.Usable(DateTimeOffset.UtcNow)).ToList();
        var all = await AppManager.Instance.SubItems() ?? [];
        string Tag(WebsiteSubscription s) => CabinetPrefix + user.Id + ":" + s.Id;
        var tags = active.Select(Tag).ToHashSet();
        var remove = all.Where(s => s.Memo?.StartsWith(CabinetPrefix, StringComparison.Ordinal) == true && !tags.Contains(s.Memo)).ToList();
        var updates = active.Where(s => force || !all.Any(g => g.Memo == Tag(s) && g.Url == s.Url && g.Remarks == "Кабинет · " + s.Name) || !CabinetRevisions.TryGetValue(Tag(s), out var revision) || revision != s.ContentUpdatedAt).ToList();
        var failed = 0;
        if (remove.Count != 0 || updates.Count != 0)
        {
            var config = AppManager.Instance.Config; var vm = ViewModel!;
            var selected = await ConfigHandler.GetDefaultServer(config);
            var activeRemoved = selected != null && remove.Any(g => g.Id == selected.Subid);
            var wasOn = Connection?.IsConnected == true;
            if (wasOn) await Connection!.DisconnectAsync();
            try
            {
                foreach (var group in remove) await ConfigHandler.DeleteSubItem(config, group.Id);
                foreach (var sub in updates)
                {
                    var group = all.FirstOrDefault(g => g.Memo == Tag(sub)) ?? new SubItem { Id = "", Memo = Tag(sub) };
                    group.Remarks = "Кабинет · " + sub.Name; group.Url = sub.Url!; group.Enabled = true;
                    await ConfigHandler.AddSubItem(config, group);
                    var success = false;
                    await SubscriptionHandler.UpdateProcess(config, group.Id, false, (ok, _) => { success |= ok; return Task.CompletedTask; });
                    if (success && (await AppManager.Instance.ProfileItems(group.Id))?.Count > 0) CabinetRevisions[Tag(sub)] = sub.ContentUpdatedAt;
                    else failed++;
                }
                await vm.RefreshForLauncherAsync(); await EnsureServerAsync(); await ConfigHandler.SaveConfig(config); ListsChanged?.Invoke();
            }
            finally
            {
                if (wasOn && !activeRemoved && await ConfigHandler.GetDefaultServer(config) != null) await Connection!.ConnectAsync();
            }
        }
        var time = DateTimeOffset.Now.ToString("HH:mm");
        CabinetMessage = failed > 0 ? $"Подписок в кабинете: {active.Count}. Не все серверы удалось обновить; повторим через минуту."
            : $"Подписок: {active.Count} · Синхронизировано в {time}";
        if (snapshot.Any(s => !s.Usable(DateTimeOffset.UtcNow))) CabinetMessage += " · Есть истёкшие или недоступные подписки.";
    }
    private static async Task ClearCabinetGroupsAsync()
    {
        await Operations.WaitAsync(Lifetime.Token);
        try { await ClearCabinetGroupsCoreAsync(); } finally { Operations.Release(); }
    }
    private static async Task ClearCabinetGroupsCoreAsync()
    {
        var groups = (await AppManager.Instance.SubItems() ?? []).Where(s => s.Memo?.StartsWith(CabinetPrefix, StringComparison.Ordinal) == true).ToList();
        if (groups.Count == 0) return;
        if (Connection?.IsConnected == true) await Connection.DisconnectAsync();
        foreach (var group in groups) await ConfigHandler.DeleteSubItem(AppManager.Instance.Config, group.Id);
        if (ViewModel != null) { await ViewModel.RefreshForLauncherAsync(); await EnsureServerAsync(); }
        await ConfigHandler.SaveConfig(AppManager.Instance.Config); ListsChanged?.Invoke();
    }
    public static async Task RefreshServiceAsync()
    {
        try
        {
            var notice = Website.NoticeAsync(Lifetime.Token); var monitor = Website.MonitorAsync(Lifetime.Token);
            await Task.WhenAll(notice, monitor);
            ServiceNotice = await notice; ServerMonitor = await monitor; ServiceLoadedAt = DateTimeOffset.UtcNow; ServiceError = "";
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { return; }
        catch { ServiceError = "Не удалось обновить статус с сайта. Повторите проверку позже."; }
        WebsiteChanged?.Invoke();
    }
    public static async Task<MoskomAvailabilityResult> CheckAvailabilityAsync()
    {
        if (!await Operations.WaitAsync(0)) throw new InvalidOperationException("Дождитесь завершения текущего действия.");
        try
        {
            var selected = await ConfigHandler.GetDefaultServer(AppManager.Instance.Config) ?? throw new InvalidOperationException("Сначала добавьте подписку и выберите сервер.");
            if (SmokeTest && !(IPAddress.TryParse(selected.Address, out var address) && IPAddress.IsLoopback(address))) throw new InvalidOperationException("Проверка CI разрешена только для локального тестового сервера.");
            int? delay = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(35));
            var test = new SpeedtestService(AppManager.Instance.Config, result =>
            {
                if (result.IndexId == selected.IndexId && int.TryParse(result.Delay, out var ms)) delay = ms > 0 ? ms : null;
                return Task.CompletedTask;
            });
            await test.RunLoop(ESpeedActionType.Realping, [selected], timeout.Token);
            return new(selected.Remarks, delay, DateTimeOffset.Now);
        }
        finally { Operations.Release(); }
    }
    // Used only by the disposable Windows smoke fixture; never accepts a production URL.
    public static void UseWebsiteFixture(Uri root)
    {
        if (!SmokeTest || !root.IsLoopback) throw new InvalidOperationException("Недопустимый тестовый адрес.");
        Website.Dispose(); Website = new(root);
    }
}
