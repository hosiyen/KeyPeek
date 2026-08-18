# KeyPeek

Hold a modifier key. See every keyboard shortcut it starts, for the app you're in.
Release. Nothing was typed.

> **Tiếng Việt:** Giữ phím `Ctrl`, `Win` hoặc `Alt` khoảng nửa giây — bảng phím tắt của
> đúng ứng dụng bạn đang dùng hiện ra; thả phím là biến mất. Bấm một dòng để chạy phím
> tắt đó. Giao diện có tiếng Việt (Cài đặt → Giao diện → Ngôn ngữ).
>
> **Cài đặt:** tải file `KeyPeek-…-win-x64.zip` ở mục
> [Releases](../../releases), giải nén, chạy `Install.cmd`. Không cần quyền admin.
> Windows SmartScreen có thể hỏi vì file chưa ký số — chọn "More info" → "Run anyway".

KeyPeek is a Windows tray utility inspired by macOS CheatSheet/KeyCue and by CtrlHelp:
hold `Ctrl`, `Win`, or `Alt` (configurable) for 400 ms and an overlay fades in showing
the focused app's shortcuts that start with the key you're holding, plus the matching
global Windows shortcuts. Keep holding and add more modifiers to narrow the list live;
release to widen; let go of everything to dismiss. You can also **click any shortcut in
the panel to run it**, or click the search box to pin the panel and type to search.

Works on Windows 10 (1809+) and Windows 11. No admin rights, ever.

## Privacy

KeyPeek installs a global keyboard hook, so this needs saying plainly:

- **Keystrokes are never logged.** Nothing you type is written to disk, ever.
- **Nothing about you leaves the machine.** No telemetry, no crash reporting, no accounts.
- The local diagnostic log (`%LOCALAPPDATA%\KeyPeek\logs`) records only app lifecycle
  events, which application a completed hold resolved to, and library load results.
