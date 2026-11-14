using ServiceLib.Services;
using Avalonia.VisualTree;

namespace v2rayN.Desktop.Views;

internal static class MoskomSubscriptionSmoke
{
    internal sealed record Report(bool Menu, bool CabinetHidden, bool CabinetProtected, bool CancelPreserved, bool InactiveKeptConnection, bool ActiveStoppedConnection, bool ProfilesRemoved, bool EmptyState, bool Persisted)
    {
        public bool Passed => Menu && CabinetHidden && CabinetProtected && CancelPreserved && InactiveKeptConnection && ActiveStoppedConnection && ProfilesRemoved && EmptyState && Persisted;
    }
    private static async Task WaitFor(Func<bool> ready)
    {
        for (var i = 0; i < 160; i++) { if (ready()) return; await Task.Delay(50); }
        throw new InvalidOperationException("Subscription management did not reach its expected state.");
    }
    private static Window Child(Window owner, string title) => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.First(w => w != owner && w.Title!.StartsWith(title, StringComparison.Ordinal));
    private static T Find<T>(Window owner, string name) where T : Control => owner.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    public static async Task<Report> RunAsync(Window owner, MainWindowViewModel vm)
    {
        var config = AppManager.Instance.Config;
        async Task<SubItem> Add(string name, bool cabinet = false)
        {
            var item = new SubItem { Id = "", Remarks = name, Url = "https://manual.example.test/" + Guid.NewGuid().ToString("N"), Memo = cabinet ? "moskom-cabinet:41:999" : "" };
            await ConfigHandler.AddSubItem(config, item);
            if (!cabinet) await ConfigHandler.AddBatchServers(config, "vless://33333333-3333-4333-8333-333333333333@127.0.0.1:6541?encryption=none&security=none&type=tcp#Managed", item.Id, true);
            return item;
        }
        var a = await Add("Ручная подписка A"); var b = await Add("Ручная подписка B"); var cabinet = await Add("Кабинет · Защищённая", true);
        var deletedProfiles = (await AppManager.Instance.ProfileItems(a.Id)).Concat(await AppManager.Instance.ProfileItems(b.Id)).Select(p => p.IndexId).ToList();
        await vm.RefreshForLauncherAsync(); await MoskomLauncherRuntime.SelectSubscriptionAsync(a.Id);
        var core = new MoskomConnectionSmokeEngine(); MoskomLauncherRuntime.UseConnectionFixture(core);
        var connection = MoskomLauncherRuntime.Connection!; await connection.ConnectAsync();
        var starts = core.Starts; var stops = core.Stops;
        var protectedCabinet = false;
        try { await MoskomLauncherRuntime.DeleteManualSubscriptionAsync(cabinet.Id); }
        catch (InvalidOperationException) { protectedCabinet = (await AppManager.Instance.GetSubItem(cabinet.Id)) != null && connection.IsConnected && core.Stops == stops; }
        var burger = Find<Button>(owner, "MoskomMenu"); var menu = burger.ContextMenu!;
        var entry = menu.Items.OfType<MenuItem>().First(i => i.Name == "MoskomManageSubscriptions");
        async Task<Window> Open()
        {
            menu.Open(burger); entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Close();
            await WaitFor(() => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.Any(w => w.Title!.StartsWith("Управление подписками", StringComparison.Ordinal)));
            return Child(owner, "Управление подписками");
        }
        var window = await Open();
        Button Remove(string id) => window.GetVisualDescendants().OfType<Button>().First(c => c.Name == "MoskomDeleteSubscription" && c.Tag as string == id);
        var hidden = window.GetVisualDescendants().OfType<Button>().Where(c => c.Name == "MoskomDeleteSubscription").All(c => c.Tag as string != cabinet.Id);
        Click(Remove(b.Id)); await Task.Delay(100); var confirm = Child(window, "Удалить подписку?");
        Click(Find<Button>(confirm, "MoskomCancelSubscriptionDelete")); await Task.Delay(100);
        var cancelled = (await AppManager.Instance.GetSubItem(b.Id)) != null && core.Stops == stops;
        Click(Remove(b.Id)); await Task.Delay(100); confirm = Child(window, "Удалить подписку?");
        Click(Find<Button>(confirm, "MoskomConfirmSubscriptionDelete"));
        await WaitFor(() => Find<StackPanel>(window, "MoskomManualSubscriptions").IsEnabled && !window.GetVisualDescendants().OfType<Button>().Any(c => c.Name == "MoskomDeleteSubscription" && c.Tag as string == b.Id));
        var inactive = connection.IsConnected && core.Starts == starts && core.Stops == stops && (await AppManager.Instance.GetSubItem(b.Id)) == null;
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height))) { bitmap.Render(window); bitmap.Save(Path.Combine(Environment.GetEnvironmentVariable("MOSKOM_SMOKE_OUTPUT")!, "subscription-management.png")); }
        Click(Remove(a.Id)); await Task.Delay(100); confirm = Child(window, "Удалить подписку?");
        Click(Find<Button>(confirm, "MoskomConfirmSubscriptionDelete"));
        await WaitFor(() => Find<StackPanel>(window, "MoskomManualSubscriptions").IsEnabled && !window.GetVisualDescendants().OfType<Button>().Any(c => c.Name == "MoskomDeleteSubscription" && c.Tag as string == a.Id));
        var activeStopped = !connection.IsConnected && core.Stops == stops + 1;
        var profilesRemoved = true;
        foreach (var id in deletedProfiles) profilesRemoved &= await AppManager.Instance.GetProfileItem(id) == null;
        await connection.DisconnectAsync(); MoskomLauncherRuntime.UseConnectionFixture(null);
        foreach (var group in await MoskomLauncherRuntime.ManualSubscriptionsAsync()) await MoskomLauncherRuntime.DeleteManualSubscriptionAsync(group.Id);
        Click(Find<Button>(window, "MoskomSubscriptionsClose")); await Task.Delay(100);
        window = await Open();
        var empty = Find<TextBlock>(window, "MoskomManualSubscriptionsEmpty").IsVisible;
        var persisted = (await MoskomLauncherRuntime.ManualSubscriptionsAsync()).Count == 0 && (await AppManager.Instance.ProfileItems("")).Count == 1;
        Click(Find<Button>(window, "MoskomSubscriptionsClose"));
        await ConfigHandler.DeleteSubItem(config, cabinet.Id); await MoskomLauncherRuntime.InitializeSubscriptionSelectionAsync();
        return new(entry.Header as string == "Управление подписками", hidden, protectedCabinet, cancelled, inactive, activeStopped, profilesRemoved, empty, persisted);
    }
}
