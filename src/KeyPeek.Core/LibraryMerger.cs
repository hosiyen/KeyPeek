namespace KeyPeek.Core;

/// <summary>
/// Merges the four library layers into one effective library. The rules, per the spec:
///
///  - Later layers win: Bundled < Downloaded < Discovered < User.
///  - Merge PER SHORTCUT, not per file: a user file that overrides one entry of a
///    bundled definition replaces exactly that entry (same chord) and leaves the rest
///    of the bundled definition intact — overriding never orphans a definition.
///  - New chords from later layers are appended to their section (created if needed).
///  - Definition metadata (display name, VerifiedAgainst, …) comes from the
///    highest layer that provides the definition file; process names are unioned.
///
/// Entries keep their layer and origin so the browser can show provenance and offer
/// reset-to-shipped.
/// </summary>
public static class LibraryMerger
{
    public static IReadOnlyList<AppDefinition> Merge(
        IEnumerable<(LibraryLayer Layer, IReadOnlyList<AppDefinition> Apps)> layers)
    {
        // Group same-identity definitions across layers, preserving first-seen order.
        var buckets = new List<(string Key, List<AppDefinition> Defs)>();
        foreach ((LibraryLayer layer, IReadOnlyList<AppDefinition> apps) in layers.OrderBy(l => l.Layer))
        {
            foreach (AppDefinition app in apps)
            {
                AppDefinition stamped = StampLayer(app, layer);
                var bucket = buckets.FirstOrDefault(b => b.Key == stamped.MergeKey);
                if (bucket.Defs is null)
                    buckets.Add((stamped.MergeKey, new List<AppDefinition> { stamped }));
                else
                    bucket.Defs.Add(stamped);
            }
        }

        return buckets.Select(b => MergeOne(b.Defs)).ToList();
    }

    private static AppDefinition StampLayer(AppDefinition app, LibraryLayer layer)
    {
        string origin = Path.GetFileName(app.SourceFile);
        return app with
        {
            Layer = layer,
            Sections = app.Sections.Select(s => new ShortcutSection(
                s.Name,
                s.Shortcuts.Select(e => e with { Layer = layer, Origin = origin }).ToList(),
                s.Table)).ToList(),
        };
    }

    /// <summary>Merge one identity's definitions, lowest layer first.</summary>
    private static AppDefinition MergeOne(List<AppDefinition> defs)
    {
        if (defs.Count == 1)
            return defs[0];

        // Section order: first-seen wins; entries: keyed by chord, later layers replace.
        var sectionOrder = new List<string>();
        var sections = new Dictionary<string, List<ShortcutEntry>>(StringComparer.OrdinalIgnoreCase);
        var byChord = new Dictionary<string, (string Section, int Index, ShortcutEntry Entry)>(StringComparer.OrdinalIgnoreCase);

        foreach (AppDefinition def in defs) // already ordered by layer
        {
            foreach (ShortcutSection section in def.Sections)
            {
                foreach (ShortcutEntry entry in section.Shortcuts)
                {
                    string chordKey = entry.KeysText;
                    if (byChord.TryGetValue(chordKey, out var existing))
                    {
                        // Same chord defined lower down: replace in place, keep position.
                        ShortcutEntry replacement = entry with
                        {
                            OverridesShipped = existing.Entry.Layer < entry.Layer,
                        };
                        sections[existing.Section][existing.Index] = replacement;
                        byChord[chordKey] = (existing.Section, existing.Index, replacement);
                    }
                    else
                    {
                        if (!sections.TryGetValue(section.Name, out List<ShortcutEntry>? list))
                        {
                            list = new List<ShortcutEntry>();
                            sections[section.Name] = list;
                            sectionOrder.Add(section.Name);
                        }
                        list.Add(entry);
                        byChord[chordKey] = (section.Name, list.Count - 1, entry);
                    }
                }
            }
        }

        // Metadata comes from the highest AUTHORED layer (User > Downloaded > Bundled).
        // Discovered definitions are supplements read from other apps' config — they
        // contribute entries, but must not rename an app or clobber VerifiedAgainst.
        AppDefinition top = defs.LastOrDefault(d => d.Layer != LibraryLayer.Discovered) ?? defs[^1];
        return top with
        {
            ProcessNames = defs.SelectMany(d => d.ProcessNames)
                .Select(AppMatcher.NormalizeProcessName).Distinct().ToList(),
            // Flat merged sections; the by-modifier index is rebuilt afterwards.
            Sections = sectionOrder
                .Select(name => new ShortcutSection(name, sections[name]))
                .ToList(),
        };
    }
}
