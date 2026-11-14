using ServiceLib.Services;
using Avalonia.Layout;

namespace v2rayN.Desktop.Views;

public static class MoskomHelpView
{
    public static Task ShowAsync(Window owner)
    {
        var window = new Window
        {
            Name = "MoskomHelpWindow", Title = "Справка · МосКомпРемонт", Icon = MoskomBrand.Icon(),
            Width = 600, Height = 730, MinWidth = 380, MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = MoskomBrand.Background
        };
        MoskomBrand.Style(window.Styles);
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(24), RowSpacing = 18 };
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(MoskomBrand.Heading("Справка"));
        heading.Children.Add(Text("Что делает каждая кнопка · версия " + MoskomBuildInfo.Version)); root.Children.Add(heading);
        var topics = new StackPanel { Name = "MoskomHelpTopics", Spacing = 10 };
        void Topic(string title, string body)
        {
            var content = new StackPanel { Spacing = 5 };
            content.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#85E7F7"), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(Text(body));
            topics.Children.Add(new Border { Background = Brush.Parse("#111522"), BorderBrush = Brush.Parse("#2F354B"), BorderThickness = new(1), CornerRadius = new(12), Padding = new(16, 13), Child = content });
        }
        Topic("Включить VPN / Выключить VPN", "Большая кнопка подключает выбранный сервер. При подключении можно нажать «Отменить». Повторное нажатие выключает VPN.");
        Topic("Подписка и сервер", "Сначала выберите нужную подписку по названию, затем сервер из неё. Серверы разных подписок не смешиваются. Добавленные вручную отдельные ключи находятся в своей группе. При смене подписки или сервера работающий VPN переподключится.");
        Topic("+ · Добавить ссылку", "Сначала скопируйте ссылку-подписку или отдельный ключ. Нажмите «+»: приложение добавит ссылку и загрузит доступные серверы.");
        Topic("Маршрутизация · Russia", "Режим «Всё» — полная маршрутизация, не работают российские сайты.\n\n«Всё, кроме РФ» — всё идет через ВПН, кроме Российских сервисов, работает всё, но скорость сниженная.\n\n«Заблокированное» — ВПН работает только на заблокированные в РФ сервисы, российские сервисы работают без сбоев, скорость без замедлений.");
        Topic("Обновить серверы", "Загружает свежие данные подписок. Если вы вошли в кабинет, также получает изменения ваших ссылок с сайта.");
        Topic("Проверить доступность", "Проверяет выбранный сервер с вашего компьютера и показывает результат в мс. Системный прокси для этой проверки не включается.");
        Topic("Статус сервиса", "Открывает объявление и состояние серверов с сайта. Проверка на сервере сервиса может отличаться от результата на вашем компьютере.");
        Topic("Войти в кабинет / Мой кабинет", "Войдите с почтой и паролем сайта: активные подписки загрузятся автоматически и будут синхронизироваться. «Выйти из кабинета» удаляет сохранённый вход и подписки кабинета; добавленные через «+» сохраняются.");
        Topic("☰ · Меню приложения", "Три полоски справа вверху открывают управление подписками, обновление правил, проверку версии приложения, DNS, справку и выход. В «Управлении подписками» можно удалить добавленную вручную подписку со всеми её серверами. Удаление требует подтверждения; активное подключение этой подписки отключится. Подписки из кабинета управляются на сайте.");
        Topic("Обновить правила Russia", "Загружает актуальные списки маршрутизации и геоданные. Выбранная политика и DNS сохраняются. Новые ограничения учитываются после добавления сайтов в исходные списки Russia.");
        Topic("Проверить обновления приложения", "Проверяет новую версию. «Обновить сейчас» скачивает и устанавливает её с перезапуском; «Позже» откладывает предложение на сутки. Подписки и настройки сохраняются.");
        Topic("DNS", "Выберите автоматически из Russia, 1.1.1.1, 8.8.8.8, 9.9.9.9 или свой адрес и нажмите «Сохранить DNS». Настройка действует внутри VPN; активное подключение перезапустится.");
        Topic("Справка", "Открывает эту страницу прямо в приложении. Прокрутите её, чтобы увидеть описание всех кнопок.");
        Topic("Выход и крестик окна", "«Выход» завершает приложение и восстанавливает прежний прокси Windows. Крестик главного окна сворачивает приложение в трей; откройте его значком рядом с часами.");
        var scroll = new ScrollViewer { Name = "MoskomHelpScroll", Content = topics, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 10 };
        footer.Children.Add(Text("Режим подключения — системный прокси Windows. Его используют приложения, которые поддерживают системный прокси."));
        var close = new Button { Name = "MoskomHelpClose", Content = "Понятно", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, Background = MoskomBrand.Primary, Foreground = Brush.Parse("#070B15") };
        close.Click += (_, _) => window.Close(); footer.Children.Add(close); Grid.SetRow(footer, 2); root.Children.Add(footer);
        window.Content = root; return window.ShowDialog(owner);
    }

    private static TextBlock Text(string text) => new() { Text = text, FontSize = 13, LineHeight = 20, Foreground = Brush.Parse("#B8C8DF"), TextWrapping = TextWrapping.Wrap };
}
