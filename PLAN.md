# PLAN — KeyPeek execution plan v3 (authored 2026-08-14, for the next session to run)

> **Executor: Claude Opus 5, working autonomously.** Read this file top to bottom
> before touching code. Then read PROGRESS.md (what happened, newest first) and
> DECISIONS.md (why things are the way they are — do not relitigate them). The
> roadmap and its order were approved by the user; every step below was written
> against the real code at commit `784aba7` with exact symbols verified.

---

## 0. Operating rules

- **Autonomous loop.** Execute milestones in order P1 → P6. No approval gates.
  Commit directly to `main` — the user explicitly said no branches.
- **Verification replaces approval.** A milestone is done only when the
  Definition-of-Done checklist (§8) passes. Never report done without running it.
- **Stop and ask the user ONLY for:** spending money; anything leaving this
  machine (publishing, creating repos, uploading); destructive actions outside
  this project; unclear licensing; product-identity changes; a non-negotiable
  proving impossible. Queue the question in NEEDS-REVIEW.md and continue with the
  next unblocked item.
- **Management files:** append to PROGRESS.md each session (newest first); record
  non-obvious choices in DECISIONS.md; user questions go to NEEDS-REVIEW.md;
  blockers to BLOCKED.md.
- **User-facing summaries in Vietnamese.** Code, comments and docs stay English.

### Non-negotiables

1. **R2 — never interfere with normal typing.** The LL keyboard hook is an
   observer: it always calls `CallNextHookEx`, never blocks, never adds latency.
   The single swallowed key today is Esc-while-overlay-visible. P1 adds a
   *sanctioned, opt-in* interception scoped to panel-visible only — with default
   settings the hook behavior must remain **byte-identical** (P1 adds a test that
   pins this; keep it green forever).
2. **Two-zone separation.** App shortcuts and system-wide shortcuts never merge
   into one list.
3. **Focus-detection honesty.** Known app / unknown app (fallback) / desktop /
   no window / KeyPeek itself / excluded / fullscreen / elevated — each has
   defined behavior; don't regress any (see ForegroundApp record fields).
4. **On-disk format is PowerToys Shortcut Guide YAML** plus documented KeyPeek
   extension fields (`Fallback`, `VerifiedAgainst`, `Updated`, `TitleRegex`,
   `AdditionalWindowFilters`). Do not invent another format.

### Environment & commands (all verified on this machine)

- Windows 10 Pro 19045, **PowerShell 5.1** — read §7 pitfalls before writing any
  script. The user is not a Windows dev; keep scripts runnable as
  `powershell -ExecutionPolicy Bypass -File scripts\<name>.ps1`.
