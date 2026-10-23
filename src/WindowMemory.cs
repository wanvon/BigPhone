using System.Windows;

namespace BigPhone;

public class WindowPlace
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>
/// Remembers each Big Phone window's last size, position and maximized state (in settings.json, "Windows").
/// A window opens with its XAML defaults until it has been closed once.
/// </summary>
public static class WindowMemory
{
    public static void Remember(Window w, string key)
    {
        var s = App.Settings;
        if (s == null) return;

        if (s.Windows.TryGetValue(key, out var p) && OnScreen(p))
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = p.Left;
            w.Top = p.Top;
            if (w.SizeToContent == SizeToContent.Manual)
            {
                w.Width = p.Width;
                w.Height = p.Height;
            }
            w.WindowState = p.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        w.Closing += (_, e) =>
        {
            if (e.Cancel) return;   // e.g. "Turn the phone off?" answered No
            Rect r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight) : w.RestoreBounds;
            if (r.IsEmpty || r.Width < 50 || r.Height < 50) return;
            s.Windows[key] = new WindowPlace
            {
                Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height,
                Maximized = w.WindowState == WindowState.Maximized,
            };
            try { s.Save(); } catch { }
        };
    }

    /// <summary>At least a 100×100 corner of the window must be on some monitor, so it can't open lost off-screen.</summary>
    static bool OnScreen(WindowPlace p)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screen, new Rect(p.Left, p.Top, Math.Max(p.Width, 1), Math.Max(p.Height, 1)));
        return !visible.IsEmpty && visible.Width >= 100 && visible.Height >= 100;
    }
}
