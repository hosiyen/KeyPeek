# DECISIONS — non-obvious choices, what was rejected, and how reversible

- **Stack: C#/.NET 8 + WPF.** Rejected C++/Win32 (all UI hand-rolled, hostile to a
  non-Windows-dev maintainer) and Electron/Tauri (a browser runtime for a tray tool).
  One SDK download, one build command, single-file publish. Reversal: total rewrite — effectively final.
- **The hook is an observer, with exactly one swallowed key.** All input passes through
  (`CallNextHookEx`) except Esc while the overlay is visible (else Ctrl+Esc opens Start
  on dismiss). R2 is enforced structurally: even if KeyPeek's logic thread hangs, typing
  is unaffected because the callback never blocks or filters. Reversal: trivial per key, but
  every added swallow weakens R2 — resist.
- **Win/Alt solo-release masking = inject inert VK 0xFF once at overlay-show.** Same
  technique as PowerToys/AutoHotkey. Rejected swallowing the key-up (leaves OS modifier
  state stuck). Verified live: Win-hold → release does not open Start.
- **Search pins via a real focus grab.** Clicking the search box removes WS_EX_NOACTIVATE
  and activates the panel; focus is restored to the captured hwnd on close. Rejected
  globally intercepting keystrokes while "focused-less" — that is a keylogger pattern and
  violates R2's spirit. Reversal: contained in OverlayWindow.Enter/ExitSearchMode.
- **Shift is not a default trigger.** Holding Shift while typing capitals or selecting is
  normal typing; a Shift-hold overlay would fire constantly. Exposed as an opt-in.
- **Sequences vs alternates heuristic.** The PowerToys manifest format encodes
  VS Code-style sequences and alternate bindings identically (verified against the real
  corpus; no distinguishing field exists). Rule: equal modifier sets on every chord =
  sequence, else alternates. Classifies every known corpus example correctly. Known
  residual risk: same-modifier alternates (e.g. Ctrl+Plus / Ctrl+=) would render as a
  sequence. Reversal: single function in PowerToysManifestLoader.
- **Merge identity is process-based, not PackageName-based.** So the VS Code adapter
  (no PackageName) merges into the bundled VS Code definition. Trade-off: two authored
  definitions for the same process (without titleRegex) merge instead of coexisting —
  the conflict detector flags that case anyway.
- **Discovered layer never supplies app metadata.** Adapters supplement entries; the
  highest *authored* layer (User > Downloaded > Bundled) names the app and owns
  VerifiedAgainst. Found via test: the adapter renamed "Visual Studio Code".
- **Bundled manifests renamed without ".en-US".** MSBuild culture-infers `xx.en-US.yml`
  embedded resources into an en-US satellite assembly, which `SatelliteResourceLanguages`
  then silently drops (15 of 17 manifests vanished). `WithCulture="false"` did not
  survive AssignCulture on this SDK. Renaming kills the inference at the root.
- **User-folder migration deletes only provably-unmodified seed copies.** Old builds
  extracted seeds into the user folder; they would shadow the bundled layer forever.
  Deletion criterion: canonical re-serialization of the parsed file equals the embedded
  legacy seed's. Any edit → file kept (user layer is sacred). One-time, marker file.
- **Downloader: parse-validate everything before an atomic cache swap.** A bad publish
  can never wipe a good cache. Failures are quiet (log only) — an update check must not nag.
  Stamp written even on failure so a broken server isn't hammered.
- **Error balloons only for user-layer files.** The WinGet folder ships one dirty
  Photoshop manifest; ballooning about files the user can't fix is noise. Everything
  still goes to the log and the library browser.
- **Community-library URLs are placeholders** (github.com/keypeek-app/library) until the
  repo exists — creating it is an external action (NEEDS-REVIEW.md). 404s are quiet.
- **InvariantGlobalization must stay OFF** — WPF bindings crash without culture data
  ("Cannot find non-neutral culture"). Do not re-add it for exe size.
- **No warnings policy:** WFAC010 suppressed deliberately (WinForms analyzer objecting
  to the WPF DPI manifest — the manifest is correct for WPF).
- **Prose key tokens load display-only.** "<Underlined letter>" etc. render but are not
  clickable and produce no load errors — erroring would flood on real-world manifests.
