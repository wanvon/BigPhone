using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BigPhone;

public class SpeedDial
{
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
}

/// <summary>
/// All user settings, stored as JSON in %APPDATA%\BigPhone\settings.json.
/// The SIP password is never stored in plain text: if "Password" is filled in by hand,
/// it is encrypted with Windows DPAPI (current user) on the next start and moved to "PasswordProtected".
/// </summary>
public class PhoneSettings
{
    // --- SIP account (FlyNumber defaults) ---
    public string Server { get; set; } = "sip.flynumber.com";
    public string Domain { get; set; } = "sip.flynumber.com";
    public string Username { get; set; } = "";
    public string AuthId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";
    public string PasswordProtected { get; set; } = "";

    // --- Network ---
    public int LocalSipPort { get; set; } = 0;              // 0 = any free port
    public int RegisterExpirySeconds { get; set; } = 300;
    public int KeepAliveSeconds { get; set; } = 15;         // keeps the router's NAT door open for incoming calls
    public string StunServer { get; set; } = "stun.l.google.com:19302";

    // --- Audio (empty = Windows default device; otherwise the start of the device name) ---
    public string AudioOutputDevice { get; set; } = "";
    public string AudioInputDevice { get; set; } = "";
    public string RingDevice { get; set; } = "";

    // --- Behaviour ---
    public bool AddUSCountryCode { get; set; } = true;      // 10-digit numbers get a leading 1
    public bool SpeakCallerName { get; set; } = true;       // say "Call from Mom" out loud
    public bool SpeakStatus { get; set; } = false;          // say "Calling", "Call ended", etc.
    public int AutoAnswerSeconds { get; set; } = 0;         // 0 = off
    public int RingTimeoutSeconds { get; set; } = 60;
    public bool SipTrace { get; set; } = false;             // write full SIP messages to the log
    public string LastNumber { get; set; } = "";

    // --- Updates (GitHub Releases) ---
    public string UpdateRepo { get; set; } = "wanvon/BigPhone";
    public bool AutoUpdate { get; set; } = true;
    public bool KeepRunning { get; set; } = false;
    public string Theme { get; set; } = "Classic";            // Classic, Dark (blue & gold), Light, Auto          // scheduled task: start at log-on, restart within 5 min if closed            // installs only at startup or 2-5 AM, never during a call

    // --- Text messages (read from FlyNumber's "New SMS" notification emails) ---
    public bool TextsEnabled { get; set; } = false;
    public string ImapHost { get; set; } = "imap.gmail.com";
    public int ImapPort { get; set; } = 993;
    public string ImapUser { get; set; } = "";
    public string ImapPasswordProtected { get; set; } = "";
    public string ImapAuth { get; set; } = "password";      // "password" (app password) or "google" (Sign in with Google)
    public string GoogleClientId { get; set; } = "";         // from the helper's own Google Cloud "Desktop app" client
    public string GoogleClientSecretProtected { get; set; } = "";
    public int TextsPollSeconds { get; set; } = 20;
    public bool PopUpNewTexts { get; set; } = true;          // open new texts full screen when not on a call
    public bool SpeakNewTexts { get; set; } = true;
    public bool ChimeNewTexts { get; set; } = true;          // "ding-dong" on the ring device (soft during calls)          // "New text from 12345. The code is 4 8 2 9 1 3"

    public List<SpeedDial> SpeedDials { get; set; } = new();

    // Last size/position of each window (managed by WindowMemory).
    public Dictionary<string, WindowPlace> Windows { get; set; } = new();

    // ------------------------------------------------------------------

    /// <summary>
    /// Portable mode (like MicroSIP): if settings.json or a "BigPhone.portable" marker file sits next to BigPhone.exe,
    /// everything is kept in that folder. Otherwise %APPDATA%\BigPhone is used.
    /// </summary>
    public static string Folder { get; } = ResolveFolder();
    public static bool IsPortable { get; private set; }

