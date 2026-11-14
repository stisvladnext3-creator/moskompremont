using ServiceLib.Services;
using Avalonia.Layout;
using Avalonia.Automation;

namespace v2rayN.Desktop.Views;

public static class MoskomSubscriptionViews
{
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#BDCAE3") };
    public static async Task ShowAsync(Window owner)
    {
        var panel = new StackPanel { Margin = new(24), Spacing = 14 };
        panel.Children.Add(MoskomBrand.Heading("Управление подписками"));
        panel.Children.Add(Text("Здесь можно удалить подписки, добавленные вручную. Подписки из личного кабинета управляются на сайте."));
        var list = new StackPanel { Name = "MoskomManualSubscriptions", Spacing = 12 };
        var status = Text(""); status.Name = "MoskomSubscriptionDeleteStatus";
        var close = new Button { Name = "MoskomSubscriptionsClose", Content = "Закрыть", MinHeight = 42, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(list); panel.Children.Add(status); panel.Children.Add(close);
        var window = new Window { Title = "Управление подписками · МосКомпРемонт", Icon = MoskomBrand.Icon(), Width = 510, Height = 580, MinWidth = 400, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = MoskomBrand.Background, Content = new ScrollViewer { Content = panel } };
        MoskomBrand.Style(window.Styles); close.Click += (_, _) => window.Close();
        async Task Refresh()
        {
            list.Children.Clear();
            var groups = await MoskomLauncherRuntime.ManualSubscriptionsAsync();
            if (groups.Count == 0)
            {
                var empty = Text("Подписок, добавленных вручную, пока нет."); empty.Name = "MoskomManualSubscriptionsEmpty";
                list.Children.Add(empty); return;
            }
            foreach (var group in groups)
            {
                var row = new StackPanel { Spacing = 8 };
                row.Children.Add(new TextBlock { Text = group.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#ECF3FF") });
                row.Children.Add(Text($"Серверов: {group.Servers.Count}"));
                var remove = new Button { Name = "MoskomDeleteSubscription", Tag = group.Id, Content = "Удалить", MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(remove, "Удалить подписку «" + group.Name + "»");
                remove.Click += async (_, _) =>
                {
                    list.IsEnabled = false;
                    try
                    {
                        if (!await ConfirmAsync(window, group)) return;
                        await MoskomLauncherRuntime.DeleteManualSubscriptionAsync(group.Id);
                        status.Text = "Подписка удалена из приложения.";
                        await Refresh();
                    }
                    catch (InvalidOperationException e) { status.Text = e.Message; }
                    catch { status.Text = "Не удалось удалить подписку. Повторите попытку."; }
                    finally { list.IsEnabled = true; }
                };
                row.Children.Add(remove);
                list.Children.Add(new Border { Child = row, Background = Brush.Parse("#151725"), CornerRadius = new(12), Padding = new(14) });
            }
        }
        await Refresh(); await window.ShowDialog(owner);
    }
    private static async Task<bool> ConfirmAsync(Window owner, MoskomSubscriptionGroup group)
    {
        var panel = new StackPanel { Margin = new(24), Spacing = 14 };
        panel.Children.Add(MoskomBrand.Heading("Удалить подписку?"));
        panel.Children.Add(Text("«" + group.Name + "» и все её серверы будут удалены из приложения. Активное подключение этой подписки будет отключено. Для повторного добавления понадобится ссылка-подписка."));
        var remove = new Button { Name = "MoskomConfirmSubscriptionDelete", Content = "Удалить подписку", MinHeight = 42, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brush.Parse("#8B303D"), Foreground = Brushes.White };
        var cancel = new Button { Name = "MoskomCancelSubscriptionDelete", Content = "Отмена", MinHeight = 42, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(remove); panel.Children.Add(cancel);
        var window = new Window { Title = "Удалить подписку? · МосКомпРемонт", Icon = MoskomBrand.Icon(), Width = 460, Height = 360, MinWidth = 380, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = MoskomBrand.Background, Content = new ScrollViewer { Content = panel } };
        MoskomBrand.Style(window.Styles); remove.Click += (_, _) => window.Close(true); cancel.Click += (_, _) => window.Close(false);
        return await window.ShowDialog<bool>(owner);
    }
}
