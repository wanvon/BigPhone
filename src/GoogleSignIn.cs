using System.IO;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace BigPhone;

/// <summary>
/// "Sign in with Google" for reading FlyNumber text emails from Gmail without an app password
/// (needed for accounts that use passkeys, where Google no longer offers app passwords).
/// Uses the helper's own Google Cloud "Desktop app" client (BigPhone ships no client ID).
/// The refresh token is kept DPAPI-encrypted in google-signin.dat next to settings.json.
/// </summary>
public static class GoogleSignIn
{
    // Full Gmail IMAP access is the only scope Gmail's IMAP server accepts; openid/email tell us which account was chosen.
    static readonly string[] Scopes = { "https://mail.google.com/", "openid", "email" };
    const string UserKey = "bigphone";

    public static bool HasClient(PhoneSettings s) =>
        s.GoogleClientId.Length > 0 && s.GetGoogleClientSecret().Length > 0;

    public static bool IsSignedIn => File.Exists(ProtectedDataStore.FilePath);

    /// <summary>Reads client_id / client_secret from the JSON file downloaded from Google Cloud Console.</summary>
    public static (string id, string secret) ReadClientFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var section = root.TryGetProperty("installed", out var inst) ? inst
                    : root.TryGetProperty("web", out var web) ? web
                    : throw new InvalidDataException("This is not a Google OAuth client file (no \"installed\" section).");
        string id = section.GetProperty("client_id").GetString() ?? "";
        string secret = section.GetProperty("client_secret").GetString() ?? "";
        if (id.Length == 0 || secret.Length == 0) throw new InvalidDataException("The file has no client_id / client_secret.");
        return (id, secret);
    }

    static GoogleAuthorizationCodeFlow Flow(PhoneSettings s) => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = s.GoogleClientId, ClientSecret = s.GetGoogleClientSecret() },
        Scopes = Scopes,
        DataStore = new ProtectedDataStore(),
        Prompt = "select_account consent",   // always show Google's account list, so the right account can be picked
    });

    /// <summary>Opens the browser for Google sign-in and returns the chosen account's email address.</summary>
    public static async Task<string> SignInAsync(PhoneSettings s, CancellationToken ct)
    {
        var flow = Flow(s);
        await flow.DataStore.ClearAsync();
        var app = new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver());
        var cred = await app.AuthorizeAsync(UserKey, ct);
        string email = EmailFromIdToken(cred.Token.IdToken);
        Log.Write($"Signed in with Google as {email}");
        return email;
    }

    /// <summary>A fresh access token for IMAP (refreshed automatically from the stored refresh token).</summary>
    public static async Task<string> GetAccessTokenAsync(PhoneSettings s)
    {
        var flow = Flow(s);
        var token = await flow.LoadTokenAsync(UserKey, CancellationToken.None)
                    ?? throw new InvalidOperationException("Not signed in with Google. Use ⚙ → Settings → Sign in with Google.");
        var cred = new UserCredential(flow, UserKey, token);
        return await cred.GetAccessTokenForRequestAsync();
    }

    public static Task SignOutAsync() => new ProtectedDataStore().ClearAsync();

    static string EmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return "";
        var parts = idToken.Split('.');
        if (parts.Length < 2) return "";
        string b64 = parts[1].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
        return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    }

    /// <summary>Google token store that keeps everything DPAPI-encrypted (the library's FileDataStore writes plain text).</summary>
    sealed class ProtectedDataStore : IDataStore
    {
        public static string FilePath => Path.Combine(PhoneSettings.Folder, "google-signin.dat");
        static readonly object Lock = new();

        static Dictionary<string, string> Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return new();
                string json = PhoneSettings.Unprotect(File.ReadAllText(FilePath));
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            catch { return new(); }
        }

        static void Write(Dictionary<string, string> d)
        {
            if (d.Count == 0) { File.Delete(FilePath); return; }
            File.WriteAllText(FilePath, PhoneSettings.Protect(JsonSerializer.Serialize(d)));
        }

        static string Key<T>(string key) => $"{typeof(T).FullName}-{key}";

        public Task StoreAsync<T>(string key, T value)
        {
            lock (Lock) { var d = Read(); d[Key<T>(key)] = NewtonsoftJsonSerializer.Instance.Serialize(value); Write(d); }
            return Task.CompletedTask;
        }

        public Task DeleteAsync<T>(string key)
        {
            lock (Lock) { var d = Read(); d.Remove(Key<T>(key)); Write(d); }
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            lock (Lock)
            {
                var d = Read();
                return Task.FromResult(d.TryGetValue(Key<T>(key), out var v) ? NewtonsoftJsonSerializer.Instance.Deserialize<T>(v) : default!);
            }
        }

        public Task ClearAsync()
        {
            lock (Lock) { if (File.Exists(FilePath)) File.Delete(FilePath); }
            return Task.CompletedTask;
        }
    }
}
