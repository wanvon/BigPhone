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

        int[] choices = { 0, 3, 5, 10, 15, 20, 30 };
        foreach (int s in choices.Union(new[] { settings.AutoAnswerSeconds }).OrderBy(x => x))
            AutoAnswerBox.Items.Add(new ComboBoxItem { Content = s == 0 ? "Never" : $"After {s} seconds", Tag = s });
        AutoAnswerBox.SelectedItem = AutoAnswerBox.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == settings.AutoAnswerSeconds);

        InfoText.Text = $"Big Phone {Updater.CurrentVersionText}  ·  account {settings.Username}@{settings.Server}\n" +
                        $"Account and network settings: ⚙ → Advanced (settings file).";
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
        _settings.AutoAnswerSeconds = AutoAnswerBox.SelectedItem is ComboBoxItem { Tag: int s } ? s : 0;
        try { _settings.Save(); }
        catch (Exception ex) { MessageBox.Show(this, $"Could not save settings:\n{ex.Message}", "Big Phone"); return; }
        Log.Write("Settings saved");
        Saved?.Invoke();
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
