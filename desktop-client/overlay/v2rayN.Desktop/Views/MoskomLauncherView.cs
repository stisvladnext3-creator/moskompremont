using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Automation;
using System.Collections.Specialized;
using System.Diagnostics;
using ServiceLib.Services;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public sealed class MoskomLauncherView : UserControl
{
    private readonly MainWindowViewModel _vm;
    private readonly Window _owner;
    private readonly ComboBox _servers = new() { Name = "MoskomServers", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48 };
    private readonly ComboBox _routing = new() { Name = "MoskomRouting", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48 };
    private readonly Button _power = new() { Name = "MoskomPower", Width = 154, Height = 154, CornerRadius = new(77), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _plus = new() { Name = "MoskomAdd", Content = "+", Width = 46, Height = 48, FontSize = 26, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _update = new() { Name = "MoskomUpdate", Content = "Обновить серверы", MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _menu = new() { Name = "MoskomMenu", Width = 46, Height = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _availability = new() { Name = "MoskomAvailability", Content = "Проверить доступность", MinHeight = 40, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _websiteStatus = new() { Name = "MoskomWebsiteStatus", Content = "Статус сервиса", MinHeight = 40, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _account = new() { Name = "MoskomLogin", Content = "Войти в кабинет", MinHeight = 40, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _accountInfo = new() { Name = "MoskomAccountInfo", FontSize = 11, Foreground = Brush.Parse("#A7B7D0"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _availabilityResult = new() { Name = "MoskomAvailabilityResult", Text = "Проверка с этого компьютера ещё не выполнялась.", FontSize = 12, Foreground = Brush.Parse("#A7B7D0"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _status = new() { Name = "MoskomStatus", Text = "VPN выключен", FontSize = 22, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _hint = new() { Name = "MoskomHint", Text = "Скопируйте ссылку-подписку и нажмите «+».", FontSize = 13, Foreground = Brush.Parse("#A7B7D0"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MinHeight = 42 };
    private readonly TextBlock _powerLabel = new() { Text = "Включить VPN", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private bool _refreshing;
    private bool _operation;
    private string? _checkedServer;

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, Foreground = Brush.Parse("#A7B7D0"), Margin = new(0, 0, 0, 7) };
    public MoskomLauncherView(Window owner, MainWindowViewModel vm)
    {
        _owner = owner; _vm = vm;
        Background = MoskomBrand.Background; Foreground = Brush.Parse("#ECF3FF"); MoskomBrand.Style(Styles);
        var shell = new StackPanel { Margin = new(30, 24), Spacing = 10, MaxWidth = 450, HorizontalAlignment = HorizontalAlignment.Stretch };
        var header = new Grid { ColumnDefinitions = new("60,*,46"), ColumnSpacing = 10 };
        header.Children.Add(new Image { Name = "MoskomSiteLogo", Source = MoskomBrand.Logo(), Width = 58, Height = 64 });
        var title = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "МОСКОМПРЕМОНТ", FontSize = 10, Foreground = Brush.Parse("#55D8F1"), FontWeight = FontWeight.Bold, LetterSpacing = 1.4 });
        title.Children.Add(new TextBlock { Text = "Мировой интернет", FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(title, 1); header.Children.Add(title);
        _menu.Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 2,4 L 24,4 M 2,12 L 24,12 M 2,20 L 24,20"), Stroke = Brush.Parse("#90E6FA"), StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, Width = 26, Height = 24 };
        AutomationProperties.SetName(_menu, "Меню приложения"); Grid.SetColumn(_menu, 2); header.Children.Add(_menu); shell.Children.Add(header);
        var context = new ContextMenu { MinWidth = 300 };
        MenuItem Entry(string text, string name, Func<Task> action)
        {
            var item = new MenuItem { Header = text, Name = name }; item.Click += async (_, _) => await action(); context.Items.Add(item); return item;
        }
        Entry("Обновить правила Russia", "MoskomRulesUpdate", () => MoskomSettingsViews.ShowRoutingAsync(owner));
        Entry("Проверить обновления приложения", "MoskomAppUpdates", () => MoskomSettingsViews.CheckApplicationAsync(owner));
        Entry("DNS", "MoskomDns", () => MoskomSettingsViews.ShowDnsAsync(owner));
        context.Items.Add(new Separator());
        Entry("Справка", "MoskomMenuHelp", () => MoskomHelpView.ShowAsync(owner));
        Entry("Выход", "MoskomMenuExit", () => AppManager.Instance.AppExitAsync(true));
        _menu.ContextMenu = context; _menu.Click += (_, _) => context.Open(_menu);
        var siteRow = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8, Margin = new(0, 4, 0, 0) };
        siteRow.Children.Add(_websiteStatus); Grid.SetColumn(_account, 1); siteRow.Children.Add(_account); shell.Children.Add(siteRow); shell.Children.Add(_accountInfo);
        var powerContent = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        // A vector path renders on every Windows font set, including machines without U+23FB.
        powerContent.Children.Add(new Avalonia.Controls.Shapes.Path { Name = "MoskomPowerIcon", Data = Geometry.Parse("M 19,14 A 24,24 0 1 0 53,14 M 36,4 L 36,31"), Stroke = Brushes.White, StrokeThickness = 7, StrokeLineCap = PenLineCap.Round, Width = 72, Height = 64, HorizontalAlignment = HorizontalAlignment.Center });
        powerContent.Children.Add(_powerLabel); _power.Content = powerContent;
        AutomationProperties.SetName(_power, "Включить VPN");
        shell.Children.Add(new Border { Child = _power, Padding = new(0, 12, 0, 0) });
        shell.Children.Add(_status); shell.Children.Add(_hint);
        var serverPanel = new StackPanel(); serverPanel.Children.Add(Label("Сервер"));
        var serverRow = new Grid { ColumnDefinitions = new("*,46"), ColumnSpacing = 8 };
        serverRow.Children.Add(_servers); Grid.SetColumn(_plus, 1); serverRow.Children.Add(_plus); serverPanel.Children.Add(serverRow); shell.Children.Add(serverPanel);
        ToolTip.SetTip(_plus, "Добавить скопированную ссылку или ключ"); AutomationProperties.SetName(_plus, "Добавить ссылку из буфера обмена");
        var routingPanel = new StackPanel(); routingPanel.Children.Add(Label("Маршрутизация · Russia")); routingPanel.Children.Add(_routing); shell.Children.Add(routingPanel);
        var actions = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 }; _update.FontSize = 12;
        actions.Children.Add(_update); Grid.SetColumn(_availability, 1); actions.Children.Add(_availability); shell.Children.Add(actions); shell.Children.Add(_availabilityResult);
        shell.Children.Add(new TextBlock { Text = "Режим: системный прокси Windows", FontSize = 12, Foreground = Brush.Parse("#8D9DB6"), TextAlignment = TextAlignment.Center });
        var foot = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8, Margin = new(0, 4, 0, 0) };
        var help = new Button { Name = "MoskomHelp", Content = "Справка", HorizontalAlignment = HorizontalAlignment.Stretch };
        var exit = new Button { Content = "Выход", HorizontalAlignment = HorizontalAlignment.Stretch };
        foot.Children.Add(help); Grid.SetColumn(exit, 1); foot.Children.Add(exit); shell.Children.Add(foot);
        shell.Children.Add(new TextBlock { Text = "МосКомпРемонт VPN " + MoskomBuildInfo.Version + " · v2rayN 7.25.6 · GPL-3.0", FontSize = 10, Foreground = Brush.Parse("#7D89A5"), TextAlignment = TextAlignment.Center });
        Content = new ScrollViewer { Content = shell, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _servers.ItemTemplate = new FuncDataTemplate<ComboItem>((x, _) => new TextBlock { Text = x?.Text, TextTrimming = TextTrimming.CharacterEllipsis });
        _routing.ItemTemplate = new FuncDataTemplate<RoutingItem>((x, _) => new TextBlock { Text = x?.Remarks?.Replace("RUv1-", ""), TextTrimming = TextTrimming.CharacterEllipsis });
        _servers.ItemsSource = vm.StatusBarViewModel.Servers;
        _routing.ItemsSource = vm.StatusBarViewModel.RoutingItems;
        _power.Click += async (_, _) =>
        {
            var c = MoskomLauncherRuntime.Connection!;
            if (c.State == MoskomConnectionState.Connecting) { c.Cancel(); return; }
            await RunAsync(MoskomLauncherRuntime.ToggleConnectionAsync);
        };
        _plus.Click += async (_, _) => await RunAsync(async () =>
        {
            var text = await AvaUtils.GetClipboardData(owner);
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("В буфере нет ссылки. Сначала скопируйте её в личном кабинете.");
            _hint.Text = "Добавляем ссылку и получаем серверы…";
            await MoskomLauncherRuntime.ImportAsync(text);
            await RefreshAsync(); _hint.Text = "Ссылка добавлена. Выберите сервер и включите VPN.";
        });
        _update.Click += async (_, _) => await RunAsync(async () => { _hint.Text = "Обновляем подписки…"; await MoskomLauncherRuntime.UpdateAsync(); await RefreshAsync(); _hint.Text = "Запрос обновления завершён. Выберите нужный сервер."; });
        _availability.Click += async (_, _) => await RunAsync(async () =>
        {
            _availabilityResult.Text = "Проверяем выбранный сервер…";
            var check = await MoskomLauncherRuntime.CheckAvailabilityAsync();
            _checkedServer = AppManager.Instance.Config.IndexId;
            _availabilityResult.Text = check.Server + "\n" + (check.Milliseconds is > 0 ? $"Доступен · {check.Milliseconds} мс · {check.CheckedAt:HH:mm}" : $"Не отвечает · {check.CheckedAt:HH:mm}");
            _availabilityResult.Foreground = Brush.Parse(check.Milliseconds is > 0 ? "#55D6AC" : "#F49A9A");
            await MoskomLauncherRuntime.RefreshServiceAsync();
        });
        _websiteStatus.Click += async (_, _) => await MoskomWebsiteViews.ShowStatusAsync(_owner);
        _account.Click += async (_, _) => await MoskomWebsiteViews.ShowAccountAsync(_owner);
        MoskomSettingsViews.AttachUpdates(owner);
        _servers.SelectionChanged += async (_, _) => { if (!_refreshing && _servers.SelectedItem is ComboItem item) { ResetAvailability(); await RunAsync(() => MoskomLauncherRuntime.SelectServerAsync(item)); } };
        _routing.SelectionChanged += async (_, _) => { if (!_refreshing && _routing.SelectedItem is RoutingItem item) await RunAsync(() => MoskomLauncherRuntime.SelectRoutingAsync(item)); };
        help.Click += async (_, _) => await MoskomHelpView.ShowAsync(owner);
        exit.Click += async (_, _) => await RunAsync(() => AppManager.Instance.AppExitAsync(true));
        if (MoskomLauncherRuntime.Connection is { } connection) connection.Changed += state => Dispatcher.UIThread.Post(() => RenderState(state));
        MoskomLauncherRuntime.ListsChanged += () => Dispatcher.UIThread.Post(async () => await RefreshAsync());
        MoskomLauncherRuntime.WebsiteChanged += () => Dispatcher.UIThread.Post(RenderWebsite);
        ((INotifyCollectionChanged)vm.StatusBarViewModel.Servers).CollectionChanged += (_, _) => Dispatcher.UIThread.Post(async () => await RefreshAsync());
        ((INotifyCollectionChanged)vm.StatusBarViewModel.RoutingItems).CollectionChanged += (_, _) => Dispatcher.UIThread.Post(async () => await RefreshAsync());
        RenderState(MoskomConnectionState.Disconnected);
        RenderWebsite();
        AttachedToVisualTree += async (_, _) => { await vm.RefreshForLauncherAsync(); await vm.StatusBarViewModel.RefreshRoutingsMenu(); await RefreshAsync(); await MoskomLauncherRuntime.StartWebsiteAsync(); };
    }
    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (_checkedServer != null && _checkedServer != AppManager.Instance.Config.IndexId) ResetAvailability();
            _servers.SelectedItem = _vm.StatusBarViewModel.Servers.FirstOrDefault(x => x.ID == AppManager.Instance.Config.IndexId);
            var active = (await AppManager.Instance.RoutingItems()).FirstOrDefault(x => x.IsActive);
            _routing.SelectedItem = _vm.StatusBarViewModel.RoutingItems.FirstOrDefault(x => x.Id == active?.Id);
        }
        finally { _refreshing = false; }
    }
    private void ResetAvailability()
    {
        _checkedServer = null; _availabilityResult.Text = "Проверка выбранного сервера ещё не выполнялась."; _availabilityResult.Foreground = Brush.Parse("#A7B7D0");
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_operation) return;
        _operation = true; RefreshEnabled();
        try { await action(); }
        catch (InvalidOperationException e) { _hint.Text = e.Message; }
        catch { _hint.Text = "Не удалось выполнить действие. Проверьте подключение и повторите попытку."; }
        finally { _operation = false; RefreshEnabled(); }
    }
    private void RefreshEnabled()
    {
        var state = MoskomLauncherRuntime.Connection?.State ?? MoskomConnectionState.Disconnected;
        var busy = _operation || state is MoskomConnectionState.Connecting or MoskomConnectionState.Disconnecting;
        _servers.IsEnabled = _routing.IsEnabled = _plus.IsEnabled = _update.IsEnabled = _availability.IsEnabled = _account.IsEnabled = _menu.IsEnabled = !busy;
        _power.IsEnabled = state == MoskomConnectionState.Connecting || !busy;
    }
    private void RenderState(MoskomConnectionState state)
    {
        var on = state == MoskomConnectionState.Connected;
        _power.Background = on ? Brush.Parse("#157D6A") : MoskomBrand.Power;
        _power.BorderBrush = Brush.Parse(on ? "#51DFC0" : "#59CFEF"); _power.BorderThickness = new(2);
        _powerLabel.Text = state switch { MoskomConnectionState.Connecting => "Отменить", MoskomConnectionState.Connected => "Выключить VPN", _ => "Включить VPN" };
        _status.Text = state switch { MoskomConnectionState.Connecting => "Проверяем подключение…", MoskomConnectionState.Connected => "VPN включён", MoskomConnectionState.Disconnecting => "Выключаем…", MoskomConnectionState.Failed => "Не удалось подключиться", _ => "VPN выключен" };
        if (on) _hint.Text = "Системный прокси включён. Используется выбранная маршрутизация.";
        else if (state == MoskomConnectionState.Disconnected) _hint.Text = _vm.StatusBarViewModel.Servers.Count == 0 ? "Скопируйте ссылку-подписку и нажмите «+»." : "Выберите сервер и нажмите большую кнопку.";
        RefreshEnabled();
    }
    private void RenderWebsite()
    {
        var runtimeError = MoskomLauncherRuntime.ServiceError;
        var notice = MoskomLauncherRuntime.ServiceNotice;
        var stale = MoskomLauncherRuntime.ServiceLoadedAt is not { } at || DateTimeOffset.UtcNow - at > TimeSpan.FromMinutes(3);
        _websiteStatus.Content = new TextBlock { Text = runtimeError != "" || stale ? "Статус сервиса · обновить" : notice?.Title ?? "Статус сервиса", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 11 };
        _websiteStatus.Background = Brush.Parse(runtimeError != "" || stale || notice?.Enabled != true ? "#253147" : notice.Level switch { "ok" => "#14694E", "down" => "#8B303D", _ => "#806017" });
        _websiteStatus.Foreground = Brushes.White;
        _account.Content = MoskomLauncherRuntime.CabinetUser == null ? "Войти в кабинет" : "Мой кабинет";
        var user = MoskomLauncherRuntime.CabinetUser;
        _accountInfo.Text = (user == null ? "" : user.FullName + "\n") + MoskomLauncherRuntime.CabinetMessage;
    }
}
