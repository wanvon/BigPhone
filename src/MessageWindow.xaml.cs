using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BigPhone;

/// <summary>One text message, full screen, with a big COPY CODE button for one-time passcodes.</summary>
public partial class MessageWindow : Window
{
    readonly TextMessage _msg;
    readonly string _who;
    readonly Action<string> _say;

    /// <summary>True when the user pressed CALL; the caller then dials <see cref="TextMessage.From"/>.</summary>
    public bool CallBackRequested { get; private set; }

    public MessageWindow(TextMessage msg, string who, Action<string> say)
    {
        InitializeComponent();
        _msg = msg;
        _who = who;
        _say = say;

        HeaderText.Text = $"💬 {who}   ·   {msg.Time:ddd h:mm tt}";
        BodyText.Text = msg.Text;

        string code = msg.Code;
        if (code.Length > 0)
        {
            CodeText.Text = $"CODE:  {Spaced(code)}";
            CopyCodeText.Text = $"📋 COPY CODE";
        }
        else
        {
            CodeRow.Height = new GridLength(0);
            CodeBanner.Visibility = Visibility.Collapsed;
            CopyCodeColumn.Width = new GridLength(0);
            CopyCodeButton.Visibility = Visibility.Collapsed;
        }

        // Short codes (5-6 digit bank/2FA senders) can't take calls.
        if (PhoneSettings.DigitsOnly(msg.From).Length < 7)
        {
            CallBackColumn.Width = new GridLength(0);
            CallBackButton.Visibility = Visibility.Collapsed;
        }
    }

    // "482913" -> "482 913", easier to read and type.
    static string Spaced(string code) => code.Length switch
    {
        6 => $"{code[..3]} {code[3..]}",
        8 => $"{code[..4]} {code[4..]}",
        _ => code,
    };

    void Body_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Large, but long texts get a little smaller so most fit without scrolling.
        double h = BodyScroller.ActualHeight;
        double size = _msg.Text.Length > 300 ? h / 9 : _msg.Text.Length > 120 ? h / 7 : h / 5;
        BodyText.FontSize = Math.Clamp(size, 28, 110);
    }

    string Audit => $"text from {_msg.From} sent {_msg.Time:yyyy-MM-dd HH:mm}";

    void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (CopyToClipboard(_msg.Code))
        {
            Log.Write($"Code copied to clipboard ({Audit})");
            Flash(CopyCodeText, "✔ COPIED!", "📋 COPY CODE");
            _say("Code copied. You can paste it now.");
        }
    }

    void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (CopyToClipboard(_msg.Text))
        {
            Log.Write($"Text copied to clipboard ({Audit})");
            Flash(CopyTextText, "✔ COPIED!", "📋 COPY TEXT");
        }
    }

    void Read_Click(object sender, RoutedEventArgs e)
    {
        Log.Write($"Text read aloud ({Audit})");
        string code = _msg.Code;
        _say($"Text from {_who}. {_msg.Text}" + (code.Length > 0 ? $". The code is {string.Join(" ", code.ToCharArray())}." : ""));
    }

    void CallBack_Click(object sender, RoutedEventArgs e)
    {
        Log.Write($"Call back pressed ({Audit})");
        CallBackRequested = true;
        DialogResult = true;
    }

    void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && _msg.Code.Length > 0)
        {
            CopyCode_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    static void Flash(TextBlock t, string now, string after)
    {
        t.Text = now;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { timer.Stop(); t.Text = after; };
        timer.Start();
    }

    /// <summary>The clipboard can be briefly locked by other apps (e.g. clipboard managers); retry a few times.</summary>
    public static bool CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch { Thread.Sleep(60); }
        }
        Log.Write("Copy to clipboard failed: clipboard busy");
        return false;
    }
}
