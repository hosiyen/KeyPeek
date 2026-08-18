# NEEDS-REVIEW — quick human-judgment questions (answer any, in any order)

1. **Community library repo (external action, yours to take):** updates point at the
   placeholder `github.com/keypeek-app/library`; until it exists, update checks 404
   quietly and the ⚑ flag saves prefilled reports to `%APPDATA%\KeyPeek\reports\`
   instead of opening a dead page. Create the repo (an `index.json` with
   `files: [...]` plus manifests) or give me your URL and I'll make it the default.
2. **Suggestions strip** — it now shows up to 6 chips at the top ("Frequently used"),
   led by what you click in the panel and falling back to the ★ picks. Is 6 the right
   number, and do you want it on the system-wide zone too, or app-only?
3. **Transparency** — default is 96%. Settings → Appearance → the slider is live
   (60–100%). Tell me a number you like and I'll make it the default.
4. **Tier-3 usage counting** (counting real chord presses, not just panel clicks) is
   deliberately NOT built: it needs to watch keystrokes, which collides with "keystrokes
   are never logged". Want it as a strictly opt-in setting, or leave it out?
5. **Smoothness verdict:** after the latest fixes (opacity-only fade, no mid-hold
   resizing, cached panel during animation) — does the overlay still stutter on your
   machine? If yes, next step is dropping AllowsTransparency on Win10 (square corners)
   or killing the fade entirely; both are taste calls I'd rather not make alone.
2. **Hold delay 400 ms** — on this machine it feels right for deliberate holds, but a
   fast typist might prefer 300–350. Try `Settings → Hold delay` at 300 for a day?
3. **Panel density** — rows are 22 px with 292 px card slots. Comfortable, or tighter?
   (Screenshot in `%TEMP%\keypeek-shots\compact-ctrl3.png` shows the current rhythm.)
4. **Shift as a trigger is OFF by default** (typing capitals = normal typing). Agree, or
   do you want it on with a longer per-key delay (that's now supported)?
5. **PowerToys offer** — the one-time dialog fired on your machine (Shortcut Guide was
   enabled). If you clicked "No" but want it off later: PowerToys Settings → Shortcut
   Guide → disable. KeyPeek will not ask again.
6. **UI language** — all overlay/settings text is English. Want a Vietnamese UI pass?

## Open for the user — session 9d
- **Shortcut editor visual check.** EditShortcutsDialog is built, unit-covered and wired to
  the library page's "My shortcuts" button, but the test driver has no mouse verbs, so I
  could not open it myself. Please click it once (Settings → Shortcut library → pick an app
  → My shortcuts), press a combination in the capture box, and say whether the flow reads
  right. It writes to %APPDATA%\KeyPeek\library\<process>.user.yml.
- **First-run welcome already consumed.** Verification launched a clean profile, so the
  installed copy has OnboardingShown=true and will not greet you. To see it: set
  "OnboardingShown": false in %APPDATA%\KeyPeek\settings.json and restart KeyPeek.
- **Panel width.** The panel is sized from card columns and stays that width for the whole
  hold. With a two-column app zone plus the rail it is ~985 DIP. If that still reads as too
  wide on your screen, say so — the alternative is capping the app zone at one column and
  scrolling more.
