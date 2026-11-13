using Avalonia.Styling;
using Avalonia.Layout;

namespace v2rayN.Desktop.Views;

public static class MoskomBrand
{
    public static IBrush Background => Gradient("#07070F", "#111529", "#160F27");
    public static IBrush Primary => Gradient("#36D5E8", "#7C8EFA", "#A271F5");
    public static IBrush Power => Gradient("#087A9D", "#3B5DD5", "#7041C9");
    private static IBrush Gradient(string a, string b, string c) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = [new(Color.Parse(a), 0), new(Color.Parse(b), .55), new(Color.Parse(c), 1)]
    };
    public static Bitmap Logo() => new(Avalonia.Platform.AssetLoader.Open(new Uri("avares://v2rayN/Assets/moskom-logo.png")));
    public static WindowIcon Icon() => new(Avalonia.Platform.AssetLoader.Open(new Uri("avares://v2rayN/Assets/moskom-vpn.ico")));
    public static void Style(Styles styles)
    {
        var button = new Avalonia.Styling.Style(s => s.OfType<Button>());
        button.Setters.Add(new Setter(Button.BackgroundProperty, Brush.Parse("#141522")));
        button.Setters.Add(new Setter(Button.ForegroundProperty, Brush.Parse("#DAE4FA")));
        button.Setters.Add(new Setter(Button.BorderBrushProperty, Brush.Parse("#34364E")));
        button.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        button.Setters.Add(new Setter(Button.CornerRadiusProperty, new CornerRadius(10)));
        styles.Add(button);
    }
    public static StackPanel Heading(string caption)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        panel.Children.Add(new Image { Source = Logo(), Width = 42, Height = 46 });
        panel.Children.Add(new TextBlock { Text = caption, FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#F0F4FF"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return panel;
    }
}
