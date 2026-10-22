using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace BigPhone;

public class TextMessage
{
    public DateTime Time { get; set; }
    public string From { get; set; } = "";      // sender number (or short code / name) as FlyNumber reports it
    public string Text { get; set; } = "";
    public bool Read { get; set; }

    /// <summary>One-time code found in the text, digits only (e.g. "482913"), or "" if none.</summary>
    public string Code => OtpFinder.Find(Text);
}

/// <summary>Received texts, newest first, stored in texts.json next to settings.json.</summary>
public static class TextStore
{
    const int MaxEntries = 100;
    static string FilePath => Path.Combine(PhoneSettings.Folder, "texts.json");
    static readonly object Lock = new();

    class Store
    {
        public uint UidValidity { get; set; }
        public uint LastUid { get; set; }
        public List<TextMessage> Messages { get; set; } = new();
    }

    static readonly Store Data = Load();

    public static List<TextMessage> Messages { get { lock (Lock) return Data.Messages.ToList(); } }
    public static int Unread { get { lock (Lock) return Data.Messages.Count(m => !m.Read); } }

    static Store Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) ?? new Store();
        }
        catch (Exception ex) { Log.Write($"Could not read texts.json: {ex.Message}"); }
        return new Store();
    }

    static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Log.Write($"Could not save texts.json: {ex.Message}"); }
    }

    public static (uint validity, uint lastUid) Position { get { lock (Lock) return (Data.UidValidity, Data.LastUid); } }

    public static void Add(IEnumerable<TextMessage> msgs, uint validity, uint lastUid)
    {
        lock (Lock)
        {
            foreach (var m in msgs.OrderBy(m => m.Time)) Data.Messages.Insert(0, m);
            if (Data.Messages.Count > MaxEntries) Data.Messages.RemoveRange(MaxEntries, Data.Messages.Count - MaxEntries);
            Data.UidValidity = validity;
            Data.LastUid = lastUid;
            Save();
        }
    }

    public static void MarkRead(TextMessage m)
    {
        lock (Lock)
        {
            var stored = Data.Messages.FirstOrDefault(x => x.Time == m.Time && x.From == m.From && x.Text == m.Text);
            if (stored == null || stored.Read) return;
            stored.Read = true;
            m.Read = true;
            Save();
        }
    }
}

