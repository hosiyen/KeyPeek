# BLOCKED

- **Multi-monitor and 125/150/200% DPI verification (T12).** This machine has one
  monitor at 125%. The code paths exist (PerMonitorV2 manifest, GetDpiForMonitor-scaled
  placement on the focused window's monitor, physical-pixel SetWindowPos) and 125% is
  verified live; other scales and a second monitor need hardware I don't have. Tried:
  nothing simulates per-monitor DPI faithfully without changing display settings, which
  is outside my write scope (system settings). Manual checklist is in README/PROGRESS.
- **Windows 11 verification (T13).** Machine is Win10 19045. All APIs used are 1809+;
  Win11-specific behavior (rounded corners, snap layouts interplay) unverified.
- **Publishing anything** (community repo, store listing, code signing) — external
  actions/money; queued in NEEDS-REVIEW.md instead.
