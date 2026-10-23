using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BigPhone;

/// <summary>One row in the picker: a person (contacts) or a call (recents).</summary>
public class PickItem
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Number { get; init; } = "";
    public string CallName { get; init; } = "";
    public Brush Background { get; init; } = Blue;
    public object? Tag { get; init; }   // e.g. the TextMessage behind a row

    public static readonly Brush Blue = Frozen(0x1E, 0x4C, 0x9A);
    public static readonly Brush Red = Frozen(0x8E, 0x1C, 0x1C);
    public static readonly Brush Gray = Frozen(0x2E, 0x2E, 0x2E);
    public static readonly Brush Green = Frozen(0x1B, 0x6E, 0x2E);

    static Brush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}

/// <summary>
/// Full-screen scrolling list of big buttons (contacts or recent calls). Tap a row to call it:
/// DialogResult is true and <see cref="Chosen"/> is set.
/// </summary>
public partial class PickerWindow : Window
{
    const double RowsPerScreen = 4.5;   // the half row shows there is more below

    public PickItem? Chosen { get; private set; }

    public PickerWindow(string header, IEnumerable<PickItem> items, string emptyMessage)
    {
        InitializeComponent();
        WindowMemory.Remember(this, "List");
        Title = $"Big Phone – {header}";
        HeaderText.Text = header;
        EmptyText.Text = emptyMessage;
        var list = items.ToList();
        List.ItemsSource = list;
        if (list.Count == 0)
        {
            Scroller.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
        }
        Resources["RowHeight"] = 120.0;
        Resources["NameSize"] = 48.0;
        Resources["NumberSize"] = 24.0;
    }

    void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Same size for every row, derived from the screen height.
        double row = Math.Max(90, Scroller.ActualHeight / RowsPerScreen - 16);
        Resources["RowHeight"] = row;
        Resources["NameSize"] = row * 0.42;
        Resources["NumberSize"] = row * 0.2;
        UpdateArrows();
    }

    void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateArrows();

    void UpdateArrows()
    {
        bool scrollable = Scroller.ScrollableHeight > 1;
        UpButton.IsEnabled = scrollable && Scroller.VerticalOffset > 1;
        DownButton.IsEnabled = scrollable && Scroller.VerticalOffset < Scroller.ScrollableHeight - 1;
    }

    void Page(int direction) =>
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + direction * Scroller.ViewportHeight * 0.8);

    void Up_Click(object sender, RoutedEventArgs e) => Page(-1);
    void Down_Click(object sender, RoutedEventArgs e) => Page(+1);

    void Item_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PickItem item })
        {
            Chosen = item;
            DialogResult = true;
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: DialogResult = false; break;
            case Key.Up: case Key.PageUp: Page(-1); break;
            case Key.Down: case Key.PageDown: Page(+1); break;
            default: return;
        }
        e.Handled = true;
    }
}