/// <summary>Finds one-time passcodes ("Your code is 482913", "G-123456", "123-456") in a text.</summary>
public static class OtpFinder
{
    static readonly Regex Keyword = new(@"\b(code|otp|passcode|pass code|pin|verification|verify|security|login|one[- ]time|token|password)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 4-8 digits, or 3+3 / 4+4 groups separated by space or dash. Not part of a longer number such as
    // a phone number ("1-800-555-1234"), but "G-123456" (Google) is fine.
    static readonly Regex Candidate = new(@"(?<![\d+])(?<!\d[- ])(\d{3}[- ]\d{3}|\d{4}[- ]\d{4}|\d{4,8})(?![\d])(?![- ]\d)",
        RegexOptions.Compiled);

    static readonly Regex IsYourCode = new(@"^\s*(is|=)\s+(your|the)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex NotACode = new(@"(ref|reference|order|acct|account|#|no\.?|ending in|x)\s*:?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    record Cand(int Pos, int End, string Digits);

    public static string Find(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var cands = Candidate.Matches(text).Cast<Match>()
            .Select(m => new Cand(m.Index, m.Index + m.Length, new string(m.Value.Where(char.IsDigit).ToArray())))
            .Where(c => !LooksLikeYear(c.Digits))
            .ToList();
        if (cands.Count == 0) return "";
        if (cands.Count == 1) return cands[0].Digits;

        // Several numbers: score each by how close it is to a word like "code"; "123456 is your code" wins outright,
        // "Ref 4321" / "order #5521" lose, and 6-digit codes (the most common) are preferred.
        var keys = Keyword.Matches(text).Cast<Match>().Select(m => m.Index).ToList();
        double Score(Cand c)
        {
            double s = keys.Count == 0 ? c.Pos : keys.Min(k => Math.Abs(c.Pos - k));
            if (IsYourCode.IsMatch(text[c.End..])) s -= 100;
            if (NotACode.IsMatch(text[..c.Pos])) s += 200;
            if (c.Digits.Length != 6) s += 15;
            return s;
        }
        return cands.OrderBy(Score).First().Digits;
    }

    static bool LooksLikeYear(string d) => d.Length == 4 && int.TryParse(d, out int y) && y >= 1990 && y <= 2099;
}

/// <summary>
/// Checks a mailbox (IMAP) for FlyNumber's "New SMS from …" notification emails and turns them into texts.
/// FlyNumber has no SMS API today; these emails are the only way for an app to receive texts.
/// </summary>
public sealed class TextWatcher : IDisposable
{
    public const string FlyNumberSender = "notifications@mail.flynumber.com";

    readonly PhoneSettings _s;
    readonly string _password;
    Timer? _timer;
    int _busy;

    public event Action<List<TextMessage>>? NewTexts;   // background thread

    public TextWatcher(PhoneSettings settings, string password)
    {
        _s = settings;
        _password = password;
    }

    public void Start()
    {
        var period = TimeSpan.FromSeconds(Math.Max(10, _s.TextsPollSeconds));
        _timer = new Timer(_ => _ = CheckAsync(), null, TimeSpan.FromSeconds(3), period);
        Log.Write($"Watching {_s.ImapUser} on {_s.ImapHost} for FlyNumber texts every {period.TotalSeconds:0} s");
    }

    public void Dispose() => _timer?.Dispose();

    async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var (validity, lastUid) = TextStore.Position;
            var (msgs, newValidity, newLast) = await FetchAsync(_s, _password, validity, lastUid);
            if (newValidity != validity || newLast != lastUid || msgs.Count > 0)
                TextStore.Add(msgs, newValidity, newLast);
            if (msgs.Any(m => !m.Read)) NewTexts?.Invoke(msgs.Where(m => !m.Read).ToList());
        }
        catch (Exception ex) { Log.Write($"Text check failed: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>Connects, logs in and counts FlyNumber text emails. Used by the Settings "Test" button.</summary>
    public static async Task<string> TestAsync(string host, int port, string user, string password)
    {
        using var client = new ImapClient { Timeout = 20000 };
        await client.ConnectAsync(host, port, SecureSocketOptions.Auto);
        await client.AuthenticateAsync(user, password);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly);
        var ids = await client.Inbox.SearchAsync(SearchQuery.FromContains(FlyNumberSender));
        await client.DisconnectAsync(true);
        return $"Connected. Found {ids.Count} FlyNumber text email(s) in the inbox.";
    }

    static async Task<(List<TextMessage>, uint, uint)> FetchAsync(PhoneSettings s, string password, uint validity, uint lastUid)
    {
        var result = new List<TextMessage>();
        using var client = new ImapClient { Timeout = 20000 };
        await client.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.Auto);
        await client.AuthenticateAsync(s.ImapUser, password);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly);

        bool firstRun = validity == 0 || inbox.UidValidity != validity;
        SearchQuery query = SearchQuery.FromContains(FlyNumberSender);
        if (firstRun)
            query = query.And(SearchQuery.DeliveredAfter(DateTime.Today.AddDays(-2)));   // don't import years of history
        else
            query = query.And(SearchQuery.Uids(new UniqueIdRange(new UniqueId(lastUid + 1), UniqueId.MaxValue)));

        uint newLast = firstRun ? 0 : lastUid;
        foreach (var uid in await inbox.SearchAsync(query))
        {
            if (!firstRun && uid.Id <= lastUid) continue;   // IMAP returns the last message for "n:*" even if n is past the end
            newLast = Math.Max(newLast, uid.Id);
            var msg = await inbox.GetMessageAsync(uid);
            var text = Parse(msg);
            if (text == null) continue;
            text.Read = firstRun;   // history from before Big Phone was set up doesn't pop up
            result.Add(text);
        }
        if (firstRun && inbox.UidNext is { } next) newLast = Math.Max(newLast, next.Id - 1);
        await client.DisconnectAsync(true);
        if (result.Count > 0) Log.Write($"Received {result.Count} text(s) by email");
        return (result, inbox.UidValidity, newLast);
    }

    /// <summary>Extracts sender and message from a FlyNumber notification email.</summary>
    public static TextMessage? Parse(MimeMessage msg)
    {
        var m = Regex.Match(msg.Subject ?? "", @"New SMS from\s+(.+)$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string from = m.Groups[1].Value.Trim();

        string body = msg.TextBody ?? HtmlToText(msg.HtmlBody ?? "");
        return new TextMessage
        {
            Time = msg.Date == DateTimeOffset.MinValue ? DateTime.Now : msg.Date.LocalDateTime,
            From = from,
            Text = ExtractMessage(body, from),
        };
    }

    /// <summary>
    /// The email body holds sender, receiving number and the message. Prefer a labelled "Message:" block;
    /// otherwise drop header-like and boilerplate lines and keep the rest.
    /// </summary>
    static string ExtractMessage(string body, string from)
    {
        var lines = body.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();

        int label = lines.FindIndex(l => Regex.IsMatch(l, @"^(message|text|sms|body)\s*:", RegexOptions.IgnoreCase));
        if (label >= 0)
        {
            string first = Regex.Replace(lines[label], @"^[^:]*:\s*", "");
            var rest = lines.Skip(label + 1).TakeWhile(l => !IsBoilerplate(l));
            string joined = string.Join("\n", new[] { first }.Concat(rest)).Trim();
            if (joined.Length > 0) return joined;
        }

        var kept = lines.Where(l => l.Length > 0 && !IsBoilerplate(l) &&
                                    !Regex.IsMatch(l, @"^(from|to|sender|recipient|received|date|time|number|flynumber)\b.*:", RegexOptions.IgnoreCase));
        string text = string.Join("\n", kept).Trim();
        return text.Length > 0 ? text : body.Trim();
    }

    static bool IsBoilerplate(string l) =>
        Regex.IsMatch(l, @"(https?://|unsubscribe|notification settings|panel\.flynumber|manage .*notifications|you('re| are) receiving|©|all rights reserved|new sms from)",
            RegexOptions.IgnoreCase);

    static string HtmlToText(string html)
    {
        string t = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<a\b[^>]*>.*?</a>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);   // links are panel/unsubscribe boilerplate
        t = Regex.Replace(t, @"<br\s*/?>|</p>|</div>|</tr>|</h\d>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, "<[^>]+>", "");
        return WebUtility.HtmlDecode(t);
    }
}
