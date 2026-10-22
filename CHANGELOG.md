# Changelog

All notable changes to Big Phone. Versions follow `MAJOR.MINOR.PATCH`;
each release is tagged `vX.Y.Z` on GitHub and installed copies update themselves.

## 1.1.1 - 2026-10-22

- Settings → Text messages: the provider help now has a clickable link to the app-password page,
  and recommends a separate mailbox just for Big Phone so the user never has to sign in to it.

## 1.1.0 - 2026-10-22

- **Text messages (receive only).** FlyNumber has no SMS API and doesn't send texts over SIP; it emails
  each incoming text. Big Phone can read those emails (IMAP: Gmail, Yahoo, iCloud, AOL or other) and show
  each text full screen in huge print. Turn on in ⚙ → Settings → Text messages. Off by default.
- **📋 COPY CODE**: one-time codes (e.g. "G-123456", "123-456", "Your code is 482913") are found
  automatically, shown big, and copied to the clipboard with one tap. Also COPY TEXT, READ aloud, CALL back.
- New **💬 TEXTS** button on the main screen; turns green with "1 NEW TEXT" until read.
- New texts open automatically (and can be read aloud, including the code) when not on a call.
- The email app password is stored encrypted (Windows DPAPI), like the SIP password.

## 1.0.2 - 2026-10-22

- **CONTACTS** button on the main screen opens a full-screen, scrolling list of big name buttons
  (all the same size, scaled to the screen). Tap a name to call. No more limit of 6 contacts.
- **RECENTS** button: the last 100 calls (called, answered, missed, declined) with time and length;
  tap one to call back. Turns red and shows "2 MISSED" until it is opened.
- Big ▲ / ▼ buttons scroll the lists for anyone who finds scrolling hard; mouse wheel and touch work too.
- Better audio diagnostics in bigphone.log: device list, chosen speaker/microphone, codec, and
  whether audio packets arrive.

## 1.0.1 - 2026-10-22

- New **Contacts** screen (⚙ → Contacts): add, edit, delete and reorder speed-dial contacts with big
  text boxes. Changes appear on the phone immediately, no restart.
- New **Settings** screen (⚙ → Settings): pick speaker, microphone and ring device (with ▶ Test),
  caller-name announcement, spoken status, US country code, auto-answer delay and automatic updates.
- The ⚙ button now opens a menu with big buttons instead of a Yes/No/Cancel box.
- These windows step aside automatically when a call comes in, so ANSWER is never blocked.
- "Advanced (settings file)" restarts Big Phone when Notepad is closed, so hand edits are picked up
  and never overwritten.
- A typo in settings.json no longer stops the phone from starting: it shows where the mistake is
  and reopens the file.

## 1.0.0 - 2026-10-22

- First release.
- Extra-large keypad, CALL / HANG UP / MUTE buttons and up to 6 speed-dial buttons.
- Full-screen flashing incoming-call screen with ANSWER / NO buttons, loud ring, caller name spoken aloud.
- FlyNumber SIP account imported automatically from MicroSIP on first run; password stored encrypted (Windows DPAPI).
- Keyboard: digits, Enter = call/answer, Esc = hang up, F1-F6 = speed dials.
- Portable mode (settings next to the exe) and automatic updates from GitHub Releases.
