# Big Phone

A Windows SIP softphone with **extra-large controls**, made for people who find normal
softphones (like MicroSIP) too small or fiddly. Built for the FlyNumber SIP service, but
works with any standard SIP/UDP provider.

- Huge keypad, **CALL**, **HANG UP** and **MUTE** buttons that fill the screen
- Up to 6 big **speed-dial** buttons with names ("Mom", "Doctor"…)
- Incoming calls take over the whole screen, flash, ring loudly, and **say who is calling**
- Big coloured status bar: green = ready, red = not connected, blue = talking (with call timer)
- Shaky double-taps are ignored; closing the window asks first
- Keyboard: digits / `*` / `#`, **Enter** = call or answer, **Esc** = hang up, **F1–F6** = speed dials
- Portable, single `.exe`, no install or .NET needed; updates itself from GitHub Releases

## Install (portable)

1. Download `BigPhone-x.y.z-win-x64.zip` from the [Releases](../../releases) page.
2. Unzip it anywhere, e.g. `C:\Utilities\BigPhone` (a USB stick works too).
3. Run `BigPhone.exe`.
   - If MicroSIP is installed on the PC, the FlyNumber account and audio devices are copied from it.
   - Otherwise Notepad opens `settings.json`: fill in `Username` (and `AuthId`), save and close Notepad.
4. Type the SIP password when asked. It's stored encrypted (Windows DPAPI) for that Windows user.
5. Add contacts: ⚙ → **Contacts**.
6. Optional: start with Windows: press `Win+R`, type `shell:startup`, and put a shortcut to `BigPhone.exe` there.

Because the zip contains a `BigPhone.portable` file, settings and the log stay next to the exe.
Delete that file to use `%APPDATA%\BigPhone` instead.

> Don't run MicroSIP and Big Phone on the same account at the same time. Incoming calls
> may ring on only one of them.

## Contacts and settings

Click the small **⚙** in the top-right corner of the status bar:

| Menu button | What it does |
|---|---|
| **Contacts** | Add / edit / delete / reorder speed-dial contacts. The first 6 become big buttons; incoming calls from them show and speak the name. |
| **Settings** | Speaker, microphone and ring device (with ▶ Test), spoken caller name, spoken status, US country code, auto-answer, automatic updates. |
| **Check for updates** | Looks for a newer version now and offers to install it. |
| **Advanced (settings file)** | Opens `settings.json` in Notepad; Big Phone restarts when Notepad is closed. |

Changes in Contacts and Settings take effect immediately. If a call comes in while one of these
windows is open, it closes (contacts are kept) so the ANSWER screen is never blocked.

### Advanced settings file

Account and network options are only in `settings.json` (see [`settings.example.json`](settings.example.json)):

| Setting | Meaning |
|---|---|
| `Server`, `Domain`, `Username`, `AuthId`, `DisplayName` | SIP account |
| `PasswordProtected` | Encrypted password; clear it to be asked again |
| `KeepAliveSeconds` | Keeps the router open for incoming calls (default 15) |
| `StunServer` | Used to find the public IP for audio |
| `RingTimeoutSeconds` | Give up an outgoing call after this long |
| `SipTrace` | Write full SIP messages to `bigphone.log` for troubleshooting |
| `UpdateRepo` | GitHub repository updates come from |

## Updates & versions

- The version is shown in the window title, the ⚙ menu and the Settings screen.
- Big Phone checks GitHub Releases at startup and every 24 h, downloads new versions in the
  background, and verifies them against their SHA-256 checksum.
- An update is only **installed when no call is in progress**, either right at startup or
  between 2 and 5 AM, so the user is never interrupted. ⚙ → **Check for updates** installs immediately (⚙ → **Check for updates**).
- `settings.json` is never touched by updates.

### Making a release (developer)

Requires the .NET 10 SDK and the GitHub CLI (`gh auth login`).

1. Add a `## x.y.z` section to [`CHANGELOG.md`](CHANGELOG.md).
2. Run:
   ```powershell
   .\release.ps1 -Version x.y.z
   ```
   This stamps the version, builds a self-contained single-file exe, zips it with a
   `.sha256` checksum, commits, tags `vx.y.z`, pushes, and creates the GitHub release.

## Building

```powershell
dotnet build src\BigPhone.csproj
dotnet run --project src\BigPhone.csproj
```

## Troubleshooting

- **Red "Not connected" bar:** check the internet connection, then the account in ⚙ → Advanced.
- **"Wrong password":** ⚙ → Advanced, clear `PasswordProtected`, close Notepad. You'll be asked for the password.
- **"The settings file has a mistake in it":** the message says the line; press OK, fix it, close Notepad.
- **Calls connect but no sound / one-way sound:** ⚙ → Settings, pick the right speaker and
  microphone (use ▶ Test), or turn on `SipTrace` in the settings file and look at `bigphone.log`.

Built with [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) and WPF (.NET 10).
