using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace BigPhone;

public partial class ContactsWindow : Window
{
    sealed class Row : INotifyPropertyChanged
    {
        string _name = "", _number = "";
        int _position;
        public string Name { get => _name; set { _name = value; Changed(); Changed(nameof(DisplayName)); } }
        public string Number { get => _number; set { _number = value; Changed(); } }
        public int Position { get => _position; set { _position = value; Changed(); } }
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(no name)" : Name;
        public event PropertyChangedEventHandler? PropertyChanged;
        void Changed([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    readonly PhoneSettings _settings;
    readonly ObservableCollection<Row> _rows = new();
    bool _loading;

    /// <summary>Raised after contacts were saved, so the phone screen can refresh its buttons.</summary>
    public event Action? Saved;

    public ContactsWindow(PhoneSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        foreach (var sd in settings.SpeedDials)
            if (!string.IsNullOrWhiteSpace(sd.Name) || !string.IsNullOrWhiteSpace(sd.Number))
                _rows.Add(new Row { Name = sd.Name, Number = sd.Number });
        List.ItemsSource = _rows;
        Renumber();
        if (_rows.Count > 0) List.SelectedIndex = 0;
        UpdateEditor();
    }

    Row? Selected => List.SelectedItem as Row;

    void Renumber()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].Position = i + 1;
    }

    void UpdateEditor()
    {
        _loading = true;
        var r = Selected;
        NameBox.Text = r?.Name ?? "";
        NumberBox.Text = r?.Number ?? "";
        NameBox.IsEnabled = NumberBox.IsEnabled = r != null;
        DeleteButton.IsEnabled = r != null;
        UpButton.IsEnabled = r != null && List.SelectedIndex > 0;
        DownButton.IsEnabled = r != null && List.SelectedIndex < _rows.Count - 1;
        _loading = false;
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEditor();

    void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected is not Row r) return;
        r.Name = NameBox.Text;
        r.Number = NumberBox.Text;
        ErrorText.Text = "";
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        var r = new Row();
        _rows.Add(r);
        Renumber();
        List.SelectedItem = r;
        List.ScrollIntoView(r);
        NameBox.Focus();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not Row r) return;
        if (MessageBox.Show(this, $"Delete {r.DisplayName}?", "Contacts", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        int i = _rows.IndexOf(r);
        _rows.RemoveAt(i);
        Renumber();
        if (_rows.Count > 0) List.SelectedIndex = Math.Min(i, _rows.Count - 1);
        UpdateEditor();
    }

    void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    void Down_Click(object sender, RoutedEventArgs e) => Move(+1);

    void Move(int delta)
    {
        int i = List.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _rows.Count) return;
        _rows.Move(i, j);
        Renumber();
        List.SelectedIndex = j;
        UpdateEditor();
    }

    /// <summary>Checks every row; returns an error message or null.</summary>
    string? Validate()
    {
        foreach (var r in _rows)
        {
            bool noName = string.IsNullOrWhiteSpace(r.Name);
            bool noNumber = PhoneSettings.DigitsOnly(r.Number).Length < 3;
            if (noName && string.IsNullOrWhiteSpace(r.Number)) continue;   // empty row: dropped on save
            if (noName) { List.SelectedItem = r; return "Please type a name."; }
            if (noNumber) { List.SelectedItem = r; return $"Please type a phone number for {r.Name}."; }
        }
        return null;
    }

    void Apply()
    {
        _settings.SpeedDials = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && PhoneSettings.DigitsOnly(r.Number).Length >= 3)
            .Select(r => new SpeedDial { Name = r.Name.Trim(), Number = r.Number.Trim() })
            .ToList();
        try { _settings.Save(); }
        catch (Exception ex) { Log.Write($"Saving contacts failed: {ex.Message}"); }
        Log.Write($"Contacts saved ({_settings.SpeedDials.Count})");
        Saved?.Invoke();
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        string? err = Validate();
        if (err != null) { ErrorText.Text = err; return; }
        Apply();
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>An incoming call needs the screen: keep the complete contacts and get out of the way.</summary>
    public void CloseForIncomingCall()
    {
        Apply();
        Close();
    }
}
