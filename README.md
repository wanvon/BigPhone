# Big Phone

A Windows SIP softphone with **extra-large controls**, made for people who find normal
softphones (like MicroSIP) too small or fiddly. Built for the FlyNumber SIP service, but
works with any standard SIP/UDP provider.

- Huge keypad, **CALL**, **HANG UP** and **MUTE** buttons that fill the screen
- **CONTACTS**: full-screen scrolling list of big name buttons; tap a name to call
- **TEXTS**: incoming text messages in huge print, with a one-tap **COPY CODE** button for one-time passcodes
- **RECENTS**: recent calls with time and length, tap to call back; shows "2 MISSED" in red when calls were missed
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

Call history is kept in `recents.json` (last 100 calls) next to `settings.json`.

Because the zip contains a `BigPhone.portable` file, settings and the log stay next to the exe.
Delete that file to use `%APPDATA%\BigPhone` instead.

> Don't run MicroSIP and Big Phone on the same account at the same time. Incoming calls
> may ring on only one of them.

## Contacts and settings

Click the small **⚙** in the top-right corner of the status bar:

| Menu button | What it does |
|---|---|
| **Contacts** | Add / edit / delete / reorder contacts shown by the CONTACTS button (first 6 also on F1–F6). Incoming calls from them show and speak the name. |
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

## Text messages (receive only)

FlyNumber doesn't deliver texts to SIP apps and has no SMS API yet. It **emails** every incoming text
(`FlyNumber: New SMS from …`, sent by `notifications@mail.flynumber.com`). Big Phone reads those emails
and shows each text full screen. Only emails from that sender are read; nothing is deleted or changed.

Setup (helper):

1. Use a mailbox that supports app passwords. A **dedicated Gmail** for the phone is best.
   For Gmail: turn on 2-Step Verification, then create an app password at
   <https://myaccount.google.com/apppasswords>.
2. FlyNumber panel → **Settings → Notifications**: send SMS emails to that address (verify it).
3. Big Phone → ⚙ → **Settings → Text messages**: tick *Show text messages*, pick the provider, enter the
   address and app password, press **Test connection**, then **Save**.

### Gmail without an app password: Sign in with Google

Use this when Google says *"The setting you are looking for is not available for your account"* on the
App passwords page (common with passkeys), or for an account without 2-Step Verification. It works with
any Gmail account, including the user's own, without changing how they sign in.

**A. One-time Google Cloud setup** (in any Google account, e.g. the helper's):

1. Open <https://console.cloud.google.com/> and create a project (or pick an existing one), e.g. "Big Phone".
2. **APIs & Services → Library**: search **Gmail API** and click **Enable**.
3. **Google Auth Platform** (or *OAuth consent screen*):
   - **Branding**: App name `Big Phone`, your email as support and developer contact. Save.
   - **Audience**: User type **External**. Click **Publish app** so the status is **In production**.
     (In *Testing* Google signs Big Phone out every 7 days.)
   - **Data Access → Add or remove scopes**: under *Manually add scopes* enter `https://mail.google.com/`,
     click **Add to table**, tick it, **Update**, **Save**.
4. **Clients → Create client**: Application type **Desktop app**, name `Big Phone`, **Create**, then
   **Download JSON** (a `client_secret_….json` file in Downloads).

**B. In Big Phone** (⚙ → Settings → TEXT MESSAGES):

1. Tick *Show text messages*, choose **Gmail – Sign in with Google (no app password)**.
2. **📂 Choose file…** → pick the downloaded `client_secret_….json`.
3. **Sign in with Google** → the browser opens → **pick the account whose inbox gets the FlyNumber emails**.
4. Google warns *"Google hasn't verified this app"* (normal for a private app): **Advanced → Go to Big Phone**.
5. Allow access (tick the Gmail box if shown) → **Continue**. When the browser says you can close the
   window, Big Phone shows **✔ Signed in as …**.
6. **Test connection**, then **✔ Save**.

The sign-in is stored encrypted (`google-signin.dat`). To disconnect: Google Account → Security →
*Your connections to third-party apps* → Big Phone → **Delete all connections**.

When a text arrives (checked every 20 s), it opens full screen unless a call is in progress, and is read
aloud if enabled. If it contains a one-time code, the code is shown in large digits with a
**📋 COPY CODE** button (Ctrl+C also works). Texts are kept in `texts.json` (last 100).

To check the alerts without waiting for a real text, start `BigPhone.exe --test-text` (e.g. from a
shortcut or Win+R). Ten seconds later a pretend text with code 123456 arrives (it isn't saved).

Limits: **sending texts isn't possible** from Big Phone. FlyNumber only allows sending from its web panel,
and doesn't yet allow texting US numbers at all. Outlook.com / Hotmail can't be used (no app passwords).

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