- .NET 8 SDK is **user-scope**: before any `dotnet` call set
  `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"` and prepend it to
  PATH (canonical copy: `scripts\verify-m1.ps1` lines 7–13; every
  dotnet-invoking script has it — install/uninstall/clean-tray don't call
  dotnet and don't).
- Build `dotnet build KeyPeek.sln -c Debug -v q --nologo` · Test
  `dotnet test KeyPeek.sln -v q --nologo` (132 green today; count only goes up).
- **Quit before building** (locked binaries): run `KeyPeek.exe --quit` for BOTH
  the dev binary (`src\KeyPeek\bin\Debug\net8.0-windows\`) and the installed one
  (`%LOCALAPPDATA%\Programs\KeyPeek\`), wait ~3 s. `--quit` retries internally
  for 4 s if it races a fresh launch. Avoid `Stop-Process -Force`; force-kills
  leave ghost tray icons (sweep with `scripts\clean-tray.ps1`).
- Publish/install: `scripts\publish.ps1` → `dist\KeyPeek.exe` (~63 MB), then
  `scripts\install.ps1` (per-user, no admin). Reinstall at the end of every
  milestone so the installed copy matches main.
- Live verification: `tools\KeyPeekDriver` — grammar:
  `down <key> | up <key> | press <key> | sleep <ms> | foreground | focus <proc> |
  shot <path> | waitidle <idleSec> <timeoutSec>`, commands `;`-separated in one
  quoted arg. Named keys today: ctrl rctrl shift rshift alt ralt win esc tab
  space enter f13 f14 f15 + single ASCII letters/digits — **no arrows yet** (P1
  adds them; P4 adds f1–f24). ALWAYS prefix injection with `waitidle` — and
  know its limit: on timeout it prints `not-idle-timeout` and the rest of the
  command string injects ANYWAY. Scripts must check stdout for `idle` and
  abort/retry on `not-idle-timeout` (verify-leaks does; the m-series skeletons
  don't). App log: `%LOCALAPPDATA%\KeyPeek\logs\keypeek.log`.
- **Log lines are the verification API.** Scripts assert exact strings
  (`hooks installed`, `Hold detected.*<app>`, `Overlay shown (<app>)`,
  `Overlay hidden (TriggerReleased|EscPressed)`, `Overlay filter → <mods>`,
  `Executed shortcut: <chords>`, `Global hooks uninstalled`,
  `KeyPeek exited cleanly`, `Community library updated: N definitions`,
  `Library: N apps`). Renaming any of these breaks scripts silently — don't.
- Screenshots for UI work → `docs/ui-review/<yyyy-MM-dd>/`.

---

## 1. P1 — Explore mode ✅ DONE (session 9, commit e35f45b)

> Shipped as specified, with three deviations worth knowing:
> (a) type-to-filter stayed out of scope (v1 = navigation only, as planned);
> (b) the `readedit` driver verb was NOT added — R2 rests on the ExplorePolicy
> default-off test plus verify-m1 7/7, and a delivery assertion is still worth
> adding when someone next touches the hook;
> (c) three pre-existing harness defects were fixed along the way (SDK
> resolution, verify-m1 idle gating, ASCII log labels) — see PROGRESS.
> Original spec kept below for reference.

### (original spec)

**User story:** while the overlay is open, ↓/↑/←/→ move a selection, Enter runs
the selected shortcut, Esc closes (already works) — no mouse.

**Accepted design (spec F1):** Reveal (today's non-blocking mode) stays default.
`ExploreMode` is a Settings toggle, default false. Interception is active ONLY
while the panel is visible. Default config = hook behavior byte-identical.

### Architecture you are extending (verified)

- Hook: `src/KeyPeek/Hooks/KeyboardMouseHookService.cs`. `KeyboardCallback`
  (~97–129) writes `InputEvent.Key(vk, isDown)` to the channel **before** the
  swallow decision, then swallows ONLY Esc when the static
  `public static volatile bool InterceptEsc` (~36) is true, pairing the key-up
  via hook-thread-only `_escDownSwallowed` (~52).
- `InterceptEsc` is toggled in exactly two places, both on the dispatcher:
  `OverlayPresenter.ShowCore` (~256, set true after `_window.Show()`) and
  `HideCore` (~268, set false). It intentionally stays true while Pinned (the
  doc comment at ~34 saying "not pinned" is wrong — do NOT "fix" the flag; Esc
  must close a pinned panel).
- Consumer: `HoldController.HandleKey` (~167–219) — MaskVk 0xFF filter first,
  auto-repeat dedupe (`_downVks`), Esc → `OnEscDown()`, other non-modifier downs
  → `_machine.OnOtherKeyDown()` (which, in Showing, hides the overlay).
- State machine (`src/KeyPeek.Core/HoldStateMachine.cs`) is pure and fully
  tested through fake `IHoldActions`.

### Steps

1. **Setting.** `KeyPeekSettings` (src/KeyPeek/Services/SettingsService.cs,
   record at lines 8–62): add `public bool ExploreMode { get; init; } = false;`.
2. **Pure policy in Core** so it's unit-testable:
   `src/KeyPeek.Core/ExplorePolicy.cs` — static class with
   `bool IsExploreKey(int vk)` (set membership) and
   `bool ShouldSwallow(int vk, bool overlayVisible, bool exploreEnabled)` (=
   `overlayVisible && exploreEnabled && IsExploreKey(vk)`; the hook calls this
   with its two flags, tests call it with every combination). Documented swallow
   set (standard Win32 VKs): Up(0x26) Down(0x28) Left(0x25) Right(0x27)
   Enter(0x0D) PageUp/VK_PRIOR(0x21) PageDown/VK_NEXT(0x22) Home(0x24)
   End(0x23). **v1 scope: no printable characters, no Backspace** —
   type-to-filter needs the search box's focus model and is a separate later
   step; keep v1 shippable.
3. **Hook.** Next to `InterceptEsc` add
   `public static volatile bool InterceptExplore` (same volatile pattern). In
   `KeyboardCallback`, after the existing channel write, add one branch: if
   `InterceptExplore && ExplorePolicy.ShouldSwallow(vk, true, true)` swallow the
   down AND record vk in a hook-thread-only `bool[256] _exploreDownSwallowed` so
   the matching key-UP is swallowed too (generalize the `_escDownSwallowed`
   pattern — orphan key-ups confuse apps). Handle WM_SYSKEYDOWN/WM_SYSKEYUP the
   same as the existing code does (Alt-held explore keys arrive as SYS
   messages). The callback stays allocation-free and never blocks.
4. **Flag lifecycle.** Set `InterceptExplore = _settings.Current.ExploreMode` in
   `OverlayPresenter.ShowCore` right where `InterceptEsc = true` is set; clear
   in `HideCore`. Never drive it from machine state — `HoldController.
   ShowOverlay` can bail (IsSelf/excluded/fullscreen) leaving State==Showing
   with no overlay on screen. **Pinned mode: unlike InterceptEsc, this flag
   must ALSO be cleared in `OnSearchClicked` (~290–299), where `_pinned` is
   set** — a LL swallow blocks keys system-wide including KeyPeek's own search
   TextBox, so leaving it on would make Left/Right/Home/End/Enter dead inside
   the pinned search box. Pinned mode is mouse+search; Explore is for the held
   state only.
5. **Routing.** In `HoldController.HandleKey`: if
   `KeyboardMouseHookService.InterceptExplore` and the vk is in the explore set,
   call a new presenter method instead (add to `IOverlayPresenter`:
   `void ExploreKey(int vk)`; presenter marshals via `_dispatcher.InvokeAsync`
   exactly like `UpdateFilter`). Do NOT let these reach `OnOtherKeyDown` — that
   would dismiss the overlay. **Place this branch BEFORE the auto-repeat dedupe
   (`if (isDown && !_downVks.Add(vk)) return;`, ~172–176)** — the hook swallows
   every OS auto-repeat of a held arrow, so routing after the dedupe would move
   the selection exactly once per physical press-and-hold; before it, holding ↓
   scrolls the selection like any list UI. Known benign race: the hook flag is
   set a beat after Show; tolerate a leading unswallowed arrow (it scrolls the
   app once) rather than trying to synchronize threads.
6. **Selection model** (dispatcher-thread state in OverlayPresenter, next to
   `_held`/`_query`): a flat index over the *built* OverlayVm entries in visual
   order — **AppZone cards then SystemZone cards, SKIPPING the Frequent strip**:
   its chips use a different template (Border + KeyCapsElement, not
   ShortcutRowElement), so they cannot show the step-7 highlight; in v1 chips
   stay mouse-only. Index the VM, never the visual tree — `OverlayWindow.Apply`
   deliberately defers SysCards fill to a Background-priority dispatch when both
   zones are present. On every RefreshContent, reset selection to the FIRST
   zone-card entry (so Enter always has a target) and clamp to list length.
   **Key semantics (define all nine — a swallowed key must never be inert):**
   ↓/↑ = ±1 entry; ←/→ = first entry of previous/next section card (wrapping);
   Home/End = first/last entry; PageUp/PageDown = ±10 entries (clamped);
   Enter = execute selected.
7. **Selection visual.** Add a `bool IsSelected` DependencyProperty to
   `ShortcutRowElement` (AffectsRender) and paint a KpHover/KpAccentSubtle
   background in OnRender — the element already InvalidateVisual()s cheaply on
   hover. Scroll it into view via `FrameworkElement.BringIntoView()` on the
   selected container. **Never** call SetWindowPos / resize mid-hold (jitter
   rule, DECISIONS).
8. **Enter executes** through the existing path only: presenter checks
   `selectedVm.Executable` FIRST (the guard lives in
   `OverlayWindow.EntryRow_Click`, NOT in `OnEntryClicked` — calling
   OnEntryClicked unguarded would execute display-only prose rows), then calls
   `OnEntryClicked(selectedVm)` → `ExecuteRequested?.Invoke(vm.ChordData)` →
   `HoldController.RequestExecute` (keeps the elevated-target refusal, the
   pinned-vs-hold delay, and usage recording). No second execute path.
9. **Settings card** (SettingsWindow, PageSettings): toggle-row pattern (copy
   the ShowFrequentlyUsed card), honest copy: "While the panel is open, arrow
   keys and Enter are consumed by KeyPeek." Add ("Explore mode", 1) to
   `SettingsIndex` (SettingsWindow.xaml.cs lines 45–50) so search finds it.
10. **Driver support:** add `up`,`down`,`left`,`right`,`pgup`,`pgdn`,`home`,
    `end` to the `Keys` dictionary in tools/KeyPeekDriver/Program.cs (~line 16)
    with the VKs from step 2.
11. **Tests** (KeyPeek.Tests):
    - Byte-identical guarantee: for every vk 0..255 × {visible,hidden},
      `ExplorePolicy.ShouldSwallow(vk, v, exploreEnabled:false) == false`.
    - Explore-on: swallow set is exactly the step-2 list and only while visible.
    - Selection-model: wrap/clamp, filter shrink, empty panel, Enter on
      non-Executable row is a no-op.
12. **Live verify:**
    - Know what verify-m1 actually checks: hold-detection true/false positives
      and clean shutdown, via log lines ONLY — it cannot detect key swallowing.
      The byte-identical R2 guarantee rests on the step-11 unit tests plus a
      NEW delivery assertion: add a driver verb `readedit` (WM_GETTEXT on the
      foreground window's Edit child — classic Notepad has one) and assert
      that letters typed into Notepad with Explore OFF, and with Explore ON
      while the panel is closed, actually arrive in the buffer.
    - Explore OFF → run `scripts\verify-m1.ps1` — must pass unchanged.
    - Explore ON: SettingsService has NO file watcher — flipping requires quit
      KeyPeek → edit `%APPDATA%\KeyPeek\settings.json` → relaunch (restore the
      user's original file when done; verify-m6.ps1 shows the rewrite/restore
      pattern). Then driver:
      `waitidle 10 240; focus notepad; down ctrl; sleep 900; press down; press down; shot <path>; press enter; up ctrl`
      — the `shot` must come BEFORE `press enter` (execute hides the overlay).
      Assert the log prefix `Executed shortcut:` (from ShortcutExecutor; not in
      the §0 list — add it there when touching this). Pick the presses so the
      selected row is harmless (first Notepad rows are New tab/New window).
    - New `scripts\verify-explore.ps1` following the verify-m5.ps1 skeleton
      (LogMark/LogTail, Assert helper, quit-before-build, retries) — and abort
      or retry the run when the driver prints `not-idle-timeout` (waitidle
      gives up and injects anyway; the skeleton does not check this).

**DoD extras:** DECISIONS entry (policy design, v1 scope without typing);
NEEDS-REVIEW note asking the user whether type-to-filter is wanted next.

---

## 2. P2 — Onboarding + panel position ✅ DONE (session 9b)

> Shipped. Deviations: the `firstRun` file-existence probe was dropped (the
> verification pass proved it decides nothing — `OnboardingShown` alone is the
> gate); placement arithmetic lives in Core as `PanelPlacement` so it is unit
> tested rather than only screenshotted. Original spec below.

### (original spec)

### 2a. First-run welcome

1. Signal: add `public bool OnboardingShown { get; init; } = false;` to
   KeyPeekSettings (the `PowerToysOfferShown` precedent); show when
   `!OnboardingShown`, set it true afterwards via SaveAndApply. (A
   `File.Exists(SettingsPath)` first-run probe adds nothing — Load() writes
   defaults when the file is missing, so a missing file already implies the
   flag is false.) Expected consequence, by design: EXISTING installs — the
   user's live install included — deserialize the flag as false and see the
   welcome exactly once after this ships. Note it in the session summary so
   the user isn't surprised.
2. Skip entirely for the non-UI arg paths (`--quit`, `--validate`,
   `--convert-yaml`, `--migrate` — they Shutdown before services) and when
   launched with `--settings`.
3. Window: copy the AddAppDialog window pattern (ResizeMode=NoResize,
   CenterScreen here, ShowInTaskbar=False, Background KpBg, Foreground
   KpTextBody, Segoe UI, UseLayoutRounding, TextFormattingMode=Display). Content:
   app icon + three rows, each an icon + KpBody line — "Hold **Ctrl** for half a
   second to see the shortcuts of the app you're in", "Click any row to run it",
   "KeyPeek lives in the tray — double-click for settings" — footer
   `[Open Settings]` (KpButton) + `[Got it]` (KpButtonPrimary, IsDefault).
   Render the Ctrl cap with `KeyCapsElement`, not text. NOTE: dialogs do not get
   the dark title bar automatically — replicate SettingsWindow.
   `ApplyTitleBarTheme()` (DwmSetWindowAttribute) in OnSourceInitialized.
4. Fire at `DispatcherPriority.ApplicationIdle` (the PowerToysCoexistence.
   MaybeOffer precedent, App.xaml.cs ~162) and AFTER that offer so two dialogs
   never stack: chain both in one ApplicationIdle continuation.
5. Test: unit-test the show/skip decision table (OnboardingShown × args).
   Live: **quit BOTH instances first** (dev binary AND
   `%LOCALAPPDATA%\Programs\KeyPeek` — a second launch hits the
   single-instance mutex and shows a blocking "already running" MessageBox
   instead of onboarding), back up `%APPDATA%\KeyPeek\settings.json`, delete
   it, launch, screenshot, relaunch to verify it doesn't show again, then
   restore the backup WITH `"OnboardingShown": true` added (the backed-up file
   predates the flag; restoring it verbatim would pop the dialog at the user's
   next real launch).

### 2b. Panel position option

1. Setting: `public string PanelPosition { get; init; } = "center";` — values
   "top" | "center" | "bottom" (settings enums are lowercase strings by
   convention here, like `Theme`; validate in Load() with fallback "center").
2. Apply: the ONLY choke point is `OverlayPresenter.PositionWindow` (~477–499).
   The vertical rule is one line (493, quoted exactly):
   `int y = _monitorInfo.rcWork.Top + Math.Max(0, (int)((workH - cy) * 0.40));`
   Replace the factor: top → `0.06`, center → `0.40` (today's look), bottom →
   anchor `_monitorInfo.rcWork.Bottom - cy - margin` where
   `margin = (int)(24 * _scale)` (`rcWork` is a field of `_monitorInfo`, not a
   local).
   Everything else (monitor of the focused window, work-area clamp, physical px,
   `_scale` from GetDpiForMonitor) stays untouched. PositionWindow runs once per
   show from ShowCore — no other call site exists; do not add one.
3. UI: segmented control = three `KpCapToggle` ToggleButtons ("Top", "Center",
   "Bottom") in the Appearance card, one shared Click handler +
   `SyncPositionButtons(string)` — copy the Theme_Click/SyncThemeButtons pattern
   (SettingsWindow.xaml.cs ~283–289, ~361–369). Persist via
   `Persist(s => s with { PanelPosition = value })`. Add to `SettingsIndex`.
4. Live verify: driver screenshots of all three positions over Notepad. Each
   flip = quit → edit settings.json → relaunch (no watcher; see P1 step 12) —
   restore the user's file afterwards. Confirm the panel-rect hit test still
   dismisses correctly (clicking outside) — `UpdatePanelRectSoon` already
   recomputes after Apply; no change expected.

---

## 3. P3 — First show <100 ms via focus-change precompute (closes T2b)

Today: `OverlayPresenter.Preload()` warms the window + Win/Ctrl system tables at
startup (~20–60 ms warm shows), but the FIRST show for an app whose content was
never built is ~220 ms (was 900+). Bar: <100 ms cold.

**Design: build content when the user switches apps, not when they press.**

1. **Measure first.** Instrument one cold show with temporary Stopwatch logs
   split across `ResolveContent` (AppMatcher + IconExtractor), `RefreshContent`
   (LINQ/VM build), and layout — on the real show path layout happens inside
   `_window.Show()` (ShowCore ~252), NOT in Apply (the only explicit
   Measure/Arrange in the codebase is in `Preload()`, ~86–87); put the layout
   stopwatch around `_window.Show()`/`PositionWindow`. Do NOT reuse
   `OverlayTiming` (single-writer contract). Optimize what the numbers say —
   the cache below targets ResolveContent+RefreshContent; if layout dominates,
   extend the warm Measure/Arrange trick from Preload() instead.
2. **Foreground watcher.** No SetWinEventHook exists anywhere yet (verified).
   Add to `src/KeyPeek/Interop/NativeMethods.cs` (near GetForegroundWindow,
   ~113): `SetWinEventHook`/`UnhookWinEvent` P/Invokes, `WinEventDelegate`,
   `EVENT_SYSTEM_FOREGROUND = 0x0003`, `WINEVENT_OUTOFCONTEXT = 0x0000`. New
   `src/KeyPeek/Services/ForegroundWatcher.cs`: installs the hook on the WPF UI
   thread (WINEVENT_OUTOFCONTEXT delivers on the installing thread's message
   loop, which the Dispatcher pumps — NOT on the "KeyPeek.Hooks" thread, whose
   docstring commits it to LL-hook work only). **Keep the delegate in a field**
   — GC of the thunk crashes the process on the next event (same rule as the
   `_keyboardProc` fields). Construct in App.OnStartup AFTER the presenter
   (line ~140) — the watcher needs `presenter.Precompute` to wire its callback,
   so "right after ForegroundAppService" doesn't compile. Dispose in OnExit and
   CleanupHooks, but make Dispose tolerate a failed unhook: CleanupHooks runs
   from ProcessExit/UnhandledException on arbitrary threads, and
   `UnhookWinEvent` fails cross-thread (unlike UnhookWindowsHookEx — that
   precedent does NOT transfer). The OS removes WinEvent hooks at process death
   anyway.
3. **Debounce** with a DispatcherTimer restarted on every event, firing after
   ~200 ms quiet. On tick call `ForegroundAppService.Capture()` — never trust
   the event's hwnd/pid (ApplicationFrameHost resolves to the real UWP child
   only once it exists; bursts happen on Alt-Tab).
4. **Presenter handoff.** New method on OverlayPresenter (and
   IOverlayPresenter): `void Precompute(ForegroundApp app)` — marshals via
   `_dispatcher.InvokeAsync(..., DispatcherPriority.Background)`, guards
   `if (_visible) return;`, skips `app.ProcessName == ""` (that's Preload's
   synthetic identity — don't poison the cache), then runs `ResolveContent(app)`
   + `RefreshContent()` + the Preload-style Measure/Arrange. Deliver the hint
   directly App→presenter — NOT through the InputEvent channel (that path is
   input state, not content prep).
5. **Show-path reuse.** In `ShowCore`, skip `ResolveContent` when the incoming
   app matches what was precomputed — compare Hwnd AND ProcessName **AND
   Title**, because `AppMatcher.FindForProcess` disambiguates by TitleRegex
   against the window title, and titles change (tab switches) without any
   EVENT_SYSTEM_FOREGROUND firing; a Hwnd+ProcessName match could otherwise
   serve stale content. (Cheaper alternative: skip only when no definition for
   that process carries a TitleRegex.) The at-hold `_foreground.Capture()` in
   `HoldController.ShowOverlay` stays authoritative — its BEFORE-overlay
   ordering is documented load-bearing (R3). The watcher is a warm-up hint
   only; correctness must never depend on it.
6. **Invalidation:** clear the precomputed identity on `LibraryService.Reloaded`
   and `SettingsService.Changed` (both already subscribable; marshal to the
   dispatcher like SettingsWindow does).
7. Tests: precompute-skip logic (visible / synthetic / same-app) as pure as
   practical. Live: driver — focus Telegram (never in the warm set), hold Ctrl,
   read the `Overlay shown (telegram) in N ms` line; repeat cold for 3 apps —
   **restart KEYPEEK between measurements** (the warmth lives in KeyPeek:
   IconExtractor's static cache, `_versionCache`, JIT/template warmth —
   restarting the target app resets nothing); target <100 ms; record
   before/after numbers in PROGRESS.md. Then run the FULL battery: verify-m1,
   m3, m4, m5 (the watcher touches startup and the presenter — regressions
   would show as focus/overlay flakes).

---

## 4. P4 — Form-based shortcut editor (no YAML knowledge required)

Today `EditRow_Click` (SettingsWindow.xaml.cs ~765–808) writes
`<process>.user.yml` into `LibraryService.LibraryDirectory`
(`%APPDATA%\KeyPeek\library`) and opens it in a text editor. Replace the
open-in-editor step with a real dialog; keep the same file contract.

1. **Dialog** `src/KeyPeek/UI/EditShortcutsDialog.xaml(.cs)`, window pattern
   copied from AddAppDialog (+ ApplyTitleBarTheme replication). Layout: list of
   the app's USER-layer entries (editable: description, section, chord,
   recommended, delete) above an add-entry form.
2. **Chord capture box:** a TextBox in capture mode: on PreviewKeyDown read
   `Keyboard.Modifiers` + the key, `e.Handled = true`. **WPF Alt quirk:** when
   Alt is held, `e.Key == Key.System` and the real key is in `e.SystemKey` —
   handle it or Alt-chords are uncapturable. Convert via
   `KeyInterop.VirtualKeyFromKey`, then VK → canonical key name: **no public
   helper exists for this** — `PowerToysKeyMap.FromVirtualKey`
   (src/KeyPeek.Core/PowerToysKeyMap.cs ~106–120) is the map you need but it is
   private; make it public (or internal + InternalsVisibleTo) rather than
   writing a second map. Its output round-trips through Serialize (OemPlus
   0xBB → "+" → emitted "Plus" → loads back "+"). Canonical names matter:
   override matching in LibraryMerger keys on rendered `KeysText`,
   case-insensitive but spelling-sensitive — a non-canonical spelling creates a
   duplicate instead of an override. Live preview via `KeyCapsElement`.
   "+ add step" allows sequences — **the dialog itself must cap at 3 chords**
   (only KeyChordParser.Parse enforces 3; the model and the YAML load path
   accept any count, and the dialog never calls Parse). Esc exits capture mode
   without saving. This is a normal focused-dialog key handler — the global
   hook is untouched (R2).
3. **Model:** build `ShortcutEntry` (required: `Chords`, `Description`,
   `RawKeys` — set RawKeys to the display string; don't set Layer/Origin, the
   merger stamps them). Section = editable ComboBox over existing
   `DisplaySections()` names, default "My shortcuts".
4. **Persist:** same contract as EditRow_Click — filename
   `(app.IsGlobal ? "windows" : AppMatcher.NormalizeProcessName(app.ProcessNames[0])) + ".user.yml"`,
   content `PowerToysManifestLoader.Serialize(definition)`. Entries merge per
   chord under the process MergeKey, BUT **definition-level metadata comes from
   the highest authored layer — which the user file becomes**: a minimal file
   would wipe AppName/VerifiedAgainst/Updated (staleness badge gone, app
   renamed to null). So clone the merged app's metadata exactly as
   EditRow_Click does (`target = app with { SourceFile = path, Sections =
   ... }`, SettingsWindow.xaml.cs ~780–784) — only Sections differ. **Traps:**
   the user file's TitleRegex must EQUAL the target definition's (MergeKey is
   `<process>|<TitleRegex ?? "">`; today no manifest sets one, so "absent"
   matches "absent" — the `with`-clone preserves this automatically); never
   name the file `index.yml` (skipped by the loader); Serialize dedupes by
   section|KeysText|Description and does not round-trip `Fallback` — user
   edits to the fallback set are out of scope, hide the Edit button for the
   fallback pseudo-app.
5. **Reload:** the FileSystemWatcher on the user folder reloads (500 ms
   debounce), but call `_library.Reload()` synchronously after saving so the
   dialog can refresh its list immediately.
6. **Conflicts inline:** before saving, run
   `ConflictDetector.Detect(candidateLibrary)` with the app's would-be
   definition swapped in; show warnings (KpWarn) — warn, don't block.
7. Tests: capture→KeyChord table (letters, digits, F-keys, arrows, punctuation
   incl. OemPlus/OemComma, Alt/SystemKey path); serialize round-trip preserving
   unrelated entries; MergeKey-preservation test (user file merges, not forks).
8. Live verify: the driver cannot click through the dialog (no mouse verbs)
   and has no F1–F12 keys — so split the verification: (a) unit/integration
   test the dialog's save path headlessly (build the entry, save, assert the
   .user.yml content and the merged library); (b) extend the driver `Keys`
   dict with f1–f24 (VK 0x70 + n − 1) while you're in there; (c) manually via
   your own run: add "Ctrl+Shift+F9 → Test entry" to Notepad in the dialog,
   hold Ctrl over Notepad → row visible, screenshot; `KeyPeek.exe --validate
   %APPDATA%\KeyPeek\library` exits 0; delete the entry → gone.

---

## 5. P5 — UI Automation harvesting prototype (research; timebox ~1 session)

Goal: for an app with no definition, auto-draft one from the app's own UI.
UIA exposes `AcceleratorKey` on menu/command elements.

1. CLI mode following the `--validate` precedent (App.xaml.cs ~62–89) for arg
   dispatch — but the CONSOLE mechanics live in
   `src/KeyPeek/Services/ValidateLibrary.cs` (~13–14): KeyPeek is WinExe, so
   printing requires `AttachConsole(ATTACH_PARENT_PROCESS)` + a StreamWriter
   over OpenStandardOutput with AutoFlush, exactly as ValidateLibrary does.
   `KeyPeek.exe --harvest <processName>` → run, print report, `Shutdown(code)`.
   Use `System.Windows.Automation` (ships with WPF; no new packages).
2. Find the process's top-level windows; walk descendants with a `CacheRequest`
   limited to {Name, AcceleratorKey, ControlType}. Guardrails: 5 s hard timeout
   per window (walk on a worker thread, abandon on timeout), depth cap ~12,
   element cap ~3000. Read-only: never invoke patterns, never click, skip
   elevated windows (UIA denies anyway). Menus often only exist while open —
   record that limitation honestly in the report rather than opening menus
   (opening menus = interfering with the user's session; don't).
3. For each non-empty AcceleratorKey: `KeyChordParser.Parse` (it accepts
   "Ctrl+S" / "Ctrl+Shift+P" style; it THROWS FormatException — try/catch per
   element, collect failures verbatim).
4. Output: draft manifest via `PowerToysManifestLoader.Serialize` written to
   `%APPDATA%\KeyPeek\reports\harvest-<process>.yml` (NOT the library folder)
   + console report: windows visited, elements walked, accelerators found,
   parse failures, elapsed ms.
5. Evaluate on: Notepad, File Explorer, Paint, Chrome, VS Code, Telegram.
   (Firefox and Discord are NOT installed on this machine — verified — and
   installing software is not sanctioned; Chrome and Telegram are present. If
   a listed app is missing, substitute and note it in PROGRESS.md.) Record
   per-app yield in PROGRESS.md.
6. **Gate:** ≥2 app families yielding ≥10 correct accelerators → write a
   productization proposal in NEEDS-REVIEW.md (consent-gated background
   harvest feeding the Discovered layer via a new `IDiscoveredAdapter` — the
   interface is in VsCodeKeybindingsAdapter.cs lines ~11–15, registration is
   the hard-coded `_adapters` array in LibraryService ~30–34). Poor yield →
   record why, close the item. Either way, do NOT productize in this milestone.

---

## 6. P6 — Accessibility pass

`ShortcutRowElement` draws everything in OnRender — screen readers see nothing.
A keyboard tool unusable with a screen reader is a real defect.

1. `OnCreateAutomationPeer` on ShortcutRowElement → a FrameworkElementAutomationPeer
   subclass. Name: build from `EntryVm.ChordData` via
   `KeyChord.ToDisplayString()` + the Description — EntryVm has NO KeysText
   property, and ChordVm.Keys holds display glyphs (↑ ⏎ ⇧ ⌫) a screen reader
   would mangle. ControlType ListItem. Invoke: **the element has no click path
   to raise** — clicks live on the DataTemplate Border (EntryRow_Click, with
   the Executable gate and the flag-zone split); add an invoke
   callback/routed event on the element that the window/template wires to the
   same EntryClicked flow, and replicate the `vm.Executable` gate in the peer.
   Frequent chips: the chip's TextBlock is already UIA-visible; if chips get
   invoke support, the peer belongs on the chip CONTAINER (a small custom
   control replacing the template Border) — NOT on KeyCapsElement, which is
   shared with settings/dashboard screens and carries no Description/ChordData.
2. High contrast: when `SystemParameters.HighContrast` is true, force opacity
   100 and map Kp tokens to system colors (SystemColors.*) in ThemeManager.
   Skipping the fade animations needs NEW plumbing — FadeIn/FadeOut live in
   OverlayWindow, driven by the presenter; add a flag they consult (and note
   the show path is perf-measured territory, §7). The existing
   UserPreferenceChanged handler (ThemeManager ~27–34) early-outs unless
   `Category == General && Theme == "system"` — high-contrast toggles arrive
   as Category **Accessibility**, so widen the filter and bypass the
   theme=="system" gate for the HC re-apply, or HC changes go undetected until
   restart.
3. Settings window keyboard audit: logical tab order per page, all toggles
   operable with Space, Nav list with arrows (ListBox gives this free — verify).
4. Verify: Narrator reads a row + a settings toggle (manual, note the exact
   utterance in PROGRESS.md); driver `tab`-walk of each settings page; high
   contrast screenshot (enable/disable via Settings app is a system setting —
   ASK THE USER to toggle it rather than changing system settings, per rules).

---

## 7. Known pitfalls (hard-won; details in PROGRESS/DECISIONS)

- **PowerShell 5.1:** no `&&`/ternary; `Set-Content` writes ANSI (use
  `-Encoding utf8` or `[System.IO.File]::WriteAllText`); commit messages via
  `git commit -F <file>`; BOM-less UTF-8 misread as ANSI by Get-Content.
- **MSBuild:** a NEW file in the `library/*.yml` embed glob is missed by
  incremental builds — `-t:Rebuild` and confirm the resource landed
  (`bundled/<name>` in GetManifestResourceNames). Never name embedded files
  `*.en-US.*` (culture-inference drops them; `WithCulture="false"` on the glob
  is load-bearing). `InvariantGlobalization` must stay OFF (WPF bindings crash).
- **XAML:** no `--` inside comments; ToggleButton lives in
  System.Windows.Controls.Primitives.
- **Settings:** immutable record — mutate only via
  `Persist(s => s with { ... })`; nested records need nested `with`. Every
  SaveAndApply fires Changed → ThemeManager re-applies + tooltips refresh;
  keep Changed handlers cheap/idempotent. Kp COLOR tokens must be
  DynamicResource (theme swaps at runtime); Controls.xaml styles may be
  StaticResource. Legacy fields `TriggerKey`/`HoldDelayOverridesMs` stay in the
  record (old files must load).
- **Threading:** OverlayPresenter content fields are dispatcher-only;
  HoldController fields are consumer-thread-only; the sanctioned cross-thread
  state is `_panelRect` (under `_rectLock`) + the static volatile hook flags.
  New cross-thread state must copy one of those patterns.
- **Overlay perf rules:** never resize/move the window while visible;
  panel-level opacity-only animation; anything on the show path gets measured.
- **TrayIcon.cs** declares `namespace KeyPeek.Services` despite living in
  Tray/ — grep by namespace. Exclude `obj/` from grep-based edits.
- **verify-m6** rewrites the live settings.json and downloaded cache. Its
  `finally` does NOT restore prior state — it resets IndexUrl to the hard-coded
  default (not what was there), leaves `LibraryUpdate.Enabled` forced true, and
  the run clears any pre-existing downloaded manifests (self-heals at the next
  scheduled update). Never kill it mid-run (that skips even this cleanup).

## 8. Definition of Done — every milestone

- [ ] Build 0 warnings; `dotnet test` all green, count ≥ previous
- [ ] New logic covered by unit tests where pure
- [ ] Live verification via idle-gated driver; screenshots for UI changes
- [ ] **R2 recheck whenever the input path changed** (P1 and P3 mandatory;
      others if in doubt). Know the tooling's limits: verify-m1 asserts
      hold-detection and clean shutdown via log lines only — it CANNOT detect
      key swallowing. The full R2 story = verify-m1 green + the ExplorePolicy
      default-off unit tests + the P1 `readedit` delivery assertion (typed
      characters actually reach the app).
- [ ] PROGRESS.md appended; DECISIONS.md updated for non-obvious choices
- [ ] `scripts\publish.ps1` + `scripts\install.ps1` — installed copy = main
- [ ] Committed to main; each commit leaves the tree green
- [ ] Vietnamese summary to the user at session end

## 9. Maintenance track (between milestones / when blocked)

- T3b: re-run `scripts\verify-leaks.ps1` (50× cycle) post quit-race fix; the
  old failure is untrusted, not confirmed.
- T9: YAML fuzz — malformed manifests must yield LibraryErrors, never throw.
- Light-theme parity screenshots (both themes, 100% + 125%).
- U18/U19 remnants: empty states, tooltips, focus states, sentence case.
- README says "84 unit tests" (line ~180) — stale; refresh alongside any README
  touch.
- Library growth: candidates Zalo, Spotify, Notion, Obsidian — author only
  against the real app; set `VerifiedAgainst`; never scrape prose.

## 10. Needs the user's decision — DO NOT start

- Community library repo on GitHub (external). The per-row report flag that pointed at the
  placeholder repo has been removed — it wrote a file to AppData and opened Notepad, and
  cost a strip of every row where a click did not run the shortcut. The update downloader
  still points at `keypeek-app/library`; it fails quietly and the bundled library stands.
- Code signing (money) · winget/installer publication (external) ·
  app self-update (external)
- Vietnamese UI localization (asked, unanswered)
- Tier-3 global usage counting (privacy; deliberately unbuilt)
- Type-to-filter inside Explore mode (follow-up question after P1)

## 11. Done so far (context, do not redo)

Sessions 1–8 (see PROGRESS.md): hooks/state machine/overlay/click-to-run/
settings+library UI in the Win11 idiom, PowerToys YAML + four-layer library +
downloader + two adapters, fallback "common in most apps" definition,
frequently-used strip + UsageTracker, one shared hold delay, ghost-tray
mitigations, install/uninstall scripts, 132 tests, 33 apps / 2,920 shortcuts,
installed live at `%LOCALAPPDATA%\Programs\KeyPeek`.

## Findings from the 2026-08-14 review

Re-checked against the code on 2026-08-17. Most of this list had been fixed in the
sessions after it was written but never struck through, which cost a re-verification
pass — hence the status markers. **Keep them current.**

Fixed and verified in code:

- ~~A captured sequence reloads as alternates~~ — explicit `Sequence: true` extension.
- ~~Serialize splits a simultaneous multi-key chord~~ — one cap per key.
- ~~The editor holds a snapshot of the user file~~ — `MergeInDiskChanges` re-reads first.
- ~~High Contrast hover/accent map to solid HighlightColor~~ — mapped to WindowColor.
- ~~High Contrast palette reads only the red channel / KpPanelBg overwritten~~ — luminance,
  and the HC path returns before the opacity block.
- ~~System rail slack is 3 DIP short~~ — `ZoneSlack + 12`.
- ~~Panel width clamps to 92% and clips the rail~~ — drops the app zone to one column and
  recomputes instead of clamping.
- ~~AlignKeysColumn discarded by the 0.42-of-width cap~~ — cap is 0.58.
- ~~User-file naming keys on process name alone~~ — `FileNameFor` folds in TitleRegex.
- ~~Warm-up can repaint a panel that is mid-fade-out~~ — the re-warm moved into the
  fade-out completion callback (2026-08-17).

Found while fixing the above (2026-08-17):

- ~~"Remove" in the shortcut editor did nothing~~ — the merge-before-write started from the
  disk copy, and a deletion is an absence, so it added the row straight back. Deletions now
  travel as their own instruction (`UserManifest.MergeOverDisk`).
- ~~Saving reshuffled the user's own rows to the bottom~~ — `WithEntry` replaces in place.

- ~~Explore selection raises no automation event~~ — `OnIsSelectedChanged` raises
  `AutomationFocusChanged`, guarded by `ListenerExists`.

**Nothing from that review is open.** Later work is tracked in section 10 (needs the
user's decision) and in PROGRESS.md.
