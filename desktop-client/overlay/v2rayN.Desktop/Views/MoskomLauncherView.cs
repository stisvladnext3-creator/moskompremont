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
    private readonly TextBlock _status = new() { Name = "MoskomStatus", Text = "VPN выключен", FontSize = 22, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _hint = new() { Name = "MoskomHint", Text = "Скопируйте ссылку-подписку и нажмите «+».", FontSize = 13, Foreground = Brush.Parse("#A7B7D0"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MinHeight = 42 };
    private readonly TextBlock _powerLabel = new() { Text = "Включить VPN", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private bool _refreshing;
    private bool _operation;

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, Foreground = Brush.Parse("#A7B7D0"), Margin = new(0, 0, 0, 7) };
    public MoskomLauncherView(Window owner, MainWindowViewModel vm)
    {
        _owner = owner; _vm = vm;
        Background = Brush.Parse("#0A1020"); Foreground = Brush.Parse("#ECF3FF");
        var shell = new StackPanel { Margin = new(30, 24), Spacing = 14, MaxWidth = 430, HorizontalAlignment = HorizontalAlignment.Stretch };
        shell.Children.Add(new TextBlock { Text = "МОСКОМПРЕМОНТ", FontSize = 11, Foreground = Brush.Parse("#65DCEC"), FontWeight = FontWeight.Bold, LetterSpacing = 2 });
        shell.Children.Add(new TextBlock { Text = "Мировой интернет", FontSize = 27, FontWeight = FontWeight.Bold });
        shell.Children.Add(new TextBlock { Text = "Быстрое подключение на Windows", FontSize = 12, Foreground = Brush.Parse("#8D9DB6") });
        var powerContent = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        powerContent.Children.Add(new TextBlock { Text = "⏻", FontSize = 68, Foreground = Brushes.White, FontFamily = new("Segoe UI Symbol, DejaVu Sans"), HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 72 });
        powerContent.Children.Add(_powerLabel); _power.Content = powerContent;
        AutomationProperties.SetName(_power, "Включить VPN");
        shell.Children.Add(new Border { Child = _power, Padding = new(0, 12, 0, 0) });
        shell.Children.Add(_status); shell.Children.Add(_hint);
        var serverPanel = new StackPanel(); serverPanel.Children.Add(Label("Сервер"));
        var serverRow = new Grid { ColumnDefinitions = new("*,46"), ColumnSpacing = 8 };
        serverRow.Children.Add(_servers); Grid.SetColumn(_plus, 1); serverRow.Children.Add(_plus); serverPanel.Children.Add(serverRow); shell.Children.Add(serverPanel);
        ToolTip.SetTip(_plus, "Добавить скопированную ссылку или ключ"); AutomationProperties.SetName(_plus, "Добавить ссылку из буфера обмена");
        var routingPanel = new StackPanel(); routingPanel.Children.Add(Label("Маршрутизация · Russia")); routingPanel.Children.Add(_routing); shell.Children.Add(routingPanel);
        shell.Children.Add(_update);
        shell.Children.Add(new TextBlock { Text = "Режим: системный прокси Windows", FontSize = 12, Foreground = Brush.Parse("#8D9DB6"), TextAlignment = TextAlignment.Center });
        var foot = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8, Margin = new(0, 4, 0, 0) };
        var help = new Button { Content = "Справка", HorizontalAlignment = HorizontalAlignment.Stretch };
        var exit = new Button { Content = "Выход", HorizontalAlignment = HorizontalAlignment.Stretch };
        foot.Children.Add(help); Grid.SetColumn(exit, 1); foot.Children.Add(exit); shell.Children.Add(foot);
        shell.Children.Add(new TextBlock { Text = "На основе v2rayN 7.25.6 · GPL-3.0", FontSize = 10, Foreground = Brush.Parse("#6E7F9B"), TextAlignment = TextAlignment.Center });
        Content = new ScrollViewer { Content = shell, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _servers.ItemTemplate = new FuncDataTemplate<ComboItem>((x, _) => new TextBlock { Text = x?.Text, TextTrimming = TextTrimming.CharacterEllipsis });
        _routing.ItemTemplate = new FuncDataTemplate<RoutingItem>((x, _) => new TextBlock { Text = x?.Remarks?.Replace("RUv1-", ""), TextTrimming = TextTrimming.CharacterEllipsis });
        _servers.ItemsSource = vm.StatusBarViewModel.Servers;
        _routing.ItemsSource = vm.StatusBarViewModel.RoutingItems;
        _power.Click += async (_, _) =>
        {
            var c = MoskomLauncherRuntime.Connection!;
            if (c.State == MoskomConnectionState.Connecting) { c.Cancel(); return; }
            await RunAsync(async () => { if (c.IsConnected) await c.DisconnectAsync(); else await c.ConnectAsync(); });
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
        _servers.SelectionChanged += async (_, _) => { if (!_refreshing && _servers.SelectedItem is ComboItem item) await RunAsync(() => MoskomLauncherRuntime.SelectServerAsync(item)); };
        _routing.SelectionChanged += async (_, _) => { if (!_refreshing && _routing.SelectedItem is RoutingItem item) await RunAsync(() => MoskomLauncherRuntime.SelectRoutingAsync(item)); };
        help.Click += (_, _) => { var file = System.IO.Path.Combine(Utils.GetBaseDirectory(), "КАК-ПОЛЬЗОВАТЬСЯ.txt"); if (File.Exists(file)) Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); };
        exit.Click += async (_, _) => await RunAsync(() => AppManager.Instance.AppExitAsync(true));
        if (MoskomLauncherRuntime.Connection is { } connection) connection.Changed += state => Dispatcher.UIThread.Post(() => RenderState(state));
        MoskomLauncherRuntime.ListsChanged += () => Dispatcher.UIThread.Post(async () => await RefreshAsync());
        ((INotifyCollectionChanged)vm.StatusBarViewModel.Servers).CollectionChanged += (_, _) => Dispatcher.UIThread.Post(async () => await RefreshAsync());
        ((INotifyCollectionChanged)vm.StatusBarViewModel.RoutingItems).CollectionChanged += (_, _) => Dispatcher.UIThread.Post(async () => await RefreshAsync());
        RenderState(MoskomConnectionState.Disconnected);
        AttachedToVisualTree += async (_, _) => { await vm.RefreshForLauncherAsync(); await vm.StatusBarViewModel.RefreshRoutingsMenu(); await RefreshAsync(); };
    }
    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            _servers.SelectedItem = _vm.StatusBarViewModel.Servers.FirstOrDefault(x => x.ID == AppManager.Instance.Config.IndexId);
            var active = (await AppManager.Instance.RoutingItems()).FirstOrDefault(x => x.IsActive);
            _routing.SelectedItem = _vm.StatusBarViewModel.RoutingItems.FirstOrDefault(x => x.Id == active?.Id);
        }
        finally { _refreshing = false; }
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
        _servers.IsEnabled = _routing.IsEnabled = _plus.IsEnabled = _update.IsEnabled = !busy;
        _power.IsEnabled = state == MoskomConnectionState.Connecting || !busy;
    }
    private void RenderState(MoskomConnectionState state)
    {
        var on = state == MoskomConnectionState.Connected;
        _power.Background = Brush.Parse(on ? "#157D6A" : "#335FD6");
        _powerLabel.Text = state switch { MoskomConnectionState.Connecting => "Отменить", MoskomConnectionState.Connected => "Выключить VPN", _ => "Включить VPN" };
        _status.Text = state switch { MoskomConnectionState.Connecting => "Проверяем подключение…", MoskomConnectionState.Connected => "VPN включён", MoskomConnectionState.Disconnecting => "Выключаем…", MoskomConnectionState.Failed => "Не удалось подключиться", _ => "VPN выключен" };
        if (on) _hint.Text = "Системный прокси включён. Используется выбранная маршрутизация.";
        else if (state == MoskomConnectionState.Disconnected) _hint.Text = _vm.StatusBarViewModel.Servers.Count == 0 ? "Скопируйте ссылку-подписку и нажмите «+»." : "Выберите сервер и нажмите большую кнопку.";
        RefreshEnabled();
    }
}