- KeyPeek makes exactly two kinds of network request, both read-only GETs that carry no
  user data whatsoever and both switchable off in Settings:
  1. **Library updates** — shortcut definitions, on a schedule you control (weekly by
     default; there is also a "check now" button).
  2. **App logos** — each app's official icon, fetched once from that app's own vendor
     and cached in `%LOCALAPPDATA%\KeyPeek\icons`. See [App logos](#app-logos).
- The hook passes every keystroke straight through to Windows and **never blocks or
  delays input**. There is exactly one swallowed key: `Esc`, only while the overlay is
  on screen (otherwise `Ctrl`+`Esc` would open the Start menu when dismissing).
- KeyPeek injects synthetic input in exactly two, deliberate cases:
  1. When you **click a shortcut row**, it sends that key combination to your app —
     that's the feature.
  2. When the overlay opens from a `Win` or `Alt` hold, it sends one inert "mask"
     keypress (virtual key 0xFF, which no app receives as a real key) so that releasing
     `Win`/`Alt` doesn't pop the Start menu or the app's menu bar. This is the same
     technique PowerToys and AutoHotkey use.
- Mouse input is never intercepted or modified.

## Install & run

The app is a single self-contained file — no .NET install, no admin rights, any
Windows 10 (1809+) / Windows 11 x64 machine.

**Giving it to someone else (the normal case):**

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package.ps1
```

That produces `dist\KeyPeek-<version>-win-x64.zip` (~58 MB) containing the exe, an
`Install.cmd` / `Uninstall.cmd` pair for people who don't use PowerShell, and a README
in Vietnamese and English. Hand over that one file: they extract it anywhere and
double-click **Install.cmd**. It installs per-user into
`%LOCALAPPDATA%\Programs\KeyPeek` with a Start-Menu shortcut, and unblocks the
mark-of-the-web that a downloaded zip leaves on its contents.

**On this machine, from the repo:** `powershell -ExecutionPolicy Bypass -File
scripts\install.ps1` (add `-Rebuild` to publish first). Remove with
`scripts\uninstall.ps1` (`-RemoveData` also deletes settings and your own library).

**SmartScreen:** the exe is not code-signed, so the first run shows "Windows protected
your PC" — *More info → Run anyway*. Signing requires a certificate that costs money
every year; see NEEDS-REVIEW.md.
- Turn on run-at-startup in Settings ("Start KeyPeek when I sign in") — it uses the
  current-user Run key, nothing else.

- **Right-click the tray icon**: Settings & library, Reload library, Open library
  folder, open the log, Exit. Double-click opens Settings.
- **Settings** (also editable as JSON at `%APPDATA%\KeyPeek\settings.json`):
  trigger keys (`Ctrl`/`Win`/`Alt`/`Shift`) and one hold delay for all of them, theme and
  transparency, panel position (top/centre/bottom), panel animation, Explore mode,
  the suggestions strip and its local usage data, run at startup (current-user Run key —
  no service, no scheduled task), show-over-fullscreen, excluded apps.
  `Shift` is off by default: holding Shift while typing capitals is normal typing.
- **My shortcuts** (Library page): add your own shortcuts by pressing them — no YAML.
  They are written to your own layer, which library updates never overwrite.
- **Explore mode** (off by default): while the panel is open, `↑`/`↓` move a selection,
  `←`/`→` jump between groups, `Enter` runs the selected shortcut. Those keys go to
  KeyPeek instead of the app *only* while the panel is on screen.
- `KeyPeek.exe --quit` stops a running instance; `--validate` checks every library file
  and prints errors with file and line; `--harvest <process>` is a research tool that
  reads a running app's own UI for shortcuts (see PROGRESS.md for its measured yield).

## The interaction, precisely

1. Hold a trigger key (`Ctrl`, `Win`, or `Alt` by default). After 400 ms (configurable,
   per key if you like) the overlay fades in over the monitor your focused app is on —
   without stealing focus.
2. The panel shows **the held key's table** — holding `Ctrl` in Chrome and holding
   `Alt` in Chrome are two different tables, not one list filtered two ways — split
   into two zones: the focused app's shortcuts (with its real icon, extracted from the
   executable) and a visually distinct **system-wide** rail. The held key is drawn as
   the large anchor cap in the header, and rows never repeat it: with `Ctrl` held,
   `Ctrl+Shift+N` renders as `⇧` `N`. ★ marks curated everyday picks, sorted first.
3. Add more modifiers → both zones narrow live and emptied groups vanish. Release one →
   it widens. If one zone empties, the other takes the space (holding `Win` naturally
   flips the panel toward system shortcuts).
4. Click a shortcut row → KeyPeek dismisses the overlay, restores focus to the app it
   captured, and sends that combination to it. Press a real shortcut yourself → it
   works normally (KeyPeek never blocks keys) and the overlay gets out of the way.
   If the target app runs as administrator, KeyPeek says so instead of failing
   silently (Windows forbids synthetic input across that boundary).
5. Click the search box → the panel pins open and takes focus so you can type;
   search matches descriptions and keys in both zones ("screenshot" finds
   `Win+Shift+S`). `Esc` or a click outside closes it and puts focus back.
6. Release the trigger key(s) → the overlay fades out. Nothing was typed.

Focused-app resolution handles every state explicitly: known app, unknown app (a
"Create definition file" button drops a starter file in your library), the desktop,
no foreground window at all (system-wide panel, "Windows" header), KeyPeek's own
windows (never triggers), excluded and fullscreen apps (never triggers), and elevated
windows (shown, but marked).

If the hold is interrupted — you press another key, click, scroll, or were mid-drag —
the overlay never appears. An app that fires when you meant to copy something is worse
than no app; this rule (R2) wins every tie.

## Where shortcuts come from — four layers

KeyPeek merges four sources, per shortcut (later layers win, and overriding one entry
never orphans the rest of a definition):

1. **Bundled** — ships inside the exe, read-only, replaced wholesale on app update.
2. **Downloaded** — the community library, fetched over HTTPS on your schedule into
   `%LOCALAPPDATA%\KeyPeek\downloaded`. How corrections reach you without a new build.
3. **Discovered** — read at runtime from apps' own config: the PowerToys/WinGet manifest
   folder (`%LOCALAPPDATA%\Microsoft\WinGet\KeyboardShortcuts`) if present, plus two
   adapters that surface *your own* customisations — VS Code's `keybindings.json` and
   JetBrains IDE keymaps. Adapters supplement curated definitions; they never replace
   them (those files hold only your overrides, not the defaults).
4. **User** — your files in `%APPDATA%\KeyPeek\library`. Never touched by any update.
   Wins every conflict.

The library browser shows which layer each entry came from and can reset your overrides
back to the shipped values. Definitions can carry `VerifiedAgainst` (the app version
they were checked against); when the app you're in is meaningfully newer, the overlay
header shows a quiet "checked against X · you're on Y" note, and every row has a
hover ⚑ that opens a prefilled correction issue — a fast report loop beats clever
detection. If PowerToys' own Shortcut Guide is enabled, KeyPeek offers once (and only
once) to turn it off — two overlays fighting over the Win key helps nobody.

## Editing the shortcut library

Shortcuts live in plain JSON, one file per app, at `%APPDATA%\KeyPeek\library`
(seeded on first run from the files bundled in the exe; the `library/` folder in this
repo holds the same files). Edits are picked up automatically within a second, or use
tray → Reload library.

KeyPeek's native format is the **PowerToys Shortcut Guide YAML manifest** — a manifest
written for KeyPeek works in PowerToys and vice versa, and KeyPeek inherits the whole
community-maintained PowerToys library instead of duplicating it:

```yaml
PackageName: Google.Chrome
Name: Google Chrome
WindowFilter: "chrome.exe"      # process image name; "*" = system-wide
BackgroundProcess: false        # true = shown even when not foreground (system zone)
VerifiedAgainst: "141"          # KeyPeek extension: app version this was checked against
Updated: 2026-08-01             # KeyPeek extension: last review date
Shortcuts:
  - SectionName: Tabs
    Properties:
      - Name: New tab
        Recommended: true
        Shortcut:
          - Win: false
            Ctrl: true
            Shift: false
            Alt: false
            Keys: [ T ]
```

- Files are organised by section with explicit modifier flags; the overlay's
  by-held-modifier view is built in memory at load. A `Shortcut` list with several
  chords is either a multi-step sequence (`Ctrl+K` then `Ctrl+S`) or alternates
  (`Ctrl+L` / `Alt+D`) — the format can't tell them apart, so chords with identical
  modifier sets are treated as a sequence, anything else as alternates.
- Key spellings are tolerant (bare letters, `"<0>"`, `"<Page Up>"`, `Plus`, `Esc`…);
  prose tokens like `"<Underlined letter>"` display but aren't clickable. An empty
  `Keys` string is a bare-modifier chord (the lone `Win` key).
- KeyPeek-only optional fields (`VerifiedAgainst`, `Updated`, `TitleRegex`,
  `AdditionalWindowFilters`) keep the file a valid PowerToys manifest.
- The older KeyPeek JSON formats still load from the user folder, and
  `KeyPeek --convert-yaml <folder>` rewrites them as manifests.
- Malformed entries are rejected **loudly** — file and line in the log, a tray balloon
  (for your own files), and `KeyPeek --validate` — and never take their neighbors down.
- The library window (tray → Open KeyPeek) browses every app, searches across all apps
  at once, creates starter definitions, lists **conflicts** (a chord defined both
  app-side and system-wide, or twice for the same process), shows each entry's layer,
  and resets your overrides to shipped.

## Building from source

Requires only the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — no Visual Studio.

```
dotnet build KeyPeek.sln          # debug build
dotnet test KeyPeek.sln           # 223 unit tests
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1   # single-file dist\KeyPeek.exe
powershell -ExecutionPolicy Bypass -File scripts\package.ps1   # shareable zip
```

The tests cover the parser, matcher, filter, hold state machine, loaders and merger; the
Explore-mode policy (including the assertion that with the feature off no key in any
state is swallowed); panel placement; the shipped library data itself (unknown key
tokens, placeholder text, empty descriptions and markup left in section titles all fail
the build); and a YAML fuzz suite (truncation at every byte, seeded corruption, absurd
sizes) that proves a malformed manifest becomes an error rather than an exception.

The `scripts\verify-m*.ps1` scripts are end-to-end checks that drive the real app with
injected input (they wait for the machine to be idle so they never fight a human for
the keyboard): m1 = hold detection & non-interference, m3 = overlay/focus/Esc,
m4 = live filtering, m5 = multi-trigger & Win-release masking, plus
`verify-leaks.ps1` for launch/quit and overlay cycling.

## Data sources & credits

The bundled library ships the
[Microsoft PowerToys](https://github.com/microsoft/PowerToys) Shortcut Guide manifests
verbatim (The MIT License, Copyright (c) Microsoft Corporation. All rights reserved —
verified against the repo's LICENSE), plus curated Photoshop/Figma manifests written
for KeyPeek where the upstream ones aren't clean, normalized against vendor
documentation. [CtrlHelp's MIT-licensed shortcut data](https://github.com/veler/CtrlHelpApp)
(Copyright (c) 2024 Etienne Baudoux) was reviewed as a source but not copied from.
KeyPeek itself is MIT licensed (see LICENSE).

## App logos

The shortcut library shows each app's own logo. **KeyPeek's package contains no
third-party logos at all**, because redistributing another company's mark is that
company's call to license, not ours — and the usual icon sets are explicit that their own
licence does not cover the individual brands (Simple Icons ships under CC0 "though that
doesn't mean to imply that all icons within the project are also CC0"). So a logo reaches
the list one of three ways, best first:

1. **From the app as installed here.** The icon is read out of the executable — the real
   thing, exact, no network. Found via the App Paths registry key, Start-Menu shortcut
   targets, and running processes.
2. **From the vendor.** For apps that aren't installed, the official icon is fetched once
   over HTTPS from a URL on that vendor's own servers, and cached in
   `%LOCALAPPDATA%\KeyPeek\icons`. The table of URLs is
   [`OfficialIconSources.cs`](src/KeyPeek.Core/OfficialIconSources.cs); every entry was
   fetched and looked at before being added, and the fetcher refuses any host not on that
   file's allow-list. Settings → Library updates → **Download app logos** turns this off,
   and then KeyPeek never makes the request.
3. **A glyph we drew.** A dozen plain strokes each for browser, editor, terminal, vector,
   video, 3D, chat, mail, office, media, files. This is what a row falls back to offline,
   with logos switched off, or for a vendor that publishes no stable icon file (Adobe's
   product icons and Postman's are SVG-only or unreachable, so those apps wear a glyph).

## Known limitations

- Input to **elevated (admin) windows** is invisible to a non-elevated hook, so the
  overlay won't trigger while an elevated app is focused. This is by design — KeyPeek
  refuses to run elevated.
- **AltGr** on European layouts reports itself as Ctrl+Alt; a long AltGr hold can open
  the overlay (harmlessly). Normal AltGr typing is far quicker than the hold delay.
- Clicking a shortcut while physically holding extra modifiers releases those modifiers
  logically for the injected chord; in rare cases Windows may then miss your *next*
  physical chord until you release and re-press the key.
- The panel is a per-pixel-transparent window, which Windows composites on the CPU: its
  first animation frame costs 30–70 ms whatever else is idle. The fade starts partly
  visible so that reads as arrival rather than a stutter, and **Settings → Motion** turns
  it off entirely.
- A **hardware Office or Copilot key** appears in the Windows table as a key cap. KeyPeek
  cannot press it for you (there is no synthesizable key behind it), so those rows are
  reference-only, and the sections say which keyboards have them.
- Multi-monitor and per-monitor DPI are implemented (Per-Monitor V2, monitor of the
  focused window) but could not be verified here — this machine has one display. See
  BLOCKED.md.
- Known open defects from the last adversarial review are listed at the end of PLAN.md.
  None of them consume or delay keystrokes.
- The overlay suppresses itself over borderless-fullscreen apps (games, video) unless
  you enable "Show over fullscreen apps".
