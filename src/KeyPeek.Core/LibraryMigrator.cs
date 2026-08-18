namespace KeyPeek.Core;

/// <summary>
/// Converts legacy flat-section definitions into the v2 per-modifier table layout.
/// Each entry lands in the table of every trigger-class modifier (Ctrl/Alt/Win) its
/// chord uses — Ctrl+Alt+K belongs in both the ctrl and alt views. Shift-only chords go
/// to the "shift" table, modifier-less keys (F2, Delete) to "plain".
/// </summary>
public static class LibraryMigrator
{
    private const Modifiers TriggerClass = Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win;

    public static bool NeedsMigration(AppDefinition app) =>
        app.Sections.Any(s => s.Table == Modifiers.None &&
                              s.Shortcuts.Any(e => e.Chords[0].Mods != Modifiers.None));

    public static AppDefinition ToTables(AppDefinition app)
    {
        var buckets = new List<(Modifiers Table, string Section, List<ShortcutEntry> Entries)>();

        void Add(Modifiers table, string section, ShortcutEntry entry)
        {
            var bucket = buckets.FirstOrDefault(b => b.Table == table && b.Section == section);
            if (bucket.Entries is null)
            {
                bucket = (table, section, new List<ShortcutEntry>());
                buckets.Add(bucket);
            }
            bucket.Entries.Add(entry);
        }

        foreach (ShortcutSection section in app.Sections)
        {
            if (section.Table != Modifiers.None)
            {
                // already v2-authored; keep as-is
                foreach (ShortcutEntry e in section.Shortcuts)
                    Add(section.Table, section.Name, e);
                continue;
            }

            foreach (ShortcutEntry e in section.Shortcuts)
            {
                // A sequence lives in its first chord's tables; alternates live in the
                // tables of EVERY alternate (Ctrl+L / Alt+D appears under Ctrl and Alt).
                IEnumerable<KeyChord> anchors = e.ChordsAreAlternatives ? e.Chords : e.Chords.Take(1);
                var tables = new HashSet<Modifiers>();
                foreach (KeyChord chord in anchors)
                {
                    Modifiers triggers = chord.Mods & TriggerClass;
                    if (triggers == Modifiers.None)
                    {
                        tables.Add(chord.Mods.HasFlag(Modifiers.Shift) ? Modifiers.Shift : Modifiers.None);
                    }
                    else
                    {
                        if (triggers.HasFlag(Modifiers.Ctrl)) tables.Add(Modifiers.Ctrl);
                        if (triggers.HasFlag(Modifiers.Alt)) tables.Add(Modifiers.Alt);
                        if (triggers.HasFlag(Modifiers.Win)) tables.Add(Modifiers.Win);
                    }
                }
                foreach (Modifiers table in tables)
                    Add(table, section.Name, e);
            }
        }

        return app with
        {
            Sections = buckets.Select(b => new ShortcutSection(b.Section, b.Entries, b.Table)).ToList(),
        };
    }
}