- **Curated Photoshop/Figma manifests instead of upstream.** The PowerToys ones are
  scraped prose with modifiers embedded in key strings (verified); ingesting them raw
  would produce garbage rows.

## 2026-08-14 UI pass (session 7)
- **One accent hue across overlay and settings.** The settings window's purple is gone;
  both use KpAccent. Reversal: two token values in the theme dictionaries.
- **Per-key hold delays removed** (user: all trigger keys should hold the same). The
  settings field stays in the record so existing settings.json files still load, but it
  is neither applied nor shown. Rejected: keeping it behind Advanced — an option nobody
  wants is still a maintenance cost.
- **Rows are drawn, not composed.** ShortcutRowElement paints caps + star + description
  + hover flag in one visual because ~7 visuals/row made the first show of a 140-row
  panel take ~900 ms. Cost: click zones are geometric (right 22 px = report) and hover
  colour changes need InvalidateVisual. Reversal: the old DataTemplate is in git history.
- **Conflict rule narrowed to chords Windows intercepts first.** Reporting every app
  chord that also exists system-wide produced 311 findings on the real library, all
  noise (the focused app wins those). Now: Win+…, Alt+Tab, Alt+Esc, Ctrl+Shift+Esc,
  Ctrl+Alt+Del. Reversal: one predicate in ConflictDetector.
- **Usage data is panel clicks only** (tier 2). Counting real chord presses (tier 3)
  would mean inspecting keystrokes; that trade sits with the user, not me — logged in
  NEEDS-REVIEW instead of built.
- **Ghost tray icons** are a Windows behaviour after a force-kill, not an app bug: the
  shell only reaps a dead process's icon when the mouse crosses the tray. Mitigations:
  clean exit paths dispose the icon, scripts use `--quit`, and clean-tray.ps1 sweeps.
- **A "common in most apps" fallback definition** (`Fallback: true`, a KeyPeek-only
  manifest field). An app with no definition still responds to Ctrl+C/F/S, so showing an
  empty app zone was actively misleading. Rejected: (a) merging these into the system
  zone — they are app shortcuts, not OS ones, and the two-zone split is a
  non-negotiable; (b) showing them for every app — a real definition is authoritative
  and duplicating Copy/Paste into it is noise. Labelled honestly ("COMMON IN MOST APPS")
  because some apps will not implement all of them. Reversal: delete the manifest.

## 2026-08-14 P1 Explore mode (session 9)
- **A second swallow flag, not a widened Esc flag.** `InterceptExplore` is separate from
  `InterceptEsc` because their lifecycles differ: Esc stays intercepted while the panel is
  pinned (it must still close it), Explore must NOT — pinned search has real keyboard focus
  and a low-level swallow would block Left/Right/Home/End/Enter inside our own text box.
- **The swallow set lives in Core as a pure function** (`ExplorePolicy`). That is what makes
  the R2 guarantee testable: one test asserts that with Explore off, no vk in any state is
  swallowed, which is the whole "default config is byte-identical" claim in one assertion.
- **Explore keys route before the auto-repeat dedupe** in HoldController. The dedupe exists
  so the state machine sees only real transitions, but a held arrow should keep moving the
  selection; the hook consumes every repeat anyway, so the app never sees them.
- **Selection is indexed over the view-model, not the visual tree.** OverlayWindow.Apply
  defers the system zone's containers by a frame, so any container walk would miss them.
  Rejected: a ListBox per card (the drawn-row perf work exists precisely to avoid that).
- **Frequent-strip chips are not keyboard-selectable in v1.** They use a different template
  that cannot render the row highlight; selecting an invisible thing is worse than not
  selecting it. Reversal: give the chip container its own selected visual.
- **Log lines use ASCII modifier names.** The filter line printed "⇧" after the UI redesign,
  which silently broke verify-m4 for several sessions (PS 5.1 also reads the BOM-less log
  as ANSI). The log is a machine interface; the UI keeps its glyphs.
- **Harness scripts resolve the SDK by asking `--list-sdks`.** A runtime-only dotnet on PATH
  made every script fail depending on the caller's environment.

## 2026-08-14 P2 onboarding + panel position (session 9b)
- **The welcome flag is written before the window opens.** If the dialog throws, or the
  user kills the app while it is up, the alternative is greeting them on every launch —
  far worse than a first-run window someone never read.
