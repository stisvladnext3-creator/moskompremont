using ServiceLib.Services;
using Avalonia.Layout;

namespace v2rayN.Desktop.Views;

public static class MoskomSettingsViews
{
    private static TextBlock Text(string value, double size = 13) => new() { Text = value, FontSize = size, Foreground = Brush.Parse("#BDCAE3"), TextWrapping = TextWrapping.Wrap };
    private static Button Button(string value, string name) => new() { Name = name, Content = value, MinHeight = 42, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Window Dialog(Window owner, string title, StackPanel panel, double height = 510)
    {
        var window = new Window { Title = title + " · МосКомпРемонт", Icon = MoskomBrand.Icon(), Width = 490, Height = height, MinWidth = 400, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = MoskomBrand.Background };
        MoskomBrand.Style(window.Styles); panel.Margin = new(24); panel.Spacing = 14; window.Content = new ScrollViewer { Content = panel }; return window;
    }
    private static string Message(Exception error) => error is InvalidOperationException or IOException ? error.Message : "Не удалось выполнить обновление. Проверьте интернет и повторите попытку.";
    private static IProgress<MoskomTransferProgress> Progress(TextBlock label)
    {
        string previous = "";
        return new Progress<MoskomTransferProgress>(p =>
        {
            var value = p.Percent is { } percent ? $"{p.File} · {percent}%" : $"{p.File} · {p.Received / 1024 / 1024} МБ";
            if (value != previous) { previous = value; label.Text = value; }
        });
    }
    public static async Task ShowRoutingAsync(Window owner)
    {
        var panel = new StackPanel(); panel.Children.Add(MoskomBrand.Heading("Правила маршрутизации"));
        panel.Children.Add(Text("Russia определяет, какие сайты идут через VPN. Обновление загружает актуальные списки и геоданные runetfreedom. Выбранная политика и ваш DNS сохраняются."));
        panel.Children.Add(Text("Новые блокировки появятся в приложении после добавления в исходный список Russia.", 12));
        var stamp = MoskomLauncherRuntime.Preferences.RulesUpdatedAt;
        panel.Children.Add(Text(stamp == null ? "Пресет Russia включён в установщик." : "Обновлено: " + stamp.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")));
        var status = Text("Можно обновлять без переустановки приложения.");
        var update = Button("Обновить правила Russia", "MoskomRulesApply"); var close = Button("Закрыть", "MoskomRulesClose");
        update.Background = MoskomBrand.Primary; update.Foreground = Brush.Parse("#070B15");
        panel.Children.Add(update); panel.Children.Add(status); panel.Children.Add(close);
        var window = Dialog(owner, "Правила маршрутизации", panel); CancellationTokenSource? operation = null;
        update.Click += async (_, _) =>
        {
            update.IsEnabled = false; close.Content = "Отменить загрузку"; status.Text = "Проверяем актуальную версию Russia…"; operation = new();
            try { var changed = await MoskomLauncherRuntime.UpdateRoutingRulesAsync(operation.Token, Progress(status)); status.Text = changed ? "Правила Russia обновлены. Настройки применены." : "У вас уже актуальные правила Russia."; }
            catch (OperationCanceledException) { status.Text = "Обновление отменено. Предыдущие правила сохранены."; }
            catch (Exception e) { status.Text = Message(e); }
            finally { operation.Dispose(); operation = null; update.IsEnabled = true; close.Content = "Закрыть"; }
        };
        close.Click += (_, _) => { if (operation != null) operation.Cancel(); else window.Close(); };
        window.Closed += (_, _) => operation?.Cancel(); await window.ShowDialog(owner);
    }
    public static async Task ShowDnsAsync(Window owner)
    {
        var panel = new StackPanel(); panel.Children.Add(MoskomBrand.Heading("DNS"));
        panel.Children.Add(Text("DNS применяется к доменам, которые обрабатывает VPN-приложение. «Автоматически» использует настройки Russia. В готовых вариантах запросы через VPN защищены DNS-over-HTTPS."));
        var ids = new[] { "auto", "cloudflare", "google", "quad9", "custom" };
        var selector = new ComboBox { Name = "MoskomDnsChoice", ItemsSource = MoskomDnsChoices.Names, SelectedIndex = Math.Max(0, Array.IndexOf(ids, MoskomLauncherRuntime.Preferences.Dns)), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
        var custom = new TextBox { Name = "MoskomDnsCustom", Watermark = "IP-адрес или https://…/dns-query", Text = MoskomLauncherRuntime.Preferences.CustomDns, MinHeight = 44 };
        custom.IsVisible = selector.SelectedIndex == 4; selector.SelectionChanged += (_, _) => custom.IsVisible = selector.SelectedIndex == 4;
        panel.Children.Add(selector); panel.Children.Add(custom);
        panel.Children.Add(Text("Для прямых соединений и запуска VPN сохраняются DNS и bootstrap из Russia. При изменении настроек активное подключение будет перезапущено.", 12));
        var save = Button("Сохранить DNS", "MoskomDnsSave"); save.Background = MoskomBrand.Primary; save.Foreground = Brush.Parse("#070B15");
        var status = Text(""); panel.Children.Add(save); panel.Children.Add(status); var window = Dialog(owner, "DNS", panel);
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try { await MoskomLauncherRuntime.SaveDnsAsync(ids[selector.SelectedIndex], custom.Text); status.Text = "DNS сохранён и применён."; }
            catch (Exception e) { status.Text = Message(e); }
            finally { save.IsEnabled = true; }
        };
        await window.ShowDialog(owner);
    }
    public static async Task CheckApplicationAsync(Window owner)
    {
        var panel = new StackPanel(); panel.Children.Add(MoskomBrand.Heading("Обновление приложения"));
        var label = Text("Проверяем наличие обновления…"); panel.Children.Add(label);
        panel.Children.Add(Text("Установлена версия " + MoskomBuildInfo.Version, 12));
        var window = Dialog(owner, "Обновление приложения", panel, 350); MoskomAppRelease? found = null;
        window.Opened += async (_, _) =>
        {
            try { found = await MoskomLauncherRuntime.CheckApplicationUpdateAsync(true); if (found != null) window.Close(); else label.Text = "У вас установлена последняя доступная версия."; }
            catch (Exception e) { label.Text = Message(e); }
        };
        await window.ShowDialog(owner); if (found != null) await ShowUpdateAsync(owner, found);
    }
    public static async Task ShowUpdateAsync(Window owner, MoskomAppRelease release)
    {
        var panel = new StackPanel(); panel.Children.Add(MoskomBrand.Heading("Доступно обновление " + release.Version));
        panel.Children.Add(Text("Установлена версия " + MoskomBuildInfo.Version + ". Подписки и настройки сохранятся."));
        var status = Text("После загрузки проверим файл, завершим подключение и запустим установку.");
        var now = Button("Обновить сейчас", "MoskomUpdateNow"); var later = Button("Позже", "MoskomUpdateLater");
        now.Background = MoskomBrand.Primary; now.Foreground = Brush.Parse("#070B15"); panel.Children.Add(now); panel.Children.Add(later); panel.Children.Add(status);
        var window = Dialog(owner, "Доступно обновление " + release.Version, panel, 430); CancellationTokenSource? operation = null; var installing = false;
        now.Click += async (_, _) =>
        {
            operation = new(); now.IsEnabled = false; later.Content = "Отменить загрузку";
            try
            {
                var installer = await MoskomLauncherRuntime.DownloadApplicationUpdateAsync(release, operation.Token, Progress(status));
                status.Text = "Файл проверен. Запускаем установку…"; installing = true;
                await MoskomLauncherRuntime.InstallApplicationUpdateAsync(installer);
            }
            catch (OperationCanceledException) { status.Text = "Загрузка отменена."; }
            catch (Exception e) { installing = false; status.Text = Message(e); }
            finally { operation.Dispose(); operation = null; now.IsEnabled = true; later.Content = "Позже"; }
        };
        later.Click += (_, _) => { if (operation != null) operation.Cancel(); else window.Close(); };
        window.Closed += (_, _) => { operation?.Cancel(); if (!installing) MoskomLauncherRuntime.DeferApplicationUpdate(release); };
        await window.ShowDialog(owner);
    }
    public static void AttachUpdates(Window owner)
    {
        MoskomAppRelease? pending = null; var showing = false;
        async Task Present()
        {
            if (pending == null || showing || !owner.IsVisible || !owner.IsEnabled || owner.OwnedWindows.Any(w => w.IsVisible)) return;
            var release = pending; pending = null; showing = true;
            try { await ShowUpdateAsync(owner, release); } finally { showing = false; }
        }
        MoskomLauncherRuntime.ApplicationUpdateFound += release => Dispatcher.UIThread.Post(async () => { pending = release; await Present(); });
        owner.Activated += async (_, _) => await Present();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; timer.Tick += async (_, _) => await Present(); timer.Start(); owner.Closed += (_, _) => timer.Stop();
    }
}
