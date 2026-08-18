namespace KeyPeek.Core;

/// <summary>
/// Collapses the by-modifier index back into the authored shape for BROWSING.
///
/// The overlay deliberately duplicates entries across tables (Ctrl+Alt+Tab belongs in
/// both the ctrl and alt views), and ToTables splits one authored section into several
/// same-named per-table sections. Rendering that expansion in the library browser
/// showed "Desktop Shortcuts" twice with duplicated rows — a bug in the view, fixed
/// here: one section per name (first-seen order), entries de-duplicated by
/// chord+description, keeping the winning (highest) layer's copy so provenance and
/// overrides still show correctly.
/// </summary>
public static class AppDefinitionDisplay
{
    public static IReadOnlyList<ShortcutSection> DisplaySections(this AppDefinition app)
    {
        var order = new List<string>();
        var sections = new Dictionary<string, List<ShortcutEntry>>(StringComparer.OrdinalIgnoreCase);
        var position = new Dictionary<string, (string Section, int Index)>(StringComparer.OrdinalIgnoreCase);

        foreach (ShortcutSection section in app.Sections)
        {
            if (!sections.TryGetValue(section.Name, out List<ShortcutEntry>? list))
            {
                list = new List<ShortcutEntry>();
                sections[section.Name] = list;
                order.Add(section.Name);
            }

            foreach (ShortcutEntry entry in section.Shortcuts)
            {
                string key = $"{entry.KeysText}|{entry.Description}";
                if (position.TryGetValue(key, out (string Section, int Index) at))
                {
                    // Same chord+description seen again (table expansion or a lower
                    // layer): keep the higher layer's copy, in the original position.
                    if (entry.Layer > sections[at.Section][at.Index].Layer)
                        sections[at.Section][at.Index] = entry;
                }
                else
                {
                    list.Add(entry);
                    position[key] = (section.Name, list.Count - 1);
                }
            }
        }

        return order.Select(name => new ShortcutSection(name, sections[name])).ToList();
    }
}
