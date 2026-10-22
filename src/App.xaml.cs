using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BigPhone;

public partial class App : Application
{
    Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

        PhoneSettings settings;
        try { settings = PhoneSettings.Load(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read settings file:\n{PhoneSettings.FilePath}\n\n{ex.Message}", "Big Phone");
            Shutdown();
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            MessageBox.Show($"Please fill in the FlyNumber SIP username in:\n{PhoneSettings.FilePath}\n\nThen start Big Phone again.", "Big Phone setup");
            System.Diagnostics.Process.Start("notepad.exe", $"\"{PhoneSettings.FilePath}\"");
            Shutdown();
            return;
        }

        string password = settings.GetPassword();
        if (password.Length == 0)
        {
            password = AskPassword(settings.Username) ?? "";
            if (password.Length == 0) { Shutdown(); return; }
            settings.SetPassword(password);
            settings.Save();
        }

        var main = new MainWindow(settings, password);
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
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
