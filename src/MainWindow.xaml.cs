using System.Diagnostics;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BigPhone;

public partial class MainWindow : Window
{
    enum UiState { Idle, Dialing, InCall, Incoming }

    static readonly Brush ReadyBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0x2E));
    static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x1F, 0x1F));
    static readonly Brush BusyBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x5A, 0x00));
    static readonly Brush TalkBrush = new SolidColorBrush(Color.FromRgb(0x0D, 0x3B, 0x66));
    static readonly Brush IncomingA = new SolidColorBrush(Color.FromRgb(0x0D, 0x3B, 0x66));
    static readonly Brush IncomingB = new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0x2E));
    static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    static readonly Brush UnmutedBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    static readonly Brush RecentsBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

    readonly PhoneSettings _settings;
    readonly SipPhone _phone;
    Ringer _ringer;
    Window? _helperWindow;   // menu, contacts or settings: at most one open at a time
    readonly SpeechSynthesizer? _voice;
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _flash = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly DispatcherTimer _autoAnswer = new();
    readonly DispatcherTimer _resetStatus = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    readonly DateTime _startedAt = DateTime.Now;
    DateTime _lastUpdateCheck = DateTime.MinValue;

    UiState _state = UiState.Idle;
    string _number = "";
    string _talkingTo = "";
    DateTime _callStart;
    DateTime _lastBigAction = DateTime.MinValue;
    bool _registered;
    string _regMessage = "Connecting…";
    bool _confirmedClose;

    public MainWindow(PhoneSettings settings, string password)
    {
        InitializeComponent();
        _settings = settings;
        Title = $"Big Phone {Updater.CurrentVersionText}";

        ReloadSpeedDials();
        _ringer = new Ringer(SipPhone.FindOutputDevice(_settings.RingDevice));
        try { _voice = new SpeechSynthesizer { Volume = 100, Rate = -1 }; } catch { _voice = null; }

        _clock.Tick += (_, _) => UpdateCallClock();
        _flash.Tick += (_, _) => IncomingScreen.Background = IncomingScreen.Background == IncomingA ? IncomingB : IncomingA;
        _autoAnswer.Tick += (_, _) => { _autoAnswer.Stop(); if (_state == UiState.Incoming) _ = AnswerAsync(); };
        _resetStatus.Tick += (_, _) => { _resetStatus.Stop(); if (_state == UiState.Idle) ShowIdleStatus(); };

        _phone = new SipPhone(_settings, password);
        _phone.RegistrationChanged += (ok, msg) => Ui(() => { _registered = ok; _regMessage = msg; if (_state == UiState.Idle && !_resetStatus.IsEnabled) ShowIdleStatus(); });
        _phone.IncomingCall += (num, name) => Ui(() => OnIncoming(num, name));
        _phone.IncomingCancelled += () => Ui(() => { if (_state == UiState.Incoming) EndCallUi("Missed call"); });
        _phone.CallProgress += msg => Ui(() => { if (_state == UiState.Dialing) SetStatus($"{msg}  {_talkingTo}", BusyBrush); });
        _phone.CallAnswered += () => Ui(OnCallConnected);
        _phone.CallEnded += reason => Ui(() => { if (_state != UiState.Idle) EndCallUi(reason); });

        UpdateNumber();
        ShowIdleStatus();
        UpdateButtons();
        UpdateRecentsBadge();
        UpdateTextsBadge();
        StartTextWatcher();
        _phone.Start();

        _updateTimer.Tick += async (_, _) => await UpdateTickAsync(false);
        _updateTimer.Start();
        Loaded += async (_, _) => await UpdateTickAsync(false);
    }

    // ================================================================ updates

    /// <summary>
    /// Checks GitHub once a day and downloads new versions in the background. A downloaded update is installed
    /// (app restarts in ~2 s) only when the phone is idle AND either the app just started or it is 2-5 AM.
    /// </summary>
    async Task UpdateTickAsync(bool userAsked)
    {
        if (!_settings.AutoUpdate && !userAsked) return;
        if (userAsked || (DateTime.Now - _lastUpdateCheck).TotalHours >= 24)
        {
            _lastUpdateCheck = DateTime.Now;
            var info = await Updater.CheckAsync(_settings.UpdateRepo);
            if (info != null) await Updater.DownloadAsync(_settings.UpdateRepo, info);
        }

        if (Updater.StagedVersion() == null || _state != UiState.Idle) return;
        bool justStarted = (DateTime.Now - _startedAt).TotalMinutes < 2;
        bool night = DateTime.Now.Hour is >= 2 and < 5;
        if (userAsked || justStarted || night) InstallUpdateNow();
    }

    void InstallUpdateNow()
    {
        if (_state != UiState.Idle) return;
        SetStatus("Updating… one moment", BusyBrush);
        _phone.Dispose();
        _ringer.Dispose();
        if (!Updater.ApplyStagedAndRestart())
        {
            // couldn't update: restart normally so the phone keeps working
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--after-update") { UseShellExecute = false });
        }
        _confirmedClose = true;
        Application.Current.Shutdown();
    }

    static void Ui(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    // ================================================================ status

    void SetStatus(string text, Brush bg)
    {
        StatusText.Text = text;
        StatusBar.Background = bg;
    }

    void ShowIdleStatus()
    {
        if (_registered) SetStatus("✔ Ready to call", ReadyBrush);
        else SetStatus("✖ " + _regMessage, ErrorBrush);
    }

    void FlashStatus(string text, Brush bg)
    {
        SetStatus(text, bg);
        _resetStatus.Stop();
        _resetStatus.Start();
    }

    void Say(string text)
    {
        try { _voice?.SpeakAsyncCancelAll(); _voice?.SpeakAsync(text); } catch { }
    }

    void UpdateNumber()
    {
        NumberText.Text = FormatNumber(_number);
        NumberHint.Visibility = _number.Length == 0 && _state == UiState.Idle ? Visibility.Visible : Visibility.Collapsed;
    }

    static string FormatNumber(string n)
    {
        string d = PhoneSettings.DigitsOnly(n);
        if (d != n) return n;   // has * # or + : show as typed
        if (d.Length == 10) return $"{d[..3]} {d[3..6]} {d[6..]}";
        if (d.Length == 11 && d[0] == '1') return $"1 {d[1..4]} {d[4..7]} {d[7..]}";
        return n;
    }

    void UpdateButtons()
    {
        bool idle = _state == UiState.Idle;
        CallButton.IsEnabled = idle;
        CallButtonText.Text = idle && _number.Length == 0 && _settings.LastNumber.Length > 0 ? "↻ REDIAL" : "📞 CALL";
        HangUpText.Text = idle ? "CLEAR" : "HANG UP";
        HangUpButton.Background = idle ? UnmutedBrush : MutedBrush;
        MuteButton.IsEnabled = _state == UiState.InCall;
        MuteText.Text = _phone?.IsMuted == true ? "UNMUTE" : "MUTE";
        MuteButton.Background = _phone?.IsMuted == true ? MutedBrush : UnmutedBrush;
        ContactsButton.IsEnabled = idle;
        TextsButton.IsEnabled = idle;
        RecentsButton.IsEnabled = idle;
    }

    void UpdateCallClock()
    {
        if (_state != UiState.InCall) return;
        var t = DateTime.Now - _callStart;
        string muted = _phone.IsMuted ? "   (MUTED)" : "";
        SetStatus($"Talking to {_talkingTo}   {t:mm\\:ss}{muted}", TalkBrush);
    }

    // Guards against shaky double-taps starting two actions.
    bool Debounced()
    {
        if ((DateTime.Now - _lastBigAction).TotalMilliseconds < 1200) return true;
        _lastBigAction = DateTime.Now;
        return false;
    }

    // ================================================================ actions

    void PressKey(char k)
    {
        if (_state == UiState.Incoming || _state == UiState.Dialing) return;
        if (_number.Length >= 24) return;
        _number += k;
        UpdateNumber();
        UpdateButtons();
        if (_state == UiState.InCall) _ = _phone.SendDtmfAsync(k);
    }

    void Backspace()
    {
        if (_state != UiState.Idle || _number.Length == 0) return;
        _number = _number[..^1];
        UpdateNumber();
        UpdateButtons();
    }

    async Task StartCallAsync(string number, string displayName)
    {
        string dial = _settings.NormalizeForDialing(number);
        if (dial.Length < 3)
        {
            FlashStatus("Type a number first", ErrorBrush);
            Say("Type a number first");
            return;
        }
        if (!_registered)
        {
            FlashStatus("Phone is not connected", ErrorBrush);
            Say("The phone is not connected");
            return;
        }

        _settings.LastNumber = number;
        try { _settings.Save(); } catch { }

        _state = UiState.Dialing;
        BeginRecord(false, number, displayName);
        _talkingTo = string.IsNullOrEmpty(displayName) ? FormatNumber(PhoneSettings.DigitsOnly(number).Length > 0 ? number : dial) : displayName;
        SetStatus($"Calling {_talkingTo}…", BusyBrush);
        if (_settings.SpeakStatus) Say($"Calling {_talkingTo}");
        UpdateButtons();
        await _phone.CallAsync(dial);
    }

    void OnCallConnected()
    {
        _ringer.Stop();
        _flash.Stop();
        _autoAnswer.Stop();
        IncomingScreen.Visibility = Visibility.Collapsed;
        _state = UiState.InCall;
        _callStart = DateTime.Now;
        _callConnectedAt = _callStart;
        _number = "";
        UpdateNumber();
        UpdateCallClock();
        _clock.Start();
        UpdateButtons();
    }

    void EndCallUi(string reason)
    {
        FinishRecord(reason);
        _ringer.Stop();
        _flash.Stop();
        _clock.Stop();
        _autoAnswer.Stop();
        IncomingScreen.Visibility = Visibility.Collapsed;
        _state = UiState.Idle;
        _number = "";
        UpdateNumber();
        UpdateButtons();
        FlashStatus(reason, reason == "Call ended" ? BusyBrush : ErrorBrush);
        if (_settings.SpeakStatus) Say(reason);
    }

    void OnIncoming(string number, string sipName)
    {
        _state = UiState.Incoming;
        CloseHelperWindow();
        string known = _settings.NameForNumber(number);
        string name = known.Length > 0 ? known : sipName;
        string pretty = FormatNumber(PhoneSettings.DigitsOnly(number).Length > 0 ? PhoneSettings.DigitsOnly(number) : number);
        _talkingTo = name.Length > 0 ? name : pretty;
        BeginRecord(true, number, name);
        CallerText.Text = name.Length > 0 && name != pretty ? $"{name}\n{pretty}" : pretty;

        IncomingScreen.Visibility = Visibility.Visible;
        _flash.Start();
        UpdateButtons();

        // Bring the window to the front even if minimized or behind other windows.
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Maximized;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;

        if (_settings.SpeakCallerName)
        {
            Say($"Call from {(name.Length > 0 ? name : SpellDigits(number))}");
            // start ringing after the announcement
            var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            delay.Tick += (_, _) => { delay.Stop(); if (_state == UiState.Incoming) _ringer.Start(); };
            delay.Start();
        }
        else
        {
            _ringer.Start();
        }

        if (_settings.AutoAnswerSeconds > 0)
        {
            _autoAnswer.Interval = TimeSpan.FromSeconds(_settings.AutoAnswerSeconds);
            _autoAnswer.Start();
        }
    }

    static string SpellDigits(string n)
    {
        string d = PhoneSettings.DigitsOnly(n);
        if (d.Length == 11 && d[0] == '1') d = d[1..];
        return d.Length == 0 ? "unknown number" : string.Join(" ", d.ToCharArray());
    }

    async Task AnswerAsync()
    {
        if (_state != UiState.Incoming) return;
        _ringer.Stop();
        _voice?.SpeakAsyncCancelAll();
        _flash.Stop();
        SetStatus("Connecting…", BusyBrush);
        await _phone.AnswerAsync();
    }

    void Decline()
    {
        if (_state != UiState.Incoming) return;
        _phone.Decline();
        EndCallUi("Call declined");
    }

    void HangUpOrClear()
    {
        switch (_state)
        {
            case UiState.Idle:
                _number = "";
                UpdateNumber();
                UpdateButtons();
                break;
            case UiState.Incoming:
                Decline();
                break;
            default:
                _phone.HangUp();
                EndCallUi("Call ended");
                break;
        }
    }

    // ================================================================ event handlers

    void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string t } && t.Length == 1) PressKey(t[0]);
    }

    void Backspace_Click(object sender, RoutedEventArgs e) => Backspace();

    async void Call_Click(object sender, RoutedEventArgs e)
    {
        if (_state != UiState.Idle || Debounced()) return;
        if (_number.Length == 0 && _settings.LastNumber.Length > 0)
        {
            // first press shows the last number, second press calls it
            _number = _settings.LastNumber;
            UpdateNumber();
            UpdateButtons();
            return;
        }
        await StartCallAsync(_number, _settings.NameForNumber(_number));
    }

    async void Contacts_Click(object sender, RoutedEventArgs e)
    {
        if (_state != UiState.Idle) return;
        var items = _contacts.Select(c => new PickItem
        {
            Title = c.Name, Detail = FormatNumber(c.Number), Number = c.Number, CallName = c.Name, Background = PickItem.Blue
        });
        await PickAndCallAsync("Who do you want to call?", items, "No contacts yet. Your helper can add them with ⚙ → Contacts.");
    }

    async void Recents_Click(object sender, RoutedEventArgs e)
    {
        if (_state != UiState.Idle) return;
        RecentCalls.MarkSeen();
        UpdateRecentsBadge();
        var items = RecentCalls.Calls.Select(r =>
        {
            string name = r.Name.Length > 0 ? r.Name : _settings.NameForNumber(r.Number);
            return new PickItem
            {
                Title = name.Length > 0 ? name : FormatNumber(r.Number),
                Detail = RecentCalls.Describe(r),
                Number = r.Number,
                CallName = name,
                Background = r.Kind is CallKind.Missed or CallKind.Declined ? PickItem.Red
                           : r.Kind == CallKind.Incoming ? PickItem.Blue : PickItem.Gray,
            };
        });
        await PickAndCallAsync("Recent calls – tap to call back", items, "No calls yet.");
    }

    /// <summary>Shows the full-screen list; if a row is tapped, calls it.</summary>
    PickItem? PickFromList(string header, IEnumerable<PickItem> items, string empty)
    {
        CloseHelperWindow();
        var w = new PickerWindow(header, items, empty) { Owner = this };
        _helperWindow = w;   // an incoming call closes it
        bool? ok = w.ShowDialog();
        if (_helperWindow == w) _helperWindow = null;
        return ok == true ? w.Chosen : null;
    }

    async Task PickAndCallAsync(string header, IEnumerable<PickItem> items, string empty)
    {
        if (PickFromList(header, items, empty) is { } c && _state == UiState.Idle)
        {
            _number = c.Number;
            UpdateNumber();
            await StartCallAsync(c.Number, c.CallName);
        }
    }

    // ---- text messages

    TextWatcher? _texts;
    static readonly Brush NewTextBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x8E, 0x3A));

    void StartTextWatcher()
    {
        _texts?.Dispose();
        _texts = null;
        string pw = _settings.GetImapPassword();
        if (!_settings.TextsEnabled || _settings.ImapUser.Length == 0 || pw.Length == 0) return;
        _texts = new TextWatcher(_settings, pw);
        _texts.NewTexts += msgs => Ui(() => OnNewTexts(msgs));
        _texts.Start();
    }

    void UpdateTextsBadge()
    {
        bool on = _settings.TextsEnabled;
        TextsButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TextsRow.Height = on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        int unread = TextStore.Unread;
        TextsText.Text = unread == 1 ? "1 NEW TEXT" : unread > 1 ? $"{unread} NEW TEXTS" : "TEXTS";
        TextsButton.Background = unread > 0 ? NewTextBrush : RecentsBrush;
    }

    string WhoSent(TextMessage m)
    {
        string name = _settings.NameForNumber(m.From);
        if (name.Length > 0) return name;
        string d = PhoneSettings.DigitsOnly(m.From);
        return d.Length >= 10 ? FormatNumber(d) : m.From;
    }

    void OnNewTexts(List<TextMessage> msgs)
    {
        UpdateTextsBadge();
        var newest = msgs.OrderByDescending(m => m.Time).First();
        if (_state != UiState.Idle) return;   // never interrupt a call; the green TEXTS button shows it

        if (_settings.SpeakNewTexts)
        {
            string code = newest.Code;
            Say($"New text from {WhoSent(newest)}." + (code.Length > 0 ? $" The code is {string.Join(" ", code.ToCharArray())}." : ""));
        }
        if (_settings.PopUpNewTexts && _helperWindow == null)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Maximized;
            Activate();
            _ = ShowMessage(newest);
        }
    }

    async void Texts_Click(object sender, RoutedEventArgs e)
    {
        if (_state != UiState.Idle) return;
        var items = TextStore.Messages.Select(m =>
        {
            string preview = m.Text.Replace('\n', ' ');
            if (preview.Length > 45) preview = preview[..45] + "…";
            return new PickItem
            {
                Title = (m.Read ? "" : "● ") + WhoSent(m),
                Detail = $"{m.Time:ddd h:mm tt}  ·  {preview}",
                Background = m.Read ? PickItem.Gray : PickItem.Green,
                Tag = m,
            };
        });
        var chosen = PickFromList("Text messages – tap to read", items,
            _settings.TextsEnabled ? "No texts yet." : "Text messages are not set up. Your helper can turn them on in ⚙ → Settings.");
        if (chosen?.Tag is TextMessage msg) await ShowMessage(msg);
    }

    async Task ShowMessage(TextMessage msg)
    {
        TextStore.MarkRead(msg);
        UpdateTextsBadge();
        CloseHelperWindow();
        var w = new MessageWindow(msg, WhoSent(msg), Say) { Owner = this };
        _helperWindow = w;   // an incoming call closes it
        w.ShowDialog();
        if (_helperWindow == w) _helperWindow = null;
        if (w.CallBackRequested && _state == UiState.Idle)
        {
            _number = msg.From;
            UpdateNumber();
            await StartCallAsync(msg.From, _settings.NameForNumber(msg.From));
        }
    }

    void UpdateRecentsBadge()
    {
        int missed = RecentCalls.UnseenMissed;
        RecentsText.Text = missed > 0 ? $"{missed} MISSED" : "RECENTS";
        RecentsButton.Background = missed > 0 ? MutedBrush : RecentsBrush;
    }

    // ---- call history

    bool _recording;
    bool _callIncoming;
    string _callNumber = "", _callName = "";
    DateTime _callWhen;
    DateTime? _callConnectedAt;

    void BeginRecord(bool incoming, string number, string name)
    {
        _recording = true;
        _callIncoming = incoming;
        _callNumber = number;
        _callName = name;
        _callWhen = DateTime.Now;
        _callConnectedAt = null;
    }

    void FinishRecord(string reason)
    {
        if (!_recording) return;
        _recording = false;
        var kind = _callIncoming
            ? (_callConnectedAt != null ? CallKind.Incoming : reason == "Call declined" ? CallKind.Declined : CallKind.Missed)
            : (_callConnectedAt != null ? CallKind.Outgoing : CallKind.NotConnected);
        int secs = _callConnectedAt is { } t ? (int)(DateTime.Now - t).TotalSeconds : 0;
        RecentCalls.Add(new CallRecord { Time = _callWhen, Number = _callNumber, Name = _callName, Kind = kind, Seconds = secs });
        UpdateRecentsBadge();
    }

    void HangUp_Click(object sender, RoutedEventArgs e) => HangUpOrClear();

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_state != UiState.InCall) return;
        _phone.SetMute(!_phone.IsMuted);
        UpdateButtons();
        UpdateCallClock();
    }

    async void Answer_Click(object sender, RoutedEventArgs e) => await AnswerAsync();

    void Decline_Click(object sender, RoutedEventArgs e) => Decline();

    // ================================================================ helper windows (⚙)

    List<SpeedDial> _contacts = new();

    void ReloadSpeedDials()
    {
        _contacts = _settings.SpeedDials
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Number))
            .ToList();
    }

    /// <summary>Helper windows are not modal, so an incoming call can always take over the screen.</summary>
    void ShowHelperWindow(Window w)
    {
        CloseHelperWindow();
        w.Owner = this;
        w.Closed += (_, _) => { if (_helperWindow == w) _helperWindow = null; };
        _helperWindow = w;
        w.Show();
    }

    void CloseHelperWindow()
    {
        var w = _helperWindow;
        _helperWindow = null;
        if (w is ContactsWindow cw) cw.CloseForIncomingCall();
        else w?.Close();
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        string mode = PhoneSettings.IsPortable ? "portable" : "installed";
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Big Phone {Updater.CurrentVersionText}  ({mode})", FontSize = 18, Foreground = Brushes.LightGray,
            Margin = new Thickness(6, 0, 6, 10)
        });
        var menu = new Window
        {
            Title = "Big Phone – Menu", Content = panel, Width = 460, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), ShowInTaskbar = false
        };

        void Item(string text, string color, Action action)
        {
            var b = new Button { Content = text, Style = (Style)FindResource("DlgButton"), Background = (Brush)new BrushConverter().ConvertFrom(color)! };
            b.Click += (_, _) => { menu.Close(); action(); };
            panel.Children.Add(b);
        }

        Item("👤  Contacts", "#1E4C9A", OpenContacts);
        Item("🔧  Settings", "#3A3A3A", OpenSettings);
        Item("⟳  Check for updates", "#3A3A3A", async () => await CheckForUpdatesInteractiveAsync());
        Item("📝  Advanced (settings file)", "#3A3A3A", async () => await EditSettingsFileAsync());
        Item("Close", "#2A2A2A", () => { });
        ShowHelperWindow(menu);
    }

    /// <summary>
    /// Opens settings.json in Notepad and restarts Big Phone when Notepad is closed, so hand edits are
    /// picked up and never overwritten by this running copy. Never restarts in the middle of a call.
    /// </summary>
    async Task EditSettingsFileAsync()
    {
        if (MessageBox.Show(this,
                "Account, network and other settings are in this file.\n\n" +
                "Big Phone will restart when you close Notepad (after the call, if you are on one).\n" +
                "If you only want to change contacts or sound, use Contacts or Settings instead.\n\n" +
                $"Settings: {PhoneSettings.FilePath}\nLog: {PhoneSettings.LogPath}",
                "Big Phone", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;

        try { _settings.Save(); } catch { }   // the file should show what is in use right now
        using var notepad = Process.Start("notepad.exe", $"\"{PhoneSettings.FilePath}\"");
        if (notepad == null) return;
        await notepad.WaitForExitAsync();
        while (_state != UiState.Idle) await Task.Delay(1000);

        Log.Write("Settings file edited; restarting");
        _confirmedClose = true;
        App.Restart();
    }

    void OpenContacts()
    {
        var w = new ContactsWindow(_settings);
        w.Saved += ReloadSpeedDials;
        ShowHelperWindow(w);
    }

    void OpenSettings()
    {
        var w = new SettingsWindow(_settings);
        w.Saved += () =>
        {
            // Ring device takes effect now; audio devices are used from the next call.
            if (_state != UiState.Incoming)
            {
                _ringer.Dispose();
                _ringer = new Ringer(SipPhone.FindOutputDevice(_settings.RingDevice));
            }
            FlashStatus("✔ Settings saved", ReadyBrush);
            StartTextWatcher();
            UpdateTextsBadge();
        };
        ShowHelperWindow(w);
    }

    async Task CheckForUpdatesInteractiveAsync()
    {
        if (_state != UiState.Idle) { MessageBox.Show(this, "Please finish the call first.", "Big Phone"); return; }
        SetStatus("Checking for updates…", BusyBrush);
        var info = await Updater.CheckAsync(_settings.UpdateRepo);
        if (info != null && !await Updater.DownloadAsync(_settings.UpdateRepo, info))
        {
            ShowIdleStatus();
            MessageBox.Show(this, "The update could not be downloaded. See the log for details.", "Big Phone");
            return;
        }
        var staged = Updater.StagedVersion();
        if (staged == null)
        {
            ShowIdleStatus();
            MessageBox.Show(this, $"Big Phone {Updater.CurrentVersionText} is the newest version.", "Big Phone");
            return;
        }
        if (MessageBox.Show(this, $"Install version {staged} now? Big Phone will restart.", "Big Phone",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            InstallUpdateNow();
        else
            ShowIdleStatus();
    }

    async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case >= Key.D0 and <= Key.D9 when !shift:
                PressKey((char)('0' + (e.Key - Key.D0))); break;
            case >= Key.NumPad0 and <= Key.NumPad9:
                PressKey((char)('0' + (e.Key - Key.NumPad0))); break;
            case Key.D8 when shift:
            case Key.Multiply:
                PressKey('*'); break;
            case Key.D3 when shift:
                PressKey('#'); break;
            case Key.Back:
                Backspace(); break;
            case Key.Enter:
                if (_state == UiState.Incoming) await AnswerAsync();
                else if (_state == UiState.Idle) Call_Click(this, new RoutedEventArgs());
                break;
            case Key.Escape:
                HangUpOrClear(); break;
            case >= Key.F1 and <= Key.F6:
                int i = e.Key - Key.F1;
                if (i < _contacts.Count && _state == UiState.Idle && !Debounced())
                {
                    _number = _contacts[i].Number;
                    UpdateNumber();
                    await StartCallAsync(_contacts[i].Number, _contacts[i].Name);
                }
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_confirmedClose)
        {
            var r = MessageBox.Show(this, "Turn the phone off?\n\nYou will NOT get calls while it is off.",
                "Big Phone", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            _confirmedClose = true;
        }
        _ringer.Dispose();
        _voice?.Dispose();
        _texts?.Dispose();
        _phone.Dispose();
    }
}
