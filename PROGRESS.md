# PROGRESS — append-only, newest first

## 2026-08-18 (session 12) — corpus in Vietnamese, the open-hitch found and killed, GitHub live
**The stutter had an address.** Extending the frame probe past its 260 ms window and logging
WHEN the worst gap started pinned it in one run: 66–92 ms frozen at +193…+235 ms on nearly
every open — the fade's Completed event (fires ~100 ms late) dropping the panel BitmapCache
and forcing an un-cached full re-render mid-stare. Cache now lives for the whole visible
period, refreshed off-screen by the next warm. After: five opens, zero frames over 55 ms;
29.7 s pinned with hover sweeps, 9 bad of 2,281 frames. The probe now reports for the whole
visible period with the "at +N ms" offset, so the next hitch names itself.
**All 2,166 library strings translated** (descriptions + section names) via a 14-agent
parallel run against a fixed glossary; ShortcutL10n applies them at display time from an
embedded EN→VI TSV. Manifests stay byte-identical to upstream; fallback is English, never a
hole. Panel search matches both languages. Design terms of art (layer, mask, keyframe) stay
English deliberately — 37 identical pairs, each checked. 309 tests.
**GitHub: public and replaced wholesale** at the user's request ("xóa nó đi và đăng lại cái
mới"): force-pushed a fresh clean snapshot (same no-history rules), deleted and recreated
Release v0.9.0 with the new zip. https://github.com/hosiyen/KeyPeek
Earlier in the session: Office-key rows written out as Ctrl+Shift+Alt+Win and click-to-run
works on them; ghost-tray-icon sweep at startup; logo toggle-on fetches immediately.

