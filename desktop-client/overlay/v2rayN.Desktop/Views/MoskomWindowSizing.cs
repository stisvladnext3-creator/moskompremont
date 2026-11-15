namespace v2rayN.Desktop.Views;

// Window geometry uses physical work-area pixels; client sizes use scaled DIP.
// Keep native title-bar controls on screen and let the existing content scroll.
internal static class MoskomWindowSizing
{
    public static void Attach(Window window)
    {
        Fit(window);
        window.Opened += (_, _) => Fit(window);
    }
    public static bool IsOnScreen(Window window)
    {
        var screen=window.Screens.ScreenFromWindow(window)??window.Screens.Primary;
        if(screen==null)return false;
        var frame=window.FrameSize??new Size(window.Width,window.Height);var area=screen.WorkingArea;
        return window.Position.X>=area.X&&window.Position.Y>=area.Y
            &&window.Position.X+frame.Width*screen.Scaling<=area.Right+2
            &&window.Position.Y+frame.Height*screen.Scaling<=area.Bottom+2;
    }
    public static void Fit(Window window)
    {
        if (window.WindowState != WindowState.Normal) return;
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null) return;
        var work = screen.WorkingArea; var scale = screen.Scaling;
        if (scale <= 0 || work.Width <= 0 || work.Height <= 0) return;
        // Before the first layout Bounds can be zero while the native frame
        // already has its requested size. That entire size is not decoration.
        var client=window.ClientSize;
        var chromeWidth = client.Width>0 ? Math.Max(16, (window.FrameSize?.Width ?? client.Width) - client.Width) : 16;
        var chromeHeight = client.Height>0 ? Math.Max(48, (window.FrameSize?.Height ?? client.Height) - client.Height) : 48;
        var maxWidth = Math.Max(1, work.Width / scale - chromeWidth - 4);
        var maxHeight = Math.Max(1, work.Height / scale - chromeHeight - 4);
        window.MinWidth = Math.Min(window.MinWidth, maxWidth); window.MinHeight = Math.Min(window.MinHeight, maxHeight);
        window.Width = Math.Clamp(double.IsFinite(window.Width) ? window.Width : window.Bounds.Width, window.MinWidth, maxWidth);
        window.Height = Math.Clamp(double.IsFinite(window.Height) ? window.Height : window.Bounds.Height, window.MinHeight, maxHeight);
        var width = (int)Math.Ceiling((window.Width + chromeWidth) * scale);
        var height = (int)Math.Ceiling((window.Height + chromeHeight) * scale);
        window.Position = new PixelPoint(Math.Clamp(window.Position.X, work.X, Math.Max(work.X, work.Right - width)),
            Math.Clamp(window.Position.Y, work.Y, Math.Max(work.Y, work.Bottom - height)));
    }
}
