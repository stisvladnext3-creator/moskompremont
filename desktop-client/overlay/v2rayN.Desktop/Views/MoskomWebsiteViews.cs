using ServiceLib.Services;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;

namespace v2rayN.Desktop.Views;

public static class MoskomWebsiteViews
{
    private static TextBlock Text(string value, double size = 14) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#E5EEFF") };
    private static Button Button(string value) => new() { Content = value, MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Window Window(string title, double height)
    {
        var window = new Window { Title = title, Icon = MoskomBrand.Icon(), Width = 470, Height = height, MinWidth = 400, MinHeight = 350, Background = MoskomBrand.Background, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        MoskomBrand.Style(window.Styles); return window;
    }
    private static void OpenCabinet() => Process.Start(new ProcessStartInfo(new Uri(MoskomWebsiteClient.Site, "account.html").AbsoluteUri) { UseShellExecute = true });

    public static async Task ShowAccountAsync(Window owner)
    {
        var window = Window("Личный кабинет · МосКомпРемонт", 550);
        var panel = new StackPanel { Margin = new(24), Spacing = 12 };
        window.Content = new ScrollViewer { Content = panel };
        panel.Children.Add(MoskomBrand.Heading("Личный кабинет")); panel.Children.Add(Text("moskompremont.ru", 12));
        var message = Text(MoskomLauncherRuntime.CabinetMessage, 12);
        if (MoskomLauncherRuntime.CabinetUser is { } user)
        {
            panel.Children.Add(Text(user.FullName, 18)); panel.Children.Add(Text(user.Email)); panel.Children.Add(message);
            var sync = Button("Синхронизировать подписки"); var logout = Button("Выйти из кабинета"); var web = Button("Открыть кабинет на сайте");
            panel.Children.Add(sync); panel.Children.Add(web); panel.Children.Add(logout);
            sync.Click += async (_, _) => { sync.IsEnabled = logout.IsEnabled = false; try { await MoskomLauncherRuntime.SyncCabinetAsync(true); message.Text = MoskomLauncherRuntime.CabinetMessage; } catch (Exception e) { message.Text = SafeMessage(e); } finally { sync.IsEnabled = logout.IsEnabled = true; } };
            logout.Click += async (_, _) => { logout.IsEnabled = sync.IsEnabled = false; try { await MoskomLauncherRuntime.LogoutCabinetAsync(); window.Close(); } catch (Exception e) { message.Text = SafeMessage(e); logout.IsEnabled = sync.IsEnabled = true; } };
            web.Click += (_, _) => OpenCabinet();
        }
        else
        {
            panel.Children.Add(Text("Войдите той же почтой и паролем, что на сайте. Приложение получит ваши подписки и будет проверять изменения автоматически.", 13));
            var email = new TextBox { Name = "MoskomEmail", Watermark = "Почта", MinHeight = 42 };
            var password = new TextBox { Name = "MoskomPassword", Watermark = "Пароль", PasswordChar = '*', MinHeight = 42 };
            var login = Button("Войти и получить подписки"); login.Name = "MoskomLoginSubmit"; var register = Button("Регистрация на сайте");
            panel.Children.Add(email); panel.Children.Add(password); panel.Children.Add(login); panel.Children.Add(register); panel.Children.Add(message);
            panel.Children.Add(Text("Пароль не сохраняется. Вход защищён средствами вашей учётной записи Windows.", 11));
            async Task Login()
            {
                if (!login.IsEnabled) return;
                if (string.IsNullOrWhiteSpace(email.Text) || string.IsNullOrEmpty(password.Text)) { message.Text = "Введите почту и пароль."; return; }
                login.IsEnabled = false; message.Text = "Входим и получаем подписки…";
                try { await MoskomLauncherRuntime.LoginCabinetAsync(email.Text, password.Text); password.Text = ""; window.Close(); }
                catch (Exception e) { message.Text = SafeMessage(e); }
                finally { password.Text = ""; login.IsEnabled = true; }
            }
            login.Click += async (_, _) => await Login();
            password.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await Login(); } };
            register.Click += (_, _) => OpenCabinet();
        }
        await window.ShowDialog(owner);
    }

    public static async Task ShowStatusAsync(Window owner)
    {
        var window = Window("Статус сервиса · МосКомпРемонт", 680);
        var panel = new StackPanel { Margin = new(24), Spacing = 12 };
        var refresh = Button("Обновить статус"); var content = new StackPanel { Spacing = 12 };
        panel.Children.Add(Text("Статус сервиса", 24)); panel.Children.Add(Text("Мониторинг с сайта moskompremont.ru. Доступность с вашего компьютера проверяется отдельной кнопкой в главном окне.", 12)); panel.Children.Add(refresh); panel.Children.Add(content);
        window.Content = new ScrollViewer { Content = panel };
        void Render()
        {
            content.Children.Clear(); var now = DateTimeOffset.UtcNow;
            var notice = MoskomLauncherRuntime.ServiceNotice;
            if (MoskomLauncherRuntime.ServiceError != "") content.Children.Add(Text(MoskomLauncherRuntime.ServiceError, 13));
            if (notice != null)
            {
                var label = Text(notice.Title, 17); label.Foreground = Brush.Parse(!notice.Enabled ? "#A7B7D0" : notice.Level switch { "ok" => "#55D6AC", "down" => "#F49A9A", _ => "#F5CD70" });
                content.Children.Add(label);
                if (notice.Enabled && !string.IsNullOrEmpty(notice.Message)) content.Children.Add(Text(notice.Message, 13));
            }
            var monitor = MoskomLauncherRuntime.ServerMonitor;
            if (monitor?.Enabled == true)
            {
                var nodes = new List<WebsiteNode>(); if (monitor.Main != null) nodes.Add(monitor.Main); nodes.AddRange(monitor.Nodes ?? []);
                foreach (var node in nodes)
                {
                    var row = new StackPanel { Spacing = 4 }; row.Children.Add(Text(node.Name, 14));
                    var detail = Text(node.Display(now), 13); detail.Foreground = Brush.Parse(node.EffectiveState(now) switch { "up" => "#55D6AC", "down" => "#F49A9A", _ => "#A7B7D0" }); row.Children.Add(detail);
                    if (WebsiteTime.Parse(node.CheckedAt) is { } time) row.Children.Add(Text(time.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), 11));
                    content.Children.Add(new Border { Child = row, Padding = new(12), Background = Brush.Parse("#151F32"), CornerRadius = new(10) });
                }
                if (!WebsiteTime.Fresh(monitor.ReportedAt, now)) content.Children.Add(Text("Нет свежего отчёта мониторинга. Обновите позже.", 12));
                if (monitor.SourceStatus is "fetch_error" or "parse_error" or "limit_exceeded") content.Children.Add(Text("Сайт не смог обновить список серверов подписки.", 12));
            }
            else content.Children.Add(Text("Мониторинг пока недоступен или выключен на сайте.", 13));
        }
        async Task Load() { refresh.IsEnabled = false; try { await MoskomLauncherRuntime.RefreshServiceAsync(); Render(); } finally { refresh.IsEnabled = true; } }
        refresh.Click += async (_, _) => await Load();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) }; timer.Tick += (_, _) => Render(); timer.Start(); window.Closed += (_, _) => timer.Stop();
        window.Opened += async (_, _) => await Load(); Render(); await window.ShowDialog(owner);
    }
    private static string SafeMessage(Exception e) => e is InvalidOperationException ? e.Message : "Не удалось связаться с сайтом. Проверьте интернет и повторите попытку.";
}