- **Existing installs see the welcome once.** `OnboardingShown` deserializes false from
  files that predate it. Considered suppressing it for upgrades; rejected — nobody has
  ever been told how this app works, and once is cheap.
- **`firstRun = !File.Exists(settings.json)` was dropped.** Load() writes defaults when the
  file is missing, so a missing file already implies the flag is false; keeping the probe
  would have been dead code that reads like a second gate.
- **Centre is 40% of the slack, not 50%.** That is the placement KeyPeek has always used
  and it is what the eye reads as centred; a "centre" option that moved the default panel
  would have been a silent regression for anyone who liked it where it was.
- **Placement is a pure function in Core** taking work area/panel height/margin. The rules
  (top/bottom margins, clamping when the panel is taller than the screen, negative work-area
  origins on a monitor above the primary) are then testable without a display — this machine
  cannot verify multi-monitor at all (BLOCKED T12).
- **Title-bar theming extracted to UI/TitleBar.** It was private to SettingsWindow, so every
  new dialog silently shipped a white title bar in dark mode.

## 2026-08-14 P3 first-show latency (session 9c)
- **Warm on focus change, not on hold.** The work cannot be made cheap — ~150 row elements
  must exist and measure their text — so it moves off the critical path instead. Cost: one
  ~300 ms UI-thread burst per app switch, at Background priority, in KeyPeek's own process.
- **Warmed identity includes the window title.** TitleRegex-selected definitions mean the
  same hwnd can need different content after a tab switch, and that produces no foreground
  event. Strict match: a miss costs milliseconds, a false hit is wrong content.
- **Measure the panel, not the window.** An unshown WPF Window is Visibility.Collapsed and
  its subtree is skipped by layout, which is why the obvious `_window.Measure(...)` warmed
  nothing (measured: 18 ms warm, show still 452 ms).
- **Apply() gained `deferSystemZone`.** The deferral that makes the panel appear sooner on
  the show path silently defeated warming, because precompute also runs at Background
  priority. Same method, opposite priority — worth the parameter.
- **The watcher primes itself once at startup.** It only reports changes, so without this
  the app the user was already in — the most likely one — was the only cold app.
- **The show path never depends on the cache.** On a miss it does exactly what it did
  before; correctness is unchanged whether or not warming ran.

## 2026-08-14 P4 editor + UI pass (session 9d)
- **A user manifest is a clone of the merged app, never a fragment.** LibraryMerger takes
  metadata from the highest authored layer, so a minimal user file silently blanks the app
  name and the VerifiedAgainst badge. Pinned by a test that merges and asserts both survive.
- **Chord capture refuses what it cannot spell.** Numpad and media keys have no canonical
  name in the format; accepting them would write files that load as prose. The editor caps
  sequences at 3 too — only the parser enforced that, and the dialog never calls it.
- **The driver was not DPI-aware.** Every screenshot in this project's history was the
  top-left 80% of the screen, which is why four layout defects survived several UI reviews.
  Screenshots are evidence; evidence that silently crops is worse than none.
- **Panel height is content-driven, window size is not.** The zone row became Auto (capped
  by MaxHeight): the panel shrinks as the filter narrows, while the window keeps the size it
  was given at show time — no mid-hold SetWindowPos, so the anti-jitter rule still holds.
- **Zone slack is 26 DIP, not 8.** A scrollbar is ~17 DIP and stole exactly enough width to
  wrap the last card onto its own row, leaving half the zone empty. Apply and PositionWindow
  share the constant so they can't drift apart.
- **Key column is per card, not global.** A fixed 168 px wasted ~90 px in cards of
  single-letter shortcuts and truncated their descriptions; each card now sizes to its own
  widest row (clamped 70–190) using the same cached cap widths the rows draw with.

## 2026-08-14 P5/P6 + data sweep (session 9e)
- **UIA harvesting closed as a negative result.** 3 shortcuts from Edge, 0 from five other
  apps. Accelerators live on menu items that exist only while a menu is open, and Electron
  and WinUI apps expose none. The only way up is driving someone else's UI, which KeyPeek
  will not do. Kept as `--harvest` for anyone who wants to re-measure on other hardware.
