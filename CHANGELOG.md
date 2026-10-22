# Changelog

All notable changes to Big Phone. Versions follow `MAJOR.MINOR.PATCH`;
each release is tagged `vX.Y.Z` on GitHub and installed copies update themselves.

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
