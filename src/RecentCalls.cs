using System.IO;
using System.Text.Json;

namespace BigPhone;

public enum CallKind { Outgoing, Incoming, Missed, Declined, NotConnected }

public class CallRecord
{
    public DateTime Time { get; set; }
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public CallKind Kind { get; set; }
    public int Seconds { get; set; }
}

/// <summary>Call history, newest first, stored in recents.json next to settings.json.</summary>
public static class RecentCalls
{
    const int MaxEntries = 100;
    static string FilePath => Path.Combine(PhoneSettings.Folder, "recents.json");

    class Store
    {
        public int UnseenMissed { get; set; }
        public List<CallRecord> Calls { get; set; } = new();
    }

    static Store _store = Load();

    public static IReadOnlyList<CallRecord> Calls => _store.Calls;
    public static int UnseenMissed => _store.UnseenMissed;

    static Store Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) ?? new Store();
        }
        catch (Exception ex) { Log.Write($"Could not read recents.json: {ex.Message}"); }
        return new Store();
    }

    static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Log.Write($"Could not save recents.json: {ex.Message}"); }
    }

    public static void Add(CallRecord r)
    {
        _store.Calls.Insert(0, r);
        if (_store.Calls.Count > MaxEntries) _store.Calls.RemoveRange(MaxEntries, _store.Calls.Count - MaxEntries);
        if (r.Kind == CallKind.Missed) _store.UnseenMissed++;
        Save();
    }

    public static void MarkSeen()
    {
        if (_store.UnseenMissed == 0) return;
        _store.UnseenMissed = 0;
        Save();
    }

    public static string Describe(CallRecord r)
    {
        string kind = r.Kind switch
        {
            CallKind.Outgoing => "↗ You called",
            CallKind.Incoming => "↙ Answered",
            CallKind.Missed => "✖ Missed call",
            CallKind.Declined => "✖ Declined",
            _ => "↗ No answer",
        };
        string when = r.Time.Date == DateTime.Today ? $"Today {r.Time:h:mm tt}"
            : r.Time.Date == DateTime.Today.AddDays(-1) ? $"Yesterday {r.Time:h:mm tt}"
            : r.Time.Year == DateTime.Today.Year ? r.Time.ToString("MMM d  h:mm tt")
            : r.Time.ToString("MMM d yyyy");
        string length = r.Seconds > 0 ? $"  ·  {r.Seconds / 60}:{r.Seconds % 60:00}" : "";
        return $"{kind}  ·  {when}{length}";
    }
}