- **A drawn row still owes automation an identity.** ShortcutRowPeer names rows from
  ChordData, not from the drawn labels: those are glyphs (⇧ ⏎ ↑) chosen for the eye, and a
  screen reader either skips or mispronounces them. Invoke goes through a command so the
  peer cannot bypass the Executable guard that a mouse click respects.
- **High Contrast overrides the user's own transparency setting.** Someone who turns HC on
  has told the OS they need maximum contrast; honouring a 20% transparency slider over that
  would be obedient and wrong.
- **One key per cap.** The format lets a chord list several keys; joining them into
  "Office+W" produced a single cap that read like a key nobody owns. Each key now draws its
  own cap, modifiers stay on the first, and the entry is explicitly not-alternatives.
- **The library gets tests too.** BundledLibraryTests fails the build on unknown key tokens,
  placeholder text ("example", "replace me"), empty descriptions and `<...>` left in section
  titles. Data defects are invisible in code review and obvious to a user on day one; the
  allowlist of display-only tokens is deliberately small so adding one is a decision.
- **YamlDotNet deserializers are per-thread now.** The shared static instance was parsed
  from the file watcher, the downloader and startup concurrently — a data race that
  surfaced as "invalid YAML" on files that were fine.
- **Starter files ship empty.** An "Example — replace me" entry loads exactly like a real
  shortcut and appears in the panel; the create-definition path now opens the editor.

## 2026-08-14 smoothness + review sweep (session 9f)
- **A transparent WPF window cannot animate cheaply.** AllowsTransparency forces CPU
  compositing; the first frame after Show costs 32–77 ms whatever else is idle (measured
  with a frame probe now kept in the code, logging only when a gap exceeds 55 ms). So the
  fade starts at 35% instead of 0: the cost is unchanged, but it stops reading as a hitch.
  Rejected: sizing the window to the panel to shrink the repainted surface — it clipped the
  footer and the timings did not move.
- **KeyPeek's Motion toggle overrides Windows' "show animations" setting.** That setting is
  off on plenty of machines for reasons unrelated to one small panel, and a user asking for
  an animation should get one. High Contrast still forces it off.
- **The warm token is dropped on hide, not maintained.** Every in-hold action (filter,
  search, pin, click) replaces the rendered tree; tracking all of them would be a standing
  invitation to miss one, and missing one shows the previous hold's content. Rebuilding
  after each hide costs ~300 ms of idle time and cannot go stale.
- **The hook tracks per-key ownership.** Swallowing a release for a press the app already
  received latches that key down inside the app — the worst class of bug this project can
  ship, and the Esc half of it was live by default. Rule: never take a key mid-press, and
  only swallow a release whose press was also swallowed.
- **Empty manifests are valid; manifests that declare shortcuts and produce none are not.**
  The first is a file the app itself creates before there is content; the second is broken
  data. Conflating them made "Add app" produce an app that never appeared.
- **The panel reserves 265 DIP of chrome, not 190.** Over-reserving costs a little empty
  space; under-reserving puts the footer outside the window, which is a control the user
  cannot reach.

## 2026-08-15 last plan items + UI polish (session 10)
- **Only `WindowFilter: "*"` publishes shortcuts system-wide.** BackgroundProcess says the
  app keeps running when unfocused; it says nothing about which of its shortcuts work
  elsewhere, and nothing in the format does. Treating it as global put every Telegram
  shortcut in every other app's panel.
- **The library is audited merged, not per file.** Both data defects a user found were
  invisible in a single manifest and obvious in the merged view, so the guard tests now
  build the same view the panel does.
- **A row knows which definition it came from.** The ⚑ report used the focused app, which
  is wrong for every system-wide row.
- **`Sequence: true` is written explicitly.** The upstream format cannot distinguish a
  sequence from alternatives, and the modifier heuristic guesses wrong for a sequence whose
  later steps carry no modifier — exactly what the editor produces.
- **High Contrast never fills a row with the highlight colour.** The description is drawn in
  WindowText; filling underneath it gives ~1.35:1, in the mode that exists for contrast.
  The selected row is marked by its accent bar instead.
- **Descriptions wrap to two lines rather than being cut.** A list you have to hover to read
  is not a list. Rows measure at the width they are actually given, so a short row does not
  reserve a line it never draws.
