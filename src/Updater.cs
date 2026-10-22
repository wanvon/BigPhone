using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace BigPhone;

/// <summary>
/// Self-update from GitHub Releases.
/// A release must contain BigPhone-x.y.z-win-x64.zip and BigPhone-x.y.z-win-x64.zip.sha256 (made by release.ps1).
/// Updating: download + verify the zip, rename the running BigPhone.exe to BigPhone.old.exe (Windows allows renaming
/// a running exe), put the new exe in its place and restart. settings.json is never touched.
/// </summary>
public static class Updater
{
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BigPhone.exe");
    static string ExeDir => Path.GetDirectoryName(ExePath)!;
    static string StagedExe => Path.Combine(PhoneSettings.Folder, "update", "BigPhone.exe");
    static string StagedVersionFile => Path.Combine(PhoneSettings.Folder, "update", "version.txt");

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"BigPhone/{CurrentVersionText}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Remove leftovers of a previous update. Call once at startup.</summary>
    public static void CleanUpOldVersion()
    {
        try
        {
            string old = Path.Combine(ExeDir, "BigPhone.old.exe");
            if (File.Exists(old)) File.Delete(old);
        }
        catch { /* still locked for a moment after restart; try next time */ }
    }

    public record UpdateInfo(Version Version, string Tag, string Notes);

    /// <summary>Checks GitHub for a newer release. Returns null when up to date or on any error.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
            v = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            var cur = new Version(CurrentVersion.Major, CurrentVersion.Minor, Math.Max(CurrentVersion.Build, 0));
            if (v <= cur) return null;
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new UpdateInfo(v, tag, notes);
        }
        catch (Exception ex)
        {
            Log.Write($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Downloads and verifies the release, leaving the new exe staged for <see cref="ApplyStagedAndRestart"/>.</summary>
    public static async Task<bool> DownloadAsync(string repo, UpdateInfo info)
    {
        try
        {
            if (StagedVersion() == info.Version) return true;   // already downloaded

            string ver = $"{info.Version.Major}.{info.Version.Minor}.{info.Version.Build}";
            string zipName = $"BigPhone-{ver}-win-x64.zip";
            string baseUrl = $"https://github.com/{repo}/releases/download/{info.Tag}/";

            byte[] zip = await Http.GetByteArrayAsync(baseUrl + zipName);
            string shaText = await Http.GetStringAsync(baseUrl + zipName + ".sha256");
            string expected = shaText.Trim().Split(' ', '\t')[0].ToLowerInvariant();
            string actual = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
            if (expected != actual)
            {
                Log.Write($"Update {ver} rejected: checksum mismatch");
                return false;
            }

            string dir = Path.GetDirectoryName(StagedExe)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            using (var archive = new ZipArchive(new MemoryStream(zip)))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("BigPhone.exe", StringComparison.OrdinalIgnoreCase));
                if (entry == null) { Log.Write("Update zip has no BigPhone.exe"); return false; }
                entry.ExtractToFile(StagedExe, true);
            }
            File.WriteAllText(StagedVersionFile, ver);
            Log.Write($"Update {ver} downloaded and verified");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"Update download failed: {ex.Message}");
            return false;
        }
    }

    public static Version? StagedVersion()
    {
        try
        {
            if (!File.Exists(StagedExe) || !File.Exists(StagedVersionFile)) return null;
            var v = Version.Parse(File.ReadAllText(StagedVersionFile).Trim());
            return v > CurrentVersion ? v : null;
        }
        catch { return null; }
    }

    /// <summary>Swaps in the staged exe and restarts. Returns false (and changes nothing) if it can't.</summary>
    public static bool ApplyStagedAndRestart()
    {
        var staged = StagedVersion();
        if (staged == null) return false;
        string exe = ExePath;
        string old = Path.Combine(ExeDir, "BigPhone.old.exe");
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Copy(StagedExe, exe);
            }
            catch
            {
                File.Move(old, exe);   // roll back
                throw;
            }
            Directory.Delete(Path.GetDirectoryName(StagedExe)!, true);
            Log.Write($"Updated {CurrentVersionText} -> {staged}; restarting");
        }
        catch (Exception ex)
        {
            Log.Write($"Applying update failed: {ex.Message}");
            return false;
        }

        Process.Start(new ProcessStartInfo(exe, "--after-update") { UseShellExecute = false, WorkingDirectory = ExeDir });
        return true;
    }
}
