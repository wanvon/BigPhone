using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace BigPhone;

/// <summary>
/// Colour themes. Every themeable colour is an application resource used through {DynamicResource …},
/// so switching updates all open windows immediately.
///   Classic – the original black and grey.
///   Dark    – blue and gold (navy background, blue keys, gold CONTACTS, slate RECENTS/TEXTS).
///   Light   – the same blue and gold on a light background.
///   Auto    – Light or Dark, following the Windows "app mode" setting.
/// Call / hang-up green and red, the missed/new badges and status-bar colours keep their meaning in every theme.
/// </summary>
public static class Theme
{
    public static readonly string[] Names = { "Classic", "Dark", "Light", "Auto" };

    public static string Current { get; private set; } = "Classic";
    public static bool IsDark { get; private set; } = true;

    // key -> (Classic, Dark blue & gold, Light blue & gold)
    static readonly Dictionary<string, (string classic, string dark, string light)> Palette = new()
    {
        ["Bg"]          = ("#101010", "#112A55", "#EEF1F6"),   // main / list / message window background
        ["Surface"]     = ("#1A1A1A", "#0E2347", "#F7F8FB"),   // menu, settings, contacts editor
        ["Fg"]          = ("#FFFFFF", "#FFFFFF", "#111111"),   // text on Bg / Surface
        ["SubtleFg"]    = ("#BBBBBB", "#C9D3E3", "#4A5568"),   // hints and help text
        ["HeadingFg"]   = ("#FFD600", "#CB9B49", "#74531C"),   // section headings in Settings
        ["KeyBg"]       = ("#2E2E2E", "#1952B3", "#FFFFFF"),   // keypad keys and list arrows
        ["KeyFg"]       = ("#FFFFFF", "#FFFFFF", "#112A55"),
        ["Border"]      = ("#5A5A5A", "#4A6FB5", "#96A4BB"),   // big-button borders
        ["Accent"]      = ("#FFD600", "#FFFFFF", "#1952B3"),   // hover / focus outline, cursor
        ["NeutralBg"]   = ("#3A3A3A", "#96A4BB", "#96A4BB"),   // RECENTS, TEXTS, MUTE, CLEAR, COPY TEXT, READ
        ["NeutralFg"]   = ("#FFFFFF", "#111111", "#111111"),
        ["ContactsBg"]  = ("#1E4C9A", "#CB9B49", "#CB9B49"),   // CONTACTS button and contact rows
        ["ContactsFg"]  = ("#FFFFFF", "#111111", "#111111"),
        ["GearBg"]      = ("#33000000", "#74531C", "#74531C"),
        ["GearFg"]      = ("#FFFFFF", "#FFFFFF", "#FFFFFF"),
        ["ListBg"]      = ("#262626", "#0A1C3D", "#FFFFFF"),   // contacts editor list
        ["FieldBorder"] = ("#00000000", "#00000000", "#96A4BB"),// number field outline (needed on a light page)
        ["OkFg"]        = ("#90EE90", "#8FE3A5", "#1B6E2E"),
        ["ErrorFg"]     = ("#FF8A80", "#FF9C8F", "#B3261E"),
        ["LinkFg"]      = ("#87CEFA", "#9CC3FF", "#1952B3"),
    };

    public static Brush Get(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    /// <summary>Applies a theme by name ("Auto" resolves to Light or Dark from Windows).</summary>
    public static void Apply(string name)
    {
        if (!Names.Contains(name)) name = "Classic";
        Current = name;
        string effective = name == "Auto" ? (WindowsUsesLightApps() ? "Light" : "Dark") : name;
        IsDark = effective != "Light";

        var res = Application.Current.Resources;
        foreach (var (key, (classic, dark, light)) in Palette)
        {
            string hex = effective switch { "Dark" => dark, "Light" => light, _ => classic };
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            res[key] = brush;
        }
        foreach (Window w in Application.Current.Windows) SetTitleBar(w);
    }

    static bool _watching;

    /// <summary>For "Auto": re-apply when the Windows light/dark setting changes.</summary>
    public static void WatchWindowsSetting()
    {
        if (_watching) return;
        _watching = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Current == "Auto")
                Application.Current?.Dispatcher.BeginInvoke(() => Apply("Auto"));
        };
    }

    static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    // ---- title bar: dark or light to match (Windows 10 20H1+ / Windows 11)

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void SetTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero)
        {
            w.SourceInitialized += (_, _) => SetTitleBar(w);
            return;
        }
        int dark = IsDark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); } catch { }   // DWMWA_USE_IMMERSIVE_DARK_MODE
    }
}