- **A too-wide panel drops an app column instead of clamping.** The clamp took the whole
  shortfall out of the star-sized rail and clipped its cards.

## 2026-08-15 pin semantics (session 10d)
- **A click pin is a pause; a search pin is a mode.** Holding a trigger takes a click-pinned
  panel back under keyboard control (filter live, close on release) because the click only
  ever meant "stop closing while I read". A search pin must survive modifiers, or Ctrl+A in
  the query box would throw the user out of search. The machine now records which kind of
  pin it is rather than treating both the same.

## 2026-08-17 official logos + measuring leaks (session 11)
- **KeyPeek's package contains no third-party logo.** A brand mark inside our installer is
  redistribution, which is the mark owner's call to license; the icon sets that look like a
  solution disclaim it themselves (Simple Icons is released under CC0 "though that doesn't
  mean to imply that all icons within the project are also CC0"). So the logos are fetched
  on the user's machine, from the vendor that owns the mark, and cached there — the same act
  as a browser showing a favicon. Order of preference is the app as installed here first,
  vendor second, our drawn glyph last. Reversal: delete `OfficialIconSources` and the app
  falls back to glyphs with no other change.
- **Every logo URL was fetched and looked at before it was added.** Two vendors returned
  byte-identical generic files and one candidate "Adobe" GitHub avatar turned out to be a
  private individual's photograph. A table of plausible URLs is not evidence; the image is.
- **A vendor with no reachable raster icon gets our glyph, not a substitute.** Adobe's
  product-icon paths time out and Postman/Publisher publish SVG only (WPF cannot decode
  SVG). Showing some other company's art there would be worse than an honest drawing.
- **Design tools split into paint / vector / video / 3D / layout.** One shared pencil across
  five Adobe rows reads as "no icon at all", which is the exact problem the glyphs exist to
  solve. Tiles also vary up to ±24° off their category hue so two neighbours sharing a glyph
  are not the same colour.
- **Leak checks measure private bytes, GDI and USER objects — never working set.** KeyPeek
  is a 63 MB single-file exe, so its working set is mostly mapped image pages plus whatever
  the OS has not trimmed; it moved tens of MB between samples on an idle machine and made
  the gate fail on a healthy build. The baseline is also taken after five warm-up cycles,
  because the first show legitimately builds the whole visual tree and that is not a leak.
- **The managed heap is logged on every hide.** "Is it leaking?" is otherwise unanswerable
  from outside the process, since private bytes sawtooth and drift upward with no memory
  pressure and look exactly like a slow leak. The heap number settles into a band (6–11 MB
  here) if nothing is retained, and one grep answers the question.

## 2026-08-17 Vietnamese UI + full corpus (session 11b)
- **English is the translation key.** `L10n.T("Open log")` reads as what the UI says, a
  missing entry degrades to English (never a resource token), and the XAML stays literal.
  The cost — renaming a string means updating the table — is the point: the table cannot
  silently drift from the UI.
- **Static XAML is translated by walking the rendered tree, not by duplicating markup.**
  The table is bidirectional, so a language switch re-translates text already on screen,
  live. Strings the walk does not recognise (user input, app names, key caps) are left
  alone by construction. One rule this imposes: no two English keys may share one
  Vietnamese face, or the reverse walk picks arbitrarily — a test enforces it.
- **Shortcut descriptions stay English.** They are data from the library corpus, ~2,900
  strings maintained upstream. Machine-translating them ships subtly wrong Vietnamese and
  breaks against every library update; "phần chữ của chính KeyPeek" is the honest scope.
- **Language chips are named in their own language** ("Tiếng Việt", "English") — a user
  stuck in the wrong language must be able to read the way out.
- **The loader folds modifier words out of key tokens** (`Keys: ["Ctrl Shift W"]`, flags
  false → Ctrl+Shift+W). Upstream After Effects writes 126 rows that way; taken literally
  they render one wide fake cap and can never run. Never folds the last word: an
  all-modifier token stays display-only rather than becoming a bare-modifier chord that
  click-to-run would send.
- **Test heuristics must not punish honest upstream data.** "example" flagged "for
  example…" descriptions; zero-unknown-tokens flagged Adobe's real prose ("Pen tool",
  double-tap "UU"). Both tests now encode what they actually mean: placeholder SHAPES, and
  short-all-caps typo-shaped tokens.
