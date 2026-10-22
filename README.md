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
   - Otherwise Notepad opens `settings.json`: fill in `Username` (and `AuthId`), save, run again.
4. Type the SIP password when asked. It's stored encrypted (Windows DPAPI) for that Windows user.
5. Optional: start with Windows: press `Win+R`, type `shell:startup`, and put a shortcut to `BigPhone.exe` there.

Because the zip contains a `BigPhone.portable` file, settings and the log stay next to the exe.
Delete that file to use `%APPDATA%\BigPhone` instead.

> Don't run MicroSIP and Big Phone on the same account at the same time. Incoming calls
> may ring on only one of them.

## Settings

Click the small ⚙ in the top-right corner of the status bar, then choose **Yes** to open `settings.json`.
Restart Big Phone after saving. See [`settings.example.json`](settings.example.json).

| Setting | Meaning |
|---|---|
| `SpeedDials` | List of `{ "Name": "...", "Number": "..." }`, up to 6 shown |
| `AudioOutputDevice` / `AudioInputDevice` / `RingDevice` | Start of the Windows device name; empty = default device |
| `AddUSCountryCode` | 10-digit numbers get a leading `1` |
| `SpeakCallerName` | Announce "Call from Mom" before ringing |
| `SpeakStatus` | Also speak "Calling…", "Call ended", etc. |
| `AutoAnswerSeconds` | Answer automatically after N seconds (0 = off) |
| `KeepAliveSeconds` | Keeps the router open for incoming calls (default 15) |
| `SipTrace` | Write full SIP messages to `bigphone.log` for troubleshooting |
| `AutoUpdate` / `UpdateRepo` | Automatic updates from GitHub Releases |

## Updates & versions

- The version is shown in the window title and in the ⚙ box.
- Big Phone checks GitHub Releases at startup and every 24 h, downloads new versions in the
  background, and verifies them against their SHA-256 checksum.
- An update is only **installed when no call is in progress**, either right at startup or
  between 2 and 5 AM, so the user is never interrupted. The ⚙ box → **No** ("check for updates now")
  installs immediately.
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

- **Red "Not connected" bar:** check internet; check username/password in `settings.json`
  (to re-enter the password, clear `PasswordProtected`).
- **"Wrong password":** clear `PasswordProtected` in `settings.json` and restart.
- **Calls connect but no sound / one-way sound:** set `AudioOutputDevice` / `AudioInputDevice`,
  or turn on `SipTrace` and look at `bigphone.log`.

Built with [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) and WPF (.NET 10).
