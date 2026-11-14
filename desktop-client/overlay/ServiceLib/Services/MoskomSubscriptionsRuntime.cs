namespace ServiceLib.Services;

public sealed record MoskomSubscriptionGroup(string Id, string Name, IReadOnlyList<ComboItem> Servers)
{
    public string DisplayName => $"{Name} · {Servers.Count}";
}

public static partial class MoskomLauncherRuntime
{
    public static async Task<IReadOnlyList<MoskomSubscriptionGroup>> ManualSubscriptionsAsync()
    {
        var subscriptions = await AppManager.Instance.SubItems() ?? [];
        var manualIds = subscriptions.Where(s => s.Memo?.StartsWith(CabinetPrefix, StringComparison.Ordinal) != true).Select(s => s.Id).ToHashSet();
        return (await SubscriptionGroupsAsync()).Where(g => manualIds.Contains(g.Id)).ToList();
    }

    public static async Task DeleteManualSubscriptionAsync(string id)
    {
        if (!await Operations.WaitAsync(0)) throw new InvalidOperationException("Дождитесь завершения текущего действия.");
        try
        {
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Выберите добавленную вручную подписку.");
            var subscription = await AppManager.Instance.GetSubItem(id)
                ?? throw new InvalidOperationException("Подписка уже удалена. Обновите список.");
            if (subscription.Memo?.StartsWith(CabinetPrefix, StringComparison.Ordinal) == true)
                throw new InvalidOperationException("Подписки из личного кабинета управляются на сайте.");
            var config = AppManager.Instance.Config;
            var selected = await AppManager.Instance.GetProfileItem(config.IndexId);
            if (Connection?.IsConnected == true && selected?.Subid == id) await Connection.DisconnectAsync();
            await ConfigHandler.DeleteSubItem(config, id);
            await EnsureServerAsync();
            if (ViewModel != null) await ViewModel.RefreshForLauncherAsync();
            await ConfigHandler.SaveConfig(config); ListsChanged?.Invoke();
        }
        finally { Operations.Release(); }
    }

    public static async Task<IReadOnlyList<MoskomSubscriptionGroup>> SubscriptionGroupsAsync()
    {
        var subscriptions = await AppManager.Instance.SubItems() ?? [];
        var profiles = await AppManager.Instance.ProfileModels("", "") ?? [];
        var grouped = profiles.GroupBy(p => p.Subid ?? "").ToDictionary(g => g.Key, g => (IReadOnlyList<ComboItem>)g.Select(p => new ComboItem { ID = p.IndexId, Text = p.GetSummary() }).ToList());
        var result = new List<MoskomSubscriptionGroup>();
        foreach (var subscription in subscriptions.Where(s => !string.IsNullOrEmpty(s.Id)).OrderBy(s => s.Memo?.StartsWith(CabinetPrefix, StringComparison.Ordinal) == true ? 0 : 1))
        {
            var name = subscription.Remarks ?? "";
            if (name.StartsWith("Кабинет · ", StringComparison.Ordinal)) name = name[10..];
            if (string.IsNullOrWhiteSpace(name)) name = "Добавленная подписка";
            result.Add(new(subscription.Id, name, grouped.GetValueOrDefault(subscription.Id) ?? []));
        }
        foreach (var group in grouped.Where(g => result.All(s => s.Id != g.Key)))
            result.Add(new(group.Key, string.IsNullOrEmpty(group.Key) ? "Отдельные ключи" : "Добавлено вручную", group.Value));
        return result;
    }

    public static MoskomSubscriptionGroup? SelectedSubscription(IReadOnlyList<MoskomSubscriptionGroup> groups)
        => groups.FirstOrDefault(g => g.Id == Preferences.SelectedSubscriptionId)
            ?? groups.FirstOrDefault(g => g.Servers.Any(s => s.ID == AppManager.Instance.Config.IndexId))
            ?? groups.FirstOrDefault();

    private static void RememberSubscription(string? id)
    {
        if (Preferences.SelectedSubscriptionId == id) return;
        Preferences.SelectedSubscriptionId = id; SavePreferences();
    }

    private static async Task ApplySubscriptionSelectionAsync(MoskomSubscriptionGroup? group)
    {
        var config = AppManager.Instance.Config;
        config.SubIndexId = group?.Id ?? "";
        if (group == null || !group.Servers.Any(s => s.ID == config.IndexId)) config.IndexId = group?.Servers.FirstOrDefault()?.ID ?? "";
        RememberSubscription(group?.Id);
        await ConfigHandler.SaveConfig(config);
    }

    private static async Task EnsureServerAsync()
    {
        var groups = await SubscriptionGroupsAsync();
        await ApplySubscriptionSelectionAsync(SelectedSubscription(groups));
    }

    public static async Task InitializeSubscriptionSelectionAsync()
    {
        await Operations.WaitAsync(Lifetime.Token);
        try { await EnsureServerAsync(); ListsChanged?.Invoke(); }
        finally { Operations.Release(); }
    }

    public static async Task SelectSubscriptionAsync(string id)
    {
        if (!await Operations.WaitAsync(0)) throw new InvalidOperationException("Дождитесь завершения текущего действия.");
        try
        {
            var group = (await SubscriptionGroupsAsync()).FirstOrDefault(g => g.Id == id)
                ?? throw new InvalidOperationException("Эта подписка больше недоступна. Обновите список подписок.");
            var config = AppManager.Instance.Config;
            var serverId = group.Servers.Any(s => s.ID == config.IndexId) ? config.IndexId : group.Servers.FirstOrDefault()?.ID ?? "";
            // UI list refreshes create new objects for the same subscription.
            // Re-selecting its existing server must leave the running core intact.
            if (Preferences.SelectedSubscriptionId == id && config.IndexId == serverId)
            {
                config.SubIndexId = id; return;
            }
            var wasOn = Connection?.IsConnected == true;
            if (wasOn) await Connection!.DisconnectAsync();
            await ApplySubscriptionSelectionAsync(group); ListsChanged?.Invoke();
            if (wasOn && group.Servers.Count > 0) await Connection!.ConnectAsync();
        }
        finally { Operations.Release(); }
    }
}
