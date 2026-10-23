using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BigPhone;

public partial class App : Application
{
    Mutex? _single;

    /// <summary>The loaded settings, for windows that remember their size and position.</summary>
    public static PhoneSettings? Settings { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Keep-alive task: "BigPhone.exe --ensure" starts Big Phone only if it isn't already running.
        if (e.Args.Contains("--ensure"))
        {
            if (Mutex.TryOpenExisting(@"Local\BigPhone.SingleInstance", out var running))
            {
                running.Dispose();
            }
            else
            {
                Log.Write("Keep-alive: Big Phone was not running - starting it");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(Environment.ProcessPath!)!,
                });
            }
            Shutdown();
            return;
        }

        // After a self-update the old copy may still be closing: wait for it.
        _single = new Mutex(false, @"Local\BigPhone.SingleInstance");
        bool first;
        try { first = _single.WaitOne(e.Args.Contains("--after-update") ? TimeSpan.FromSeconds(15) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { first = true; }
        if (!first)
        {
            MessageBox.Show("Big Phone is already open.", "Big Phone");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write($"Unhandled UI error: {ex.Exception}");
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write($"Unhandled error: {ex.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Write($"Unobserved task error: {ex.Exception}"); ex.SetObserved(); };

        Updater.CleanUpOldVersion();
        Log.Write($"Big Phone {Updater.CurrentVersionText} starting (portable={PhoneSettings.IsPortable}, folder={PhoneSettings.Folder})");

        // A hand-edited settings file with a typo must not leave the phone dead: show where the
        // mistake is, open the file, and try again when Notepad is closed.
        PhoneSettings? settings = null;
        while (settings == null)
        {
            try { settings = PhoneSettings.Load(); }
            catch (Exception ex)
            {
                Log.Write($"Settings file error: {ex.Message}");
                var r = MessageBox.Show(
                    $"The settings file has a mistake in it:\n\n{ex.Message}\n\n" +
                    "Press OK to fix it in Notepad. Big Phone starts again when you close Notepad.",
                    "Big Phone", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (r != MessageBoxResult.OK) { Shutdown(); return; }
                EditSettingsFileAndWait();
            }
        }

        while (string.IsNullOrWhiteSpace(settings.Username))
        {
            var r = MessageBox.Show($"Please fill in the FlyNumber SIP \"Username\" (and \"AuthId\") in the settings file.\n\n" +
                                    "Press OK to open it. Big Phone starts again when you close Notepad.",
                                    "Big Phone setup", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (r != MessageBoxResult.OK) { Shutdown(); return; }
            EditSettingsFileAndWait();
            try { settings = PhoneSettings.Load(); } catch { /* reported on the next start */ }
        }

        string password = settings.GetPassword();
        if (password.Length == 0)
        {
            password = AskPassword(settings.Username) ?? "";
            if (password.Length == 0) { Shutdown(); return; }
            settings.SetPassword(password);
            settings.Save();
        }

        Settings = settings;
        BigPhone.Theme.Apply(settings.Theme);
        BigPhone.Theme.WatchWindowsSetting();
        _ = Task.Run(() => KeepAlive.Sync(settings));   // make the scheduled task match the setting (and this folder)
        var main = new MainWindow(settings, password);
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
        if (e.Args.Contains("--test-text")) main.SimulateTextSoon();   // helper test: chime, voice, pop-up over other windows
    }

    public static void EditSettingsFileAndWait()
    {
        using var p = System.Diagnostics.Process.Start("notepad.exe", $"\"{PhoneSettings.FilePath}\"");
        p?.WaitForExit();
    }

    /// <summary>Starts a fresh copy of Big Phone and closes this one (the new copy waits for this one to exit).</summary>
    public static void Restart()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--after-update") { UseShellExecute = false });
        Current.Shutdown();
    }

    static string? AskPassword(string username)
    {
        var box = new PasswordBox { FontSize = 32, Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(6) };
        var ok = new Button { Content = "Save", FontSize = 28, Padding = new Thickness(30, 8, 30, 8), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = $"FlyNumber SIP password for account {username}\n(it is stored encrypted for this Windows user)",
            FontSize = 20, TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(box);
        panel.Children.Add(ok);

        var w = new Window
        {
            Title = "Big Phone setup", Content = panel, Width = 620, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => box.Focus();
        return w.ShowDialog() == true ? box.Password : null;
    }
}