    static string ResolveFolder()
    {
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "settings.json")) || File.Exists(Path.Combine(exeDir, "BigPhone.portable")))
        {
            IsPortable = true;
            return exeDir;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BigPhone");
    }
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static string LogPath => Path.Combine(Folder, "bigphone.log");

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static PhoneSettings Load()
    {
        Directory.CreateDirectory(Folder);
        PhoneSettings s;
        if (File.Exists(FilePath))
        {
            s = JsonSerializer.Deserialize<PhoneSettings>(File.ReadAllText(FilePath)) ?? new PhoneSettings();
        }
        else
        {
            s = new PhoneSettings();
            s.ImportFromMicroSip();
            s.SpeedDials.Add(new SpeedDial { Name = "Example", Number = "" });
        }
        if (!string.IsNullOrEmpty(s.Password))
        {
            s.SetPassword(s.Password);   // encrypt a hand-typed password
        }
        s.Save();
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    public string GetPassword() => Unprotect(PasswordProtected);

    public void SetPassword(string plain)
    {
        PasswordProtected = Protect(plain);
        Password = "";
    }

    public string GetImapPassword() => Unprotect(ImapPasswordProtected);
    public void SetImapPassword(string plain) => ImapPasswordProtected = Protect(plain);

    public string GetGoogleClientSecret() => Unprotect(GoogleClientSecretProtected);
    public void SetGoogleClientSecret(string plain) => GoogleClientSecretProtected = Protect(plain);

    public static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";   // encrypted by a different Windows user / PC
        }
    }

    public string NameForNumber(string number)
    {
        string digits = DigitsOnly(number);
        if (digits.Length == 0) return "";
        foreach (var sd in SpeedDials)
        {
            string d = DigitsOnly(sd.Number);
            if (d.Length == 0) continue;
            // compare the last 10 digits so "+1 941..." matches "941..."
            string a = digits.Length > 10 ? digits[^10..] : digits;
            string b = d.Length > 10 ? d[^10..] : d;
            if (a == b) return sd.Name;
        }
        return "";
    }

    public static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

    /// <summary>Turns what the user typed into what FlyNumber expects (e.g. 9415551234 -> 19415551234).</summary>
    public string NormalizeForDialing(string typed)
    {
        string t = typed.Trim();
        bool plus = t.StartsWith('+');
        string cleaned = new(t.Where(c => char.IsDigit(c) || c == '*' || c == '#').ToArray());
        if (plus) return cleaned;   // international: FlyNumber accepts E.164 digits without the +
        if (AddUSCountryCode && cleaned.Length == 10 && cleaned.All(char.IsDigit)) return "1" + cleaned;
        return cleaned;
    }

    /// <summary>Copies account and audio settings from an existing MicroSIP install (never the password).</summary>
    void ImportFromMicroSip()
    {
        string[] candidates =
        {
            @"C:\Utilities\MicroSIP\MicroSIP.ini",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicroSIP", "microsip.ini"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MicroSIP", "microsip.ini"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MicroSIP", "microsip.ini"),
        };
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path == null) return;

        string section = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            var m = Regex.Match(line, @"^\[(.+)\]$");
            if (m.Success) { section = m.Groups[1].Value; continue; }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim().Trim('"');
            if (val.Length == 0) continue;

            if (section == "Account1")
            {
                switch (key)
                {
                    case "server": Server = val; break;
                    case "domain": Domain = val; break;
                    case "username": Username = val; break;
                    case "authID": AuthId = val; break;
                    case "displayName": DisplayName = val; break;
                }
            }
            else if (section == "Settings")
            {
                switch (key)
                {
                    case "audioOutputDevice": AudioOutputDevice = val; break;
                    case "audioInputDevice": AudioInputDevice = val; break;
                    case "audioRingDevice": RingDevice = val; break;
                    case "STUN": StunServer = val; break;
                }
            }
        }
    }
}
