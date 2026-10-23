using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NAudio.Wave;

namespace BigPhone;

public partial class SettingsWindow : Window
{
    const string DefaultDevice = "Windows default";

    readonly PhoneSettings _settings;
    Ringer? _test;
    readonly DispatcherTimer _stopTest = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>Raised after settings were saved, so the phone can pick them up without a restart.</summary>
    public event Action? Saved;

    public SettingsWindow(PhoneSettings settings)
    {
        InitializeComponent();
        WindowMemory.Remember(this, "Settings");
        _settings = settings;
        _stopTest.Tick += (_, _) => StopTest();
        Closed += (_, _) => StopTest();

        var outs = Enumerable.Range(0, WaveOut.DeviceCount).Select(i => WaveOut.GetCapabilities(i).ProductName).ToList();
        var ins = Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName).ToList();
        FillDevices(OutputBox, outs, settings.AudioOutputDevice);
        FillDevices(InputBox, ins, settings.AudioInputDevice);
        FillDevices(RingBox, outs, settings.RingDevice);

        SpeakCallerBox.IsChecked = settings.SpeakCallerName;
        SpeakStatusBox.IsChecked = settings.SpeakStatus;
        USCodeBox.IsChecked = settings.AddUSCountryCode;
        AutoUpdateBox.IsChecked = settings.AutoUpdate;
        KeepRunningBox.IsChecked = settings.KeepRunning;