## 2026-08-17 (session 11c) — web apps by tab title: Gmail, YouTube, Docs, Sheets, Facebook
**The panel now follows the TAB, not just the process.** Five bundled definitions ride the
browser processes (msedge/chrome/firefox/brave) and win only while their `TitleRegex`
matches the window title: Gmail, YouTube, Google Docs, Google Sheets, Facebook. Open Gmail
in Edge, hold Ctrl → Gmail's own shortcuts (c compose, e archive, g-then-i sequences with
explicit `Sequence: true` — the bare-letter heuristic would have read them as alternates);
any other tab → Edge's definition, exactly as before. The regexes cover the LOCALIZED
product names a Vietnamese system actually shows ("Google Tài liệu", "Google Trang tính") —
tested against real tab-title strings. Data from each vendor's published shortcut
documentation, our own wording, ~60 rows total, Recommended flags on the daily half-dozen.
**Two pieces of engine work made the layering honest.** ConflictDetector treated any two
definitions sharing a process as fighting — its own advice ("give one a titleRegex") now
counts as the fix it is, so Gmail-vs-Edge is layering, not a conflict (only identical
matchers still collide). And the merged-library audit's process-collision check moved to
process+titleRegex identity, which is what the merger actually keys on.
**Web apps don't wear the browser's face.** IconFor skips the exe icon for titleRegex
definitions (Gmail wearing the Edge logo reads as a duplicate row) and the vendor-logo
fetch (their process list would fetch a BROWSER's mark); they classify by name only —
"gmail"→Mail, "facebook"/"threads"→Chat, localized Docs/Sheets names→Office glyphs.
303 tests; matcher verified against real localized tab titles.

## 2026-08-17 (session 11b) — Vietnamese UI, the full PowerToys corpus, ghost-tray sweep
**KeyPeek speaks Vietnamese.** Every string the app says itself — settings window, overlay
labels/hints/footer, tray menu, balloons, all three dialogs, the welcome card — goes
through `L10n` (Core, ~190 pairs, English string as the key so a missing translation shows
English rather than a token). Static XAML stays English and `LocalizeUi` walks the logical
tree translating in place; the table is bidirectional, so switching language re-translates
the rendered tree live — no restart. Setting: `Language` = vi / en / system (follows the
Windows display language); chips in Settings → Giao diện, each language named in itself so
a user stuck in the wrong one can read the way out. Shortcut DESCRIPTIONS stay English on
purpose: they are library data, and inventing 2,900 translations ships wrong ones. Tests
pin: no empty translation, format placeholders survive, no two English keys share one
Vietnamese face (the bidirectional map cannot round-trip those), resolution rules.
**The bundle now carries the whole PowerToys corpus.** The user asked whether we had taken
everything PowerToys has: no — 17 of 34 manifests were not bundled (After Effects,
Illustrator, InDesign, Blender, GIMP, Inkscape, IntelliJ, Access, OneNote, Paint,
PowerToys, Project, Publisher, Visio, Firefox, Postman, Telegram). On this machine they
appeared anyway via the discovered layer reading the WinGet folder, which is why nobody
noticed; a fresh install elsewhere would have shipped without them. All 17 are bundled now
(35 apps, 2,385 shortcuts bundled; 2,850 merged on this machine). Two data problems
surfaced: After Effects writes whole chords into the key token (`Keys: ["Ctrl Shift W"]`,
every flag false) — the loader now folds leading modifier words into the modifier set, 126
rows became real, sendable chords; and the placeholder-smell test flagged the word
"example" inside honest descriptions ("for example, cycle through open compositions") —
the smell list keeps "replace me" and drops "example". The unknown-token test learned to
tell deliberate prose ("Pen tool", "Numpad 0", doubled-letter "UU" idiom) from a
short-all-caps typo, which is the thing it exists to catch.
**KeyPeek sweeps ghost tray icons at startup.** The user's tray had a row of dead "K"s —
every force-killed process leaves one until the mouse crosses it. The clean-tray.ps1
technique (WM_MOUSEMOVE walk over the tray toolbars) now runs in TrayIcon's constructor,
so the first thing a new instance does is clear the corpses of the old ones.
Also: flipping "Download app logos" ON now actually fetches (the session cache remembered
the misses); a failed logo download is not retried until the next window, so a dead
network costs one request per app, not one per refresh.

## 2026-08-17 (session 11) — official logos from each vendor; the "leak" was the gate
**Official app logos, and KeyPeek still ships none.** The library list showed a lettered
tile for every app not installed here, which is most of them. Logos now come from three
places, best first: the app as installed on this machine (unchanged), the official icon
fetched once from that vendor's own server and cached in `%LOCALAPPDATA%\KeyPeek\icons`,
and a category glyph we drew. The middle one is the decision: bundling another company's
mark is their call to license, and the usual icon sets disclaim it themselves (Simple
Icons is CC0 "though that doesn't mean to imply that all icons within the project are also
CC0"). Fetching the vendor's own file on the user's machine is a different act from
shipping it. Every URL was downloaded and the image looked at before it went in the table
— two vendors served byte-identical generic files, which is what that check is for. Adobe's
product-icon paths time out from this network and Postman/Publisher are SVG-only, so those
apps keep a glyph rather than wearing someone else's art. Off switch in Settings; README
and the Home privacy line updated to match.
**The 50×/20× leak gate was failing on a measurement error, not a leak.** It sampled
WORKING SET from a baseline taken before the overlay had ever been shown, so it charged the
first show's entire visual tree (~150 row elements and their render surfaces) to the cycles,
on a 63 MB single-file exe whose working set is mostly mapped image pages anyway. Measured
properly — warm up 5 cycles, then compare private bytes, GDI objects and USER objects —
handles are flat (472→478 over 45 cycles), GDI/USER are flat (33/34, unchanged), and the
managed heap logged on every hide oscillates in a 6–11 MB band with first-10 and last-10
averages of 8.0 and 8.2 MB. **Nothing is retained.** The gate now measures those three and
the heap number ships in the hide log line, so the next person can answer this in one grep
instead of an afternoon.
**Two real defects found on the way.** "Remove" in the shortcut editor did nothing: the
save path re-reads the file first (so a concurrent text edit is not clobbered) and then
re-applies our entries — but a deletion is an *absence*, and an absence adds nothing back,
so the row returned every time. Deletions now travel as their own instruction. And every
save reshuffled the user's own rows to the bottom of their section, because adding an entry
that already existed removed and re-appended it; it replaces in place now.
Also: the re-warm after a hide moved into the fade-out completion callback (it was
repainting the panel while it was still visibly dissolving); downloaded logos swap into the
rows in place instead of rebuilding the list, which was going to throw away the scroll
position a second after someone started scrolling; two `Math.Abs(hash)` calls that throw on
`int.MinValue` became `& int.MaxValue`; and the driver learned `wheel`, because every icon
check so far had only ever seen the first screenful of a virtualised list.

## 2026-08-15 (session 10f) — real app icons, search-box fix, a virtualised library
**The search field drew a box inside a box.** `KpTextBox` hardcoded its own surface, border
and 6-px corners in the control template, so putting a text box inside the rounded search
pill produced a second square-cornered rectangle in a different shade — visible in the
user's screenshot. The template now honours Background/BorderBrush/BorderThickness from
whoever uses it, with the old values as defaults, so every existing text box looks the same
and a caller can go transparent.
**Real app icons where the machine has the app.** Icon lookup only knew about RUNNING
processes, which is why almost every row had a blank gap. It now also reads the "App Paths"
registry (how installers register an executable) and resolves Start-Menu shortcuts —
Access, Excel and File Explorer now show their own icons here. **KeyPeek still ships no
third-party logos**: redistributing other companies' marks is their call to license, not
ours, so apps the user does not have keep the lettered badge.
**The library detail pane is virtualised.** It was a plain ItemsControl inside a
ScrollViewer, so selecting Illustrator realised all 317 rows before the pane could paint. It
virtualises by section with container recycling, and a log line reports any build over
120 ms so a regression names itself.
Also: the driver gained `winrect <process>` (window position for click targeting), which is
what made it possible to drive the shortcut editor from a test at all — the editor dialog
was verified opening from the library page for the first time.
Verified: 233/233 tests; verify-m1 14/14, m3 8/8; screenshots library-icons.png and
library-virtualized.png; packaged and installed.
## 2026-08-15 (session 10e) — search regression, and the blind spot that let it through
Click-to-pin broke search. The panel-click handler is a PREVIEW handler, so it ran before
the search box's own: by the time that fired, the panel was already pinned and the search
handler bailed out on `if (_pinned) return`, so the box never took focus. Fixed twice over:
the panel handler ignores clicks that land inside the search box, and clicking the box
while already pinned now UPGRADES the pin to a search pin instead of being ignored.
**The real problem was that I could not test it.** Two regressions have now reached the
user through the same gap — the driver had no mouse, so click-to-pin, the search box and
the shortcut editor were all unverifiable. The driver gained `click <x> <y>` and
`move <x> <y>` (physical pixels; it is DPI-aware since the screenshot fix). Search is now
verified end to end by an actual click: hold Ctrl → click the box → "Overlay pinned
(search)" → type "tab" → 18 matching shortcuts → Esc closes. Screenshot: search-works.png.
Verified: 233/233 tests; verify-m1 14/14, m3 8/8; packaged and installed.
## 2026-08-15 (session 10d) — a pinned panel is a pause, not a mode
The click-to-pin from the previous session kept the panel on screen but froze it: holding a
trigger again did nothing, so the only ways out were the mouse or Esc. That is a mode, and
the user's request was for a pause — read it, then carry on as before.
Now: while pinned by a CLICK, pressing a trigger hands the panel straight back to the
keyboard. It filters live as more modifiers join and closes on release, exactly as if it had
just been opened. Pinning for SEARCH is deliberately different — that pin owns the keyboard
so Ctrl+A and Shift+arrows edit the query rather than tearing the panel out of search — so
the pin now carries which kind it is (`HoldStateMachine.PinnedForSearch`).
Two state-machine tests pin both directions; writing them corrected my own mental model,
because a release before the click closes the panel, so the click must land while the key
is still down.
Verified: 233/233 tests; verify-m1 14/14 (R2 — the machine changed), m3 8/8, m4 4/4.
## 2026-08-15 (session 10c) — six things the user asked for
1. **Alt+Shift showed nothing.** Not a display bug: the corpus has `Ctrl+Shift` ("Switch
   keyboard layout") but no `Alt+Shift` at all, so the classic language switch was simply
   missing data. Added to the bundled Windows manifest as "Switch input language"; verified
   live by holding Alt+Shift.
2. **The system rail was a sliver.** It was pinned to one column whenever the app zone was
   present, so on a wide screen it sat next to a roomy app zone with its own text truncated.
   It takes a second column when it has the cards and the monitor has the width. The
   condition also had to consider the warm-up path, which runs before any monitor is
   resolved and would otherwise bake the narrow layout into the content the show reuses.
3. **Accent colour.** Instead of picking a taste, KeyPeek now follows **the accent colour
   Windows is already using** (default), with Indigo / Violet / Teal / Amber as explicit
   alternatives in Settings. The system value is used as a hue, not verbatim — Windows'
   accent can be near-black or near-white and would disappear against one of our palettes;
   lightness stays fixed per theme so contrast is not left to chance.
4. **Click the panel to keep it.** A left-click anywhere on the panel that isn't a row or
   the search box now pins it: it stops following the trigger key and stays until a click
   outside or Esc. Before, letting go of the key closed it the moment the user reached for
   it with the mouse.
5. **"Open library folder" showed a Windows error** when the folder was missing. It is
   created before opening now.
6. **Apps without an icon looked broken.** Most of the library's apps are not installed
   here (Photoshop, Blender, Figma…), so their rows had an empty gap. They get a tinted
   initial badge instead — stable colour per name, muted enough that a column of them is
   not confetti.
Verified: 231/231 tests; verify-m1 14/14, m3 8/8, m4 4/4; screenshots in
docs/ui-review/2026-08-15 (accent-rail, alt-shift); packaged and installed.
Not verifiable here: the click-to-pin behaviour — the test driver has no mouse verbs.
## 2026-08-15 (session 10b) — UI pass on the main window
The panel had been polished; the window it opens from had not. Changes, each for a reason
visible in a screenshot:
- **Home reads as four facts, not four paragraphs.** Each card leads with the number
  ("None", "22 hours ago", "35", the trigger caps) with a quiet caption under it, an accent
  icon chip for colour, one supporting sentence, and a link instead of a button — buttons
  on every card made four equal-weight calls to action for a page you mostly glance at.
  A one-line greeting states the headline ("Everything looks healthy").
- **The search field looks like search**: pill shape, a magnifier glyph inside, and an
  accent border while focused. It was a plain rectangle that only announced itself through
  placeholder text.
- **The library's pencil appears on hover.** It was drawn on all ~140 rows at once,
  competing with the shortcuts that are the point of the page; it still appears on keyboard
  focus so it stays reachable without a mouse.
- Panel cards (from the earlier pass) keep their hairline border and title rule.
Verified: 231/231 tests; verify-m1 14/14, m3 8/8, m5 6/6; screenshots home-new.png and
library-new.png in docs/ui-review/2026-08-15; packaged and installed.
## 2026-08-15 (session 10) — the last plan items, two data bugs the user caught, UI polish
**"Quit Telegram" was showing in Microsoft Edge.** Telegram's manifest sets
`BackgroundProcess: true` next to `WindowFilter: telegram.exe`, and the loader treated
that flag as "system-wide" — so all ~35 Telegram shortcuts were published into the
system rail of every app (that is also why the rail looked cluttered in earlier
screenshots, which I had not questioned). Only `WindowFilter: "*"` means system-wide now.
Guarded by a new `MergedLibraryAuditTests` that audits the library the way the panel sees
it — merged, not per file: only "Windows" may be system-wide, the rail stays under 260
entries, no two definitions may share a process, no merged app may be empty.
**The ⚑ report named the wrong app**: it used the focused app for every row, so flagging a
Windows shortcut from inside Edge filed it against Edge. Rows now carry their source
definition.
**Remaining PLAN findings closed:** recorded sequences round-trip (new `Sequence: true`
extension field — the modifier heuristic cannot tell "Ctrl+K then Z" from alternatives);
the editor re-reads the file before writing (its own "Open the file" button invites a
concurrent editor); High Contrast no longer fills hovered/selected rows with the solid
highlight colour under WindowText text (~1.35:1), picks its palette by perceived
brightness rather than the red channel, and returns before the opacity block can paint
KeyPeek's own colour back over the system background; the rail's slack was 3 DIP short so
its second column always wrapped (a Win-hold panel was half empty); a too-wide panel now
drops an app column instead of clamping, which used to take the whole shortfall out of the
rail and clip its cards; per-card key columns are no longer discarded by the render-time
cap; user files fold TitleRegex into their name so two definitions sharing a process can't
collide; Explore selection raises an automation event.
**UI:** cards gained a hairline border and a rule under the section title so they read as
groups on a busy desktop, and **descriptions wrap to a second line instead of being cut**
— half the rows in Edge's "Address bar" card were ellipsised mid-sentence. Row height is
measured at the width the row is actually given, which also removed uneven gaps.
Verified: 231/231 tests; verify-m1 14/14, m3 8/8, m4 4/4, m5 6/6; screenshots in
docs/ui-review/2026-08-15; packaged and installed.
## 2026-08-14 (session 9f) — smoothness, T9 fuzz, and a 28-finding review swept
**Stutter, measured rather than guessed.** Added a frame probe to the fade: median gap
16–17 ms (60 fps) but the FIRST frame took 32–77 ms — a transparent WPF window is
composited on the CPU, and its first frame after Show is expensive whatever else is idle.
Fixes that measured: the fade now starts at 35% opacity over 90 ms (the unavoidable first-
frame cost stops reading as a hitch), the bitmap cache is built during warm-up instead of
at show time, the system rail is filled synchronously (deferring it made the content-sized
panel visibly grow a frame later), warming is skipped while a trigger is held and re-run
when the panel hides — so a second hold in the same window shows in **5 ms**. Windows on
this machine has animations disabled system-wide, which is why the panel had been popping
in abruptly; KeyPeek's own Motion toggle now decides, with a hint when Windows disagrees.
Tried and reverted: sizing the window to the panel. It clipped the last row and the footer
(measured height never matched the arranged one) and the frame timings did not improve.
**T9 fuzz** (`ManifestFuzzTests`): truncation at every byte, single-line deletion, random
byte corruption with fixed seeds, absurd sizes, hostile key tokens. It found a real crash
on the first run — a file truncated mid-`Shortcut:` deserializes to a null list item and
the loader dereferenced it. That is exactly the file the folder watcher sees when it reads
a half-written save.
**The 32-agent adversarial review returned 28 confirmed defects.** Fixed here, highest
severity first:
- Warm content was never invalidated by anything that happened while the panel was up, so
  the next hold re-served the previous hold's table, hero caps, footer, scroll position,
  Explore selection and suggestions strip (six of the findings, one root cause). The warm
  token is dropped when the panel hides.
- Explore mode painted its initial selection before interception was switched on, so the
  panel opened with an armed but invisible selection: Enter ran a row nobody could see and
  Up/Home looked dead.
- **The hook could swallow a key-up whose key-down had already reached the app**, latching
  that key down inside the app — and the Esc half of it was live in the DEFAULT
  configuration. The hook now tracks ownership per key: it never takes a key mid-press, and
  only swallows a release it also swallowed the press for.
- WPF never reports the Windows key in Keyboard.Modifiers, so the editor captured Win+Alt+K
  as Alt+K — and then overrode the wrong shortcut, since matching is by chord text.
- Empty manifests were rejected, so "Add app" created a file that never appeared; a
  Fallback definition lost its flag on save, so every edit to "Common shortcuts" was
  written to a file the loader then refused; the library pencil threw on any definition
  with no process name.
- The panel reserved 190 DIP for its chrome when a wrapped suggestions strip needs more,
  putting the footer outside the window; reserve is 265 and the strip is capped at 5 chips.
Verified: 223/223 tests; verify-m1 14/14 (R2 — the hook changed), m3 8/8, m4 4/4, m5 6/6;
published + installed. Remaining findings (sequence round-trip, editor snapshot staleness,
High Contrast accent contrast, rail column slack, per-card key column at extremes,
automation events for Explore selection) are listed in PLAN.md §9.

## 2026-08-14 (session 9e) — P5 closed (negative), P6 accessibility, and a data sweep
**P5 — UI Automation harvesting: does not work, item closed.** `--harvest <process>` walks
a running app's UIA tree (read-only, depth/element/time capped) and drafts a manifest into
the reports folder. Measured yield: **Edge 3 shortcuts; Notepad, Paint, File Explorer,
Claude and Telegram all 0**. The reason is structural: AcceleratorKey lives on menu items,
which most apps only create while the menu is open, and Electron/WinUI apps don't publish
it at all. Raising the yield would mean opening other people's menus behind their back,
which is out of scope by KeyPeek's own rules. Below the plan's gate (2 families × 10), so
no productization proposal — the prototype stays as a CLI tool for the curious.
**P6 — accessibility.** Drawn rows were invisible to screen readers; `ShortcutRowPeer`
gives each one a ListItem identity, a spoken name built from ChordData ("Ctrl+C, Copy" —
not the drawn glyphs, which a reader mangles), a help string, and Invoke wired through a
command so the peer runs the same guarded path as a click. Verified live by querying the
automation tree while the panel was open: **66 rows exposed with readable names**. High
Contrast now forces full opacity, skips the fades, and repaints the palette from
SystemColors; the theme watcher also listens for the Accessibility category, which it
previously ignored (so an HC toggle went unnoticed until restart).
**Data sweep (user reported "Open LinkedIn hiện không đúng").** Root cause: a chord may
list several keys (`<Office>` + W), and the loader joined them into ONE key string, drawing
a single cap reading "Office+W". Now each key gets its own cap, modifiers stay on the
first, and the row is marked not-alternatives so it never reads "Office or W". Swept the
whole corpus for the same class of problem and added `BundledLibraryTests` as a permanent
guard: unknown key tokens, placeholder text shipped as a shortcut, entries with no
description, and section titles still carrying `<...>` markup all fail the build now. The
sweep found one more: Explorer spelled a key "Number (1-9)", now rendered as the 1–9 cap.
The hardware-key sections are labelled honestly ("Office key (keyboards that have one)").
**A real concurrency bug fell out of it:** YamlDotNet's deserializer is not thread-safe and
was a shared static, so parsing from the watcher, the downloader and startup at once could
throw "invalid YAML" on perfectly good files. It is per-thread now. This also explains a
mystery 12-test failure earlier in the session that vanished on re-run.
Also fixed: "Create definition file" wrote a starter containing `Ctrl+S — Example, replace
me`, which then showed in the panel as a real shortcut (this is what the user hit). It now
opens the P4 editor instead, and both StarterFile helpers ship empty. The stray
`claude.json` that flow had created on this machine was removed.
Verified: 191/191 tests, three consecutive clean runs (the race would show up as flakes);
verify-m1 7/7, m3 8/8, m5 6/6; published + installed. Library: 33 apps / 2,919 shortcuts —
one fewer than before because the Explorer "1–9" entry now dedupes correctly against its
WinGet twin.

## 2026-08-14 (session 9d) — P4 shortcut editor + a UI/sizing pass the screenshots forced
**P4.** `EditShortcutsDialog` — press the keys, type what they do, done. Capture handles
the WPF Alt quirk (`Key.System` + `SystemKey`), refuses lone modifiers and keys the format
cannot spell, caps sequences at 3 (only the parser enforced that before), and renders a
live preview with the shared key-cap element. Rules live in Core so they are tested:
`ChordCapture` (VK → canonical name, via the now-public `PowerToysKeyMap.FromVirtualKey`)
and `UserManifest` (file name, add/replace by chord text, remove, empty-but-valid file).
The metadata trap the plan flagged is handled and pinned by a test: a user file is a clone
of the merged definition with its own Sections, so editing one shortcut cannot blank the
app's name or its "verified against" badge. Conflicts warn, never block. Reachable from
the library page as **My shortcuts**.
**The UI pass came out of actually looking at full-resolution screenshots**, which
uncovered that the harness itself was lying: `KeyPeekDriver` was not DPI-aware, so every
"screenshot" was the top-left 1536×864 of a 1920×1080 screen — every UI review this
project has done was missing the right edge of the panel. One `SetProcessDPIAware()` call
later, four real defects were visible and fixed:
1. **Panel was always full height** — the zone row was star-sized, so a filter matching
   three shortcuts still drew a screen-tall panel. Now Auto (capped): the panel shrinks to
   its content while the window keeps its size, so nothing jitters mid-hold.
2. **Zones opened scrolled** — ItemsSource changed but the ScrollViewers kept their old
   offset, so the rail often opened halfway down a table. Both scroll to top on Apply.
3. **Cards used one column instead of two** — the scrollbar takes ~17 DIP, which was
   exactly enough to wrap the second card onto its own row and leave half the zone empty.
   The slack is now 26 DIP, shared between Apply and PositionWindow.
4. **"No shortcuts match" was invisible** — with neither zone present both grid columns
   collapsed to 0-star, so the centred message had zero width to live in.
Also: descriptions were truncated because the key column was a fixed 168 px; it is now
sized per card to that card's widest row (70–190), and the library header's three buttons
had squeezed the app name to "Winc…" (short labels + trimming + a min width).
Verified: 186/186 tests; verify-m1 14/14 (R2), m3 8/8, m4 4/4, m5 6/6; full-screen
screenshots in docs/ui-review/2026-08-14 (p4-ui-full, p4-ui-empty, p4-lib-header).
Published + installed. Test artifact (notepad.user.yml) removed from the user's library.
Not yet verified by me: the editor dialog's own interaction — the driver has no mouse
verbs, so opening it needs a human click. Its logic is unit-covered; the visual check is
open in NEEDS-REVIEW.

## 2026-08-14 (session 9c) — P3: first show under 100 ms (T2b closed)
Measured before optimizing, as the plan required. A cold show over Edge broke down as
**resolve 3 ms · view-models 8 ms · layout 526 ms** — so the view-model cache the plan
sketched would have saved 11 ms of 551. The cost is WPF realizing ~150 row elements and
measuring their text, and it happens when the window is shown.
Built `ForegroundWatcher` (SetWinEventHook EVENT_SYSTEM_FOREGROUND, UI thread, delegate
pinned in a field, 250 ms DispatcherTimer debounce, resolves through
ForegroundAppService.Capture rather than trusting the event's hwnd) → presenter warms the
panel for the app the user just switched to. `PrecomputedContent` (pure, Core) is the
identity of what was warmed: hwnd + process + **title** + held modifier. Title is part of
it because definitions can be selected by TitleRegex and titles change without any
foreground event — a miss costs milliseconds, a false hit shows the wrong app's shortcuts.
Two dead ends worth recording, both found by measuring rather than reasoning:
1. Warming through `_window.Measure/Arrange/UpdateLayout` did **nothing** (18 ms warm, show
   still 452 ms). A window that has never been shown is `Visibility.Collapsed`, and WPF
   skips layout for collapsed elements entirely. Measuring the panel element directly
   (`OverlayWindow.WarmLayout`) bypasses that.
2. `Apply()` defers the 142-row system rail to the next dispatcher frame so the panel
   appears sooner — but precompute also runs at Background priority, so the biggest table
   was never warmed. Apply now takes `deferSystemZone`, false while warming.
Also: the watcher only reports CHANGES, so the app the user was already in when KeyPeek
started stayed cold — exactly the app they are most likely to use. The watcher now primes
itself once at construction.
Result on this machine: **63 ms / 65 ms / 7 ms** (Notepad cold-but-warmed, Claude, Notepad
repeat) against 231–551 ms before; the ~300 ms now happens while the machine is idle.
Verified: 167/167 tests (8 new for the match rules); verify-m1 7/7 (R2 — input path
touched), m3, m4, m5 all green; warm-show screenshot confirms the content is the focused
app's, not a stale panel. Published + installed.

## 2026-08-14 (session 9b) — P2: first-run welcome + panel position
**Welcome window.** A tray app that starts silently is indistinguishable from one that
failed to start, so KeyPeek now says hello exactly once: app icon, "KeyPeek is running",
three lines (hold the trigger — shown as the user's OWN trigger key rendered with
KeyCapsElement, not a hardcoded Ctrl; click a row to run it; everything else is in the
tray), a privacy sentence, and [Open settings] / [Got it]. `OnboardingShown` is persisted
BEFORE the window opens, so a crash or a kill can't produce a greeting loop. The decision
is a pure function (`OnboardingPolicy`) with a 4-row test table; `--settings` launches
never greet. Title-bar theming moved out of SettingsWindow into a shared `UI/TitleBar`
helper so new dialogs stop inheriting a white title bar in dark mode.
**Panel position.** `PanelPlacement` (pure, Core) computes the top edge from the work
area, panel height and a margin: top = one margin down, bottom = one margin up, centre =
40% of the slack (the placement KeyPeek has always used — kept so the default look does
not change). Settings gained a three-chip segmented control. Verified live at all three
values: y = 30 / 62 / 127 physical px on this 125% display, matching the pure function
exactly, with screenshots.
Verified: 159/159 tests; fresh-install simulation (settings.json deleted) showed the
welcome and the second launch did not; both one-time dialogs chained correctly — deleting
settings.json also reset `PowerToysOfferShown`, so the PowerToys offer appeared FIRST and
the welcome waited behind it instead of stacking, which is exactly what the chaining was
for. Published + installed.
Note: the verification run greeted the installed copy, so `OnboardingShown` is already
true on this machine — the user won't see the welcome again.
Harness limitation found: `KeyPeekDriver focus <proc>` fails while a modal dialog owns the
app ("could not focus KeyPeek"), so modal-dismissal steps need the window title or a
retry; worked around manually this session.

## 2026-08-14 (session 9) — P1 Explore mode shipped (opt-in keyboard navigation)
First milestone of PLAN.md v3, executed as written.
Built: `ExplorePolicy` (pure, Core) owns the swallow set — Enter, arrows, Home/End,
PageUp/PageDown — and is the single place that widens interception beyond Esc;
`ExploreSelection` (pure, Core) owns navigation semantics: ↓↑ ±1 clamped, ←→ jump to
the neighbouring card (Left snaps to the top of the current card first) wrapping,
Home/End ends, PgUp/PgDn ±10. Hook gained `InterceptExplore` beside `InterceptEsc`,
with a `bool[256]` so each swallowed key-down's matching key-up is swallowed too.
HoldController routes explore keys **before** the auto-repeat dedupe, so holding ↓
scrolls the selection. The presenter clears the flag when the panel is pinned —
a low-level swallow would otherwise make Left/Right/Home/End dead inside KeyPeek's
own search box (caught by the plan's verification pass, not by running it).
Selection paints via a new `IsSelected` property on the drawn row + a reference-equality
MultiBinding against the window's `SelectedEntry`; the selected row calls
`BringIntoView()` itself, because system-zone containers don't exist for a frame after
Apply(). Frequent-strip chips are deliberately not selectable in v1 (different template,
cannot show the highlight). Enter runs the row through the ordinary click path after
checking `Executable`. Settings gained a "Keyboard navigation" card (off by default).
Verified live: verify-m1 7/7 (R2, Explore off), m3 3/3, m4 1/1, m5 3/3, 150/150 unit
tests including the R2 pin (no vk in any state is swallowed with Explore off); driver
screenshots of the selection moving and jumping cards; Enter logged
`Executed shortcut: Ctrl++` and hid the overlay with reason `Executed`.
Two pre-existing defects found by actually running the battery:
- **verify scripts picked a runtime-only `dotnet` off PATH** ("No .NET SDKs were found")
  whenever the caller's PATH lacked the user-scope SDK. New `scripts/_dotnet.ps1`
  asks each candidate for `--list-sdks` and sets DOTNET_ROOT from the winner; all seven
  dotnet-invoking scripts now use it.
- **verify-m1 had no idle gating** — the only harness script without it. On a live
  desktop the human's own Ctrl press landed inside a test's log window (spurious
  detection) while their focus changes killed ours: T3/T4 failed for environmental
  reasons. Both positive and negative assertions now gate on `waitidle` and retry when
  the driver reports `not-idle-timeout`.
- **verify-m4 had been failing since the UI redesign**: the filter log line printed the
  glyph "⇧" while the script matched "Shift" (and PS 5.1 read the BOM-less log as ANSI,
  mojibaking it). Log lines are the verification API, so they now use ASCII modifier
  names (`KeyDisplay.ModifierLogText`); the UI keeps its glyphs.
Driver gained up/down/left/right/home/end/pgup/pgdn. Published + installed.

## 2026-08-14 (session 8b) — PLAN.md rewritten as the v3 handoff plan
User approved the improvement roadmap and asked for a detailed plan on main for
the next session (Claude Opus 5) to execute. PLAN.md fully rewritten: operating
rules + non-negotiables + verified environment commands, then P1 Explore mode →
P2 onboarding/panel position → P3 focus-change precompute (<100 ms) → P4 form
shortcut editor → P5 UIA harvest prototype → P6 accessibility, plus maintenance
track and the do-not-start user-decision list. Every referenced symbol/path was
first extracted by a 6-agent recon pass over the real code (hook internals,
presenter, settings, library, lifecycle, scripts/driver grammar); an
adversarial 7-agent verification pass was launched but hit the session usage
limit — riskiest claims (driver grammar ambiguity, PositionWindow formula, VK
codes) re-checked by hand instead; the full verify pass should be re-run and
any corrections committed as a follow-up. The old PLAN.md was stale (listed
U4–U17 as open although session 7 shipped them) — superseded entirely.

**Follow-up (same day, after limit reset):** the 7-agent adversarial pass ran
and produced 24 findings, all incorporated into PLAN.md. The important catches:
Explore-mode swallowing would have broken caret keys in the pinned search box
(flag must clear on pin); the selection model started on Frequent-strip chips
that can't render the highlight (v1 skips chips); a minimal user-layer file
would wipe app metadata via the merger's highest-authored-layer rule (clone
metadata like EditRow_Click); the P3 precompute skip could serve stale content
under TitleRegex (compare Title too); verify-m1 cannot detect key swallowing
(R2 now rests on unit tests + a new readedit delivery assertion); UnhookWinEvent
fails cross-thread on crash paths; HC toggles arrive as UserPreferenceCategory
Accessibility, which ThemeManager's filter drops; PowerToysKeyMap.FromVirtualKey
is private and P4 needs it public; the driver lacks F1–F12; Firefox/Discord are
not installed (P5 list adjusted to Chrome/Telegram); settings.json edits need a
quit-edit-relaunch cycle (no watcher); verify-m6's finally does not restore
prior state. PLAN.md v3 is now verified end to end.

## 2026-08-14 (session 8) — "common in most apps" fallback for undefined apps
User question: an app with no definition (Claude) still responds to Ctrl+F, Ctrl+C,
Ctrl+S — an empty panel is wrong. Built a fourth kind of definition: a **fallback**
manifest (`library/+KeyPeek.CommonApps.yml`, KeyPeek extension `Fallback: true`) holding
25 shortcuts that work in most Windows apps (editing, find/save/print, caret movement,
tabs/windows, zoom/refresh/fullscreen). When the focused app has no definition, the app
zone fills from it with the header "COMMON IN MOST APPS" and the hint becomes
"No definition for <app> yet — showing shortcuts that work in most apps", keeping the
Create-definition-file button. A real definition always wins; the fallback is never
shown for the desktop, for KeyPeek itself, or in the system zone.
Also this session: transparency slider re-framed as 0–40% with 0 (solid) as the new
default; the 142-row system rail now fills one frame after the panel appears; the
description column narrows in the rail so system text is no longer truncated.
Verified live: Claude → common set shown, 76 ms; Notepad cold first show 967 → 218 ms;
132/132 tests (3 new fallback tests: not global, real definition wins, merges as its own
identity); published + installed; library now 33 apps / 2,920 shortcuts.
Gotcha worth remembering: MSBuild's up-to-date check missed a NEW file added to the
`library/*.yml` embed glob — the manifest silently wasn't embedded until `-t:Rebuild`.

## 2026-08-14 (session 7) — Win11 settings UI, suggestions strip, perf, library audit
Built (all on main, no branches per user request):
- **Window rebuilt** to the Windows 11 Settings idiom: left nav (Home / Settings /
  Shortcut library / Conflicts), dark title bar, global search, Ctrl+1..4 + Ctrl+F,
  remembered placement clamped to the work area. Home dashboard cards report live data
  (conflicts, library freshness in relative time, coverage, triggers as key caps) with
  a version/license/privacy footer.
- **Design tokens + Win11 controls** in both themes: one accent, toggle switches, cards,
  thin scrollbars, keycap toggle chips. Enabled labels no longer painted with the
  disabled color.
- **Instant apply** — Save button deleted; transparency/theme/toggles persist on change.
- **Hold delay simplified per user**: per-key overrides removed from model and UI; one
  hold time for every trigger key.
- **Frequently used strip** (spec Feature 2, tiers 1–2): ranked by rows the user clicks
  in the panel, stored in readable `%APPDATA%\KeyPeek\usage.json`, clearable from
  Settings; falls back to curated ★ picks. Tier 3 (global chord counting) NOT built —
  still opt-in/off by design, tracked in PLAN.
- **Perf**: the whole shortcut row is now one drawn element (~7 visuals → 2).
  First show for Notepad (app zone + 142-row system zone) **906 ms → 370 ms**; warm
  shows 18–60 ms. Still above the <100 ms bar for a brand-new app → T2b stays open.

Fixed while verifying (all caught by running it, not by reading it):
- Crash opening the window: conflicts re-parsed a *display* string containing "↑↓←→".
  Conflict now carries parsed chords; regression test added.
- **311 conflicts → 0**: the rule flagged every app chord that also exists system-wide
  (Ctrl+F, Ctrl+C…). Now only chords Windows intercepts first (Win+…, Alt+Tab,
  Ctrl+Shift+Esc, Ctrl+Alt+Del) count; 3 tests pin both directions.
- Library pane opened empty: `null == null` matched a group-label row.
- Settings window restored partly off-screen; now clamped.

Library audit (user asked): **32 apps / 2,895 shortcuts**, every spec-required app
present (Windows, Explorer, Chrome, Edge, VS Code, Word, Excel, PowerPoint, Outlook
classic + new, Notepad, Terminal, Teams, Slack, Discord, Photoshop, Figma) plus 16
inherited from the WinGet/PowerToys folder (Firefox, GIMP, Blender, IntelliJ, Telegram,
Postman, Paint, OneNote, Access, Project, Publisher, Visio, Illustrator, InDesign,
After Effects, Inkscape). Apps with no definition (e.g. Zalo) still show the
"Create definition file" path.

Verified: 129/129 tests; built clean (0 warnings); screenshots at
`docs/ui-review/2026-08-14/` (home, settings, library, conflicts, overlay-with-strip);
published + installed to `%LOCALAPPDATA%\Programs\KeyPeek`, single instance confirmed.
Ghost tray icons the user reported came from dev-cycle force-kills, not normal use;
swept with `scripts/clean-tray.ps1` and my scripts now exit cleanly via `--quit`.

## 2026-08-14 (session 6) — install story + UI-revision pass begun (P0 bugs dead)
Built: per-user install/uninstall scripts (no admin; exe → %LOCALAPPDATA%\Programs\
KeyPeek, Start-Menu shortcut, Run-key removal on uninstall; install.ps1 also works
flat-copied next to KeyPeek.exe for other machines). Installed live on this machine —
the running instance now serves from Programs\KeyPeek with user data intact.
UI revision spec received; full backlog in PLAN.md (P0-B nav/dashboard → P1 pages →
P2 polish → Explore mode → Frequently used). P0 bugs fixed with tests:
- Bug 2 root-caused: the browser rendered the by-modifier index (Ctrl+Alt+Tab is
  IN both ctrl and alt tables by design) — new DisplaySections() collapses to the
  authored shape; 4 tests prove dedup + that merge-level section unification was
  already correct.
- Bug 1: accent-bar+bold selection vs weak hover wash; selection survives reloads
  (was reset to item 0 by every watcher event — the actual mismatch mechanism).
- Bug 3: scroll bounds end above the toolbar.
Verified: 124/124 tests; fresh publish + reinstall (63.0 MB). Next: P0-B left-nav +
Home dashboard + shared key-cap component (U4–U9).

## 2026-08-14 (session 5) — appearance settings, delay presets, token sweep (user asks)
Built: T5 theme wiring complete — overlay converted to the Kp* dynamic palette,
ThemeManager applies dark/light/follow-Windows live; new Appearance settings (theme
combo + 60–100% transparency slider, alpha applied over either palette); hold-delay
picker redesigned to presets (Fast 250 / Standard 400 / Relaxed 600 / Custom) per user
request. Library sweep per user report ("alt win 32", "taskbar-9"): token inventory of
all bundled+winget manifests confirmed numeric VK codes were already decoded; fixed the
stragglers — placeholders map with AND without angle brackets (bare "ArrowLR" seen
live), <TASKBAR1-9> renders as a "1–9" cap, Shell's templated section title sanitized.
Verified: 120/120 tests; dark theme screenshot live (Win-hold over Edge: hero cap, two
system cards, 66 shortcuts, correct relative caps). Cold first-show still ~530 ms
(known T2b). Light theme applied but not yet visually reviewed → NEEDS-REVIEW.

## 2026-08-14 (session 4) — user-reported: VK-code keys, dead ⚑ link, residual stutter
User reported "Ctrl Win 77" rows, the ⚑ flag opening a 404, and the overlay still not
feeling smooth. Fixed all three: (1) PowerToys' own hotkey manifest in the WinGet
folder encodes keys as raw VK codes — numeric tokens now decode via a full VK map
(84→T, 187→+, 77→M); test added, 120/120. (2) The flag saves a prefilled local report
file while the repo URL is the placeholder — no more dead link; a real configured repo
gets the prefilled-issue URL. (3) Removed the two remaining animation costs: no window
resizing while visible (live resize on filter changes read as jitter) and opacity-only
fade (the slide repainted the whole CPU-composited layered surface per frame).
Verified: 120/120 unit tests; app relaunched live, 32 apps / 2896 shortcuts; smoothness
verdict awaits the user (NEEDS-REVIEW #2).

## 2026-08-14 (session 3, autonomous) — robustness battery, tray-ghost + stutter fixes
Built: latency instrumentation (hold-timer→fade-in, logged per show); leak/memory
battery (verify-leaks.ps1); quit-race fix (`--quit` now retries up to 4 s while an
instance is still starting — before this, a --quit racing a fresh launch silently did
nothing); warm preload (real content + off-screen measure at startup); overlay
animation moved to panel-level with a temporary BitmapCache (WPF renders
AllowsTransparency windows on the CPU — animating the whole window re-uploads every
frame and stutters, which the user reported); FileVersionInfo cached off the show path;
ghost-tray sweeper (scripts/clean-tray.ps1) + tray disposal added to crash paths.

Verified (item by item):
- Unit suite: 119/119. Fresh clone → Release build: 0 warnings, 119/119 (T4 PASS).
- verify-m5 3/3 (Ctrl/Win/Alt holds, Win release doesn't open Start, focus retained).
- verify-m1 7/7 (typing non-interference — R2 recheck — incl. clean-exit hook removal).
- verify-m6 4/4 (published correction reaches a running install; user override survives).
- Live instance: handles 422→444 over 20 overlay cycles (stable), working set
  124→109 MB (GC; flat-ish). PASS.
- 50× launch/quit: FAILED first run — 50 zombies, all force-killed. Root cause was the
  quit-race above (verify-m1's own quit test passed on the same binary with a longer
  settle). Fix shipped; harness now waits for readiness. RE-RUN STILL PENDING (T3b) —
  do not trust the cycle test until it has passed post-fix. Side effect: the force-kills
  left ~50 ghost "K" tray icons on the user's machine (their screenshot); swept clean.
- Latency (measured, honest): warm shows 20–60 ms ✓; but FIRST show of a not-yet-shown
  app is 425–1360 ms (msedge, template instantiation per row). Under the 100 ms bar
  only when warm → T2b open with numbers. Warm preload covers the system zone only.

## 2026-08-14 (session 2) — PowerToys manifest format + four-layer library
Native format = PowerToys Shortcut Guide YAML (schema verified against 17 real
manifests; MIT confirmed; sequences vs alternates are structurally identical → equal-
modifier heuristic). Four layers merged per shortcut (Bundled 17 embedded / Downloaded
HTTPS cache / Discovered = WinGet folder (33 manifests live on this machine) + VS Code
& JetBrains adapters / User, never touched). Verified: corpus parse 17 apps 1351
shortcuts 1 known upstream error; live merge 32 apps / 2896 shortcuts; update E2E via
local HTTP server 4/4; user-folder migration removed 16 unmodified seed copies.
MSBuild trap: ".en-US" filenames get culture-inferred and silently dropped — renamed.

## 2026-08-13 (session 1) — spec v1→v2 build-out
M1–M8: observer-only LL hooks (Esc-while-visible the only swallowed key), multi-trigger
holds with Win/Alt release masking (VK 0xFF), two-zone content-driven overlay,
progressive filtering, click-to-run with modifier reconciliation + elevated refusal,
icons, click-to-pin search, settings + library browser with conflicts, single-file
publish (~63 MB). All verified live with an idle-gated SendInput harness (never injects
while a human is using the machine).