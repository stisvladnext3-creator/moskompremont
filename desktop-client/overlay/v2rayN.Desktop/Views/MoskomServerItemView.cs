using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text;
using ServiceLib.Services;

namespace v2rayN.Desktop.Views;

internal static class MoskomServerItemView
{
    public static Control Create(ComboItem? item)
    {
        if (!MoskomServerDisplay.TrySplit(item?.Text, out var protocol, out var country, out var name))
            return new TextBlock { Text = item?.Text, TextTrimming = TextTrimming.CharacterEllipsis };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Flag(country));
        row.Children.Add(new TextBlock { Text = $"{protocol} {name}", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static Control Flag(string country)
    {
        var grid = new Grid { Width = 24, Height = 16, ClipToBounds = true };
        switch (country)
        {
            case "by": Bands(grid, "#C8313E", "#FFD700", "#2F8A3B"); break;
            case "de": Bands(grid, "#111111", "#DD0000", "#FFCE00"); break;
            case "ee": Bands(grid, "#4891D9", "#000000", "#FFFFFF"); break;
            case "es": Bands(grid, "#AA151B", "#F1BF00", "#AA151B"); break;
            case "fi": Bands(grid, "#FFFFFF", "#FFFFFF", "#FFFFFF"); Cross(grid, "#003580"); break;
            case "fr": VerticalBands(grid, "#0055A4", "#FFFFFF", "#EF4135"); break;
            case "gb": Bands(grid, "#1D3B8F", "#1D3B8F", "#1D3B8F"); Cross(grid, "#FFFFFF"); Cross(grid, "#CF142B", true); break;
            case "jo": Bands(grid, "#000000", "#FFFFFF", "#007A3D"); break;
            case "nl": Bands(grid, "#AE1C28", "#FFFFFF", "#21468B"); break;
            case "ru": Bands(grid, "#FFFFFF", "#0039A6", "#D52B1E"); break;
            default: return EmojiFlag(country);
        }
        return new Border { Child = grid, Width = 24, Height = 16, CornerRadius = new CornerRadius(2), BorderBrush = Brush.Parse("#8090AA"), BorderThickness = new Thickness(0.5) };
    }

    private static void Bands(Grid grid, params string[] colors)
    {
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        for (var i = 0; i < colors.Length; i++) { var band = new Border { Background = Brush.Parse(colors[i]) }; Grid.SetRow(band, i); grid.Children.Add(band); }
    }

    private static void VerticalBands(Grid grid, params string[] colors)
    {
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (var i = 0; i < colors.Length; i++) { var band = new Border { Background = Brush.Parse(colors[i]) }; Grid.SetColumn(band, i); grid.Children.Add(band); }
    }

    private static void Cross(Grid grid, string color, bool narrow = false)
    {
        grid.Children.Add(new Border { Background = Brush.Parse(color), Height = narrow ? 3 : 5, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(new Border { Background = Brush.Parse(color), Width = narrow ? 3 : 5, HorizontalAlignment = HorizontalAlignment.Center });
    }

    private static TextBlock EmojiFlag(string country)
    {
        var flag = new StringBuilder(4);
        foreach (var letter in country) flag.Append(char.ConvertFromUtf32(0x1F1E6 + letter - 'a'));
        return new TextBlock { Text = flag.ToString(), FontFamily = new FontFamily(OperatingSystem.IsWindows() ? "Segoe UI Emoji" : "Apple Color Emoji"), FontSize = 18, Width = 26, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }
}