        int[] choices = { 0, 3, 5, 10, 15, 20, 30 };
        foreach (int s in choices.Union(new[] { settings.AutoAnswerSeconds }).OrderBy(x => x))
            AutoAnswerBox.Items.Add(new ComboBoxItem { Content = s == 0 ? "Never" : $"After {s} seconds", Tag = s });
        AutoAnswerBox.SelectedItem = AutoAnswerBox.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == settings.AutoAnswerSeconds);

        // Text messages
        TextsEnabledBox.IsChecked = settings.TextsEnabled;
        PopUpTextsBox.IsChecked = settings.PopUpNewTexts;
        SpeakTextsBox.IsChecked = settings.SpeakNewTexts;
        ChimeTextsBox.IsChecked = settings.ChimeNewTexts;
        ImapUserBox.Text = settings.ImapUser;
        ImapHostBox.Text = settings.ImapHost;
        foreach (var p in Providers) ProviderBox.Items.Add(p.Name);
        int idx = settings.ImapAuth == "google" ? 0 : Array.FindIndex(Providers, p => !p.Google && p.Host == settings.ImapHost);
        if (idx < 0) idx = settings.ImapHost.Length > 0 ? Providers.Length - 1 : 0;   // unknown host = "Other"
        ProviderBox.SelectedIndex = idx;
        ImapHostBox.Text = settings.ImapHost;
        UpdateGoogleStatus();
        if (settings.ImapPasswordProtected.Length > 0)
            ImapPasswordLabel.Text = "App password (saved – leave empty to keep it)";

        InfoText.Text = $"Big Phone {Updater.CurrentVersionText}  ·  account {settings.Username}@{settings.Server}\n" +
                        $"Account and network settings: ⚙ → Advanced (settings file).";
    }

    record Provider(string Name, string Host, string Help, string Url = "", bool Google = false);

    const string Tip = " Tip: use a separate mailbox just for Big Phone (set up with the helper's phone), so the user never has to sign in to it.";

    static readonly Provider[] Providers =
    {
        new("Gmail – Sign in with Google (no app password)", "imap.gmail.com",
            "For Gmail accounts that can't make app passwords (e.g. accounts using passkeys). Needs a one-time " +
            "\"Desktop app\" OAuth client from your own Google Cloud project, with the Gmail scope on its consent screen " +
            "and publishing status \"In production\". See the README for step-by-step instructions.",
            "https://console.cloud.google.com/auth/clients", Google: true),
        new("Gmail", "imap.gmail.com",
            "Gmail: turn on 2-Step Verification, then create an app password and paste it here." + Tip,
            "https://myaccount.google.com/apppasswords"),
        new("Yahoo Mail", "imap.mail.yahoo.com",
            "Yahoo: Account Security → Generate app password." + Tip,
            "https://login.yahoo.com/myaccount/security/"),
        new("iCloud Mail", "imap.mail.me.com",
            "iCloud: Sign-In and Security → App-Specific Passwords. Use your @icloud.com address." + Tip,
            "https://account.apple.com/account/manage"),
        new("AOL Mail", "imap.aol.com",
            "AOL: Account Security → Generate app password." + Tip,
            "https://login.aol.com/account/security"),
        new("Other (IMAP)", "",
            "Type your provider's IMAP server name. Big Phone uses port 993 with encryption. Outlook.com / Hotmail no longer allow app passwords, so they can't be used."),
    };

    void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderBox.SelectedIndex < 0) return;
        var p = Providers[ProviderBox.SelectedIndex];
        ProviderHelp.Inlines.Clear();
        ProviderHelp.Inlines.Add(new System.Windows.Documents.Run(p.Help + " "));
        if (p.Url.Length > 0)
        {
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Open " + new Uri(p.Url).Host))
            {
                NavigateUri = new Uri(p.Url), Foreground = System.Windows.Media.Brushes.LightSkyBlue
            };
            link.RequestNavigate += (_, ev) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ev.Uri.AbsoluteUri) { UseShellExecute = true });
                ev.Handled = true;
            };
            ProviderHelp.Inlines.Add(link);
        }
        PasswordPanel.Visibility = p.Google ? Visibility.Collapsed : Visibility.Visible;
        GooglePanel.Visibility = p.Google ? Visibility.Visible : Visibility.Collapsed;
        bool other = p.Host.Length == 0;
        ImapHostBox.IsEnabled = other;
        if (!other) ImapHostBox.Text = p.Host;
    }

    bool GoogleSelected => ProviderBox.SelectedIndex >= 0 && Providers[ProviderBox.SelectedIndex].Google;

    void UpdateGoogleStatus()
    {
        GoogleClientStatus.Text = GoogleSignIn.HasClient(_settings) ? "✔ App ID loaded" : "No app ID yet";
        GoogleSignInStatus.Text = GoogleSignIn.IsSignedIn && _settings.ImapAuth == "google" && _settings.ImapUser.Length > 0
            ? $"✔ Signed in as {_settings.ImapUser}" : "Not signed in";
        GoogleSignInButton.IsEnabled = GoogleSignIn.HasClient(_settings);
    }

    void ChooseGoogleClient_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the client_secret_….json file downloaded from Google Cloud Console",
            Filter = "Google client file (*.json)|*.json",
            InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var (id, secret) = GoogleSignIn.ReadClientFile(dlg.FileName);
            _settings.GoogleClientId = id;
            _settings.SetGoogleClientSecret(secret);
            _settings.Save();
            Log.Write("Google app ID loaded");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Big Phone");
        }
        UpdateGoogleStatus();
    }

    async void GoogleSignIn_Click(object sender, RoutedEventArgs e)
    {
        GoogleSignInStatus.Text = "Finish signing in in your web browser…";
        GoogleSignInButton.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            string email = await GoogleSignIn.SignInAsync(_settings, cts.Token);
            if (email.Length == 0) throw new InvalidOperationException("Google didn't say which account was used.");
            _settings.ImapAuth = "google";
            _settings.ImapUser = email;
            _settings.ImapHost = "imap.gmail.com";
            _settings.Save();
            Activate();
        }
        catch (Exception ex)
        {
            GoogleSignInStatus.Text = "✖ " + ex.Message;
            GoogleSignInButton.IsEnabled = true;
            return;
        }
        UpdateGoogleStatus();
    }

    string ImapPasswordToUse() =>
        ImapPasswordBox.Password.Length > 0 ? ImapPasswordBox.Password.Replace(" ", "") : _settings.GetImapPassword();

    async void TestImap_Click(object sender, RoutedEventArgs e)
    {
        ImapTestResult.Foreground = System.Windows.Media.Brushes.LightGray;
        ImapTestResult.Text = "Connecting…";
        try
        {
            ImapTestResult.Text = GoogleSelected
                ? await TextWatcher.TestAsync(_settings, true, "imap.gmail.com", _settings.ImapPort, _settings.ImapUser, "")
                : await TextWatcher.TestAsync(_settings, false, ImapHostBox.Text.Trim(), _settings.ImapPort, ImapUserBox.Text.Trim(), ImapPasswordToUse());
            ImapTestResult.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            ImapTestResult.Text = "✖ " + ex.Message;
            ImapTestResult.Foreground = System.Windows.Media.Brushes.Salmon;
        }
    }

    static void FillDevices(ComboBox box, List<string> devices, string current)
    {
        box.Items.Add(DefaultDevice);
        foreach (var d in devices) box.Items.Add(d);
        if (string.IsNullOrWhiteSpace(current)) { box.SelectedIndex = 0; return; }

        // Saved names may be longer than the 31-character names Windows reports here.
        var match = devices.FirstOrDefault(d => current.StartsWith(d, StringComparison.OrdinalIgnoreCase) ||
                                                d.StartsWith(current, StringComparison.OrdinalIgnoreCase));
        if (match != null) { box.SelectedItem = match; return; }
        // Device not plugged in right now: keep it so it isn't lost by saving.
        box.Items.Add(current);
        box.SelectedItem = current;
    }

    static string DeviceValue(ComboBox box) =>
        box.SelectedItem is string s && s != DefaultDevice ? s : "";

    void TestChime_Click(object sender, RoutedEventArgs e) => Chime.Play(SipPhone.FindOutputDevice(DeviceValue(RingBox)), soft: false);

    void TestOutput_Click(object sender, RoutedEventArgs e) => PlayTest(DeviceValue(OutputBox));
    void TestRing_Click(object sender, RoutedEventArgs e) => PlayTest(DeviceValue(RingBox));

    void PlayTest(string device)
    {
        StopTest();
        _test = new Ringer(SipPhone.FindOutputDevice(device));
        _test.Start();
        _stopTest.Start();
    }

    void StopTest()
    {
        _stopTest.Stop();
        _test?.Dispose();
        _test = null;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.AudioOutputDevice = DeviceValue(OutputBox);
        _settings.AudioInputDevice = DeviceValue(InputBox);
        _settings.RingDevice = DeviceValue(RingBox);
        _settings.SpeakCallerName = SpeakCallerBox.IsChecked == true;
        _settings.SpeakStatus = SpeakStatusBox.IsChecked == true;
        _settings.AddUSCountryCode = USCodeBox.IsChecked == true;
        _settings.AutoUpdate = AutoUpdateBox.IsChecked == true;
        _settings.KeepRunning = KeepRunningBox.IsChecked == true;
        _settings.AutoAnswerSeconds = AutoAnswerBox.SelectedItem is ComboBoxItem { Tag: int s } ? s : 0;
        _settings.TextsEnabled = TextsEnabledBox.IsChecked == true;
        _settings.PopUpNewTexts = PopUpTextsBox.IsChecked == true;
        _settings.SpeakNewTexts = SpeakTextsBox.IsChecked == true;
        _settings.ChimeNewTexts = ChimeTextsBox.IsChecked == true;
        if (GoogleSelected)
        {
            _settings.ImapAuth = "google";
            _settings.ImapHost = "imap.gmail.com";   // ImapUser was set by Sign in with Google
        }
        else
        {
            _settings.ImapAuth = "password";
            _settings.ImapHost = ImapHostBox.Text.Trim();
            _settings.ImapUser = ImapUserBox.Text.Trim();
        }
        if (ImapPasswordBox.Password.Length > 0) _settings.SetImapPassword(ImapPasswordBox.Password.Replace(" ", ""));   // Google shows app passwords with spaces
        try { _settings.Save(); }
        catch (Exception ex) { MessageBox.Show(this, $"Could not save settings:\n{ex.Message}", "Big Phone"); return; }
        Log.Write("Settings saved");
        Saved?.Invoke();
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
