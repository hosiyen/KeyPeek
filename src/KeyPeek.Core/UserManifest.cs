namespace KeyPeek.Core;

/// <summary>
/// Rules for the user layer — the one layer KeyPeek writes and updates never touch.
///
/// The subtle part is metadata. The merger takes an app's name, package, VerifiedAgainst
/// and Updated from the highest *authored* layer, which is the user file as soon as one
/// exists. A user file containing only the edited shortcut would therefore blank the app's
/// name and its "checked against version X" badge. So a user manifest is always a clone of
/// the merged definition with its own Sections — never a bare fragment.
/// </summary>
public static class UserManifest
{
    public const string DefaultSection = "My shortcuts";

    /// <summary>File name for an app's user manifest (no directory). Definitions that share
    /// a process but differ by title (Chrome vs Chrome-showing-Gmail) are different apps to
    /// the merger, so they must not share one file — the title is folded into the name.</summary>
    public static string FileNameFor(AppDefinition app)
    {
        string stem = app.IsGlobal
            ? "windows"
            : AppMatcher.NormalizeProcessName(app.ProcessNames.FirstOrDefault() ?? app.AppName);
        if (!string.IsNullOrWhiteSpace(app.TitleRegex))
            stem += "-" + Slug(app.TitleRegex!);
        return stem + ".user.yml";
    }

    /// <summary>A short, file-safe tag from a title pattern. Not reversible — it only has
    /// to be stable and distinct.</summary>
    private static string Slug(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(16).Select(char.ToLowerInvariant).ToArray();
        // & int.MaxValue rather than Math.Abs, which throws on int.MinValue — a title regex
        // of pure punctuation that happened to hash there would crash the editor on open.
        return chars.Length > 0 ? new string(chars) : (value.GetHashCode() & int.MaxValue).ToString();
    }

    /// <summary>An empty user manifest for an app: same identity (so it MERGES with the
    /// shipped definition instead of forking a second app), no shortcuts yet.</summary>
    public static AppDefinition CreateFor(AppDefinition mergedApp, string path) =>
        mergedApp with
        {
            SourceFile = path,
            Layer = LibraryLayer.User,
            Sections = new[] { new ShortcutSection(DefaultSection, Array.Empty<ShortcutEntry>()) },
        };

    /// <summary>
    /// Adds or replaces an entry. Matching is by rendered chord text, exactly as the merger
    /// matches overrides — so editing the description of a shortcut the user already
    /// overrode updates that entry rather than listing the chord twice.
    /// </summary>
    public static AppDefinition WithEntry(AppDefinition userDef, ShortcutEntry entry,
        string? sectionName = null)
    {
        string section = string.IsNullOrWhiteSpace(sectionName) ? DefaultSection : sectionName.Trim();
        var sections = userDef.Sections.ToList();
        int target = sections.FindIndex(s => string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase));

        // Replace in place where the chord already lives in the section it is going to, so
        // re-saving an app does not shuffle the user's own rows to the bottom of the list
        // every time. Elsewhere, drop it — a chord appears once.
        bool replaced = false;
        for (int i = 0; i < sections.Count; i++)
        {
            var rows = sections[i].Shortcuts.ToList();
            int at = rows.FindIndex(s =>
                string.Equals(s.KeysText, entry.KeysText, StringComparison.OrdinalIgnoreCase));
            if (at < 0)
                continue;
            if (i == target)
            {
                rows[at] = entry;
                replaced = true;
            }
            else
            {
                rows.RemoveAt(at);
            }
            sections[i] = sections[i] with { Shortcuts = rows };
        }

        if (!replaced)
        {
            if (target < 0)
                sections.Add(new ShortcutSection(section, new[] { entry }));
            else
                sections[target] = sections[target] with
                {
                    Shortcuts = sections[target].Shortcuts.Append(entry).ToList(),
                };
        }

        return userDef with { Sections = PruneEmpty(sections) };
    }

    /// <summary>Removes the entry with this chord text. Only the user's own copy goes — a
    /// bundled shortcut reappears, which is the honest outcome: KeyPeek cannot un-ship it.</summary>
    public static AppDefinition WithoutEntry(AppDefinition userDef, string keysText)
    {
        var sections = userDef.Sections
            .Select(s => s with
            {
                Shortcuts = s.Shortcuts
                    .Where(x => !string.Equals(x.KeysText, keysText, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
            })
            .ToList();
        return userDef with { Sections = PruneEmpty(sections) };
    }

    /// <summary>
    /// Reconcile the edits held in memory with the file as it stands on disk right now.
    ///
    /// The editor can sit open for a long time, and its own "Open the file" button invites a
    /// text editor alongside it, so a blind write of a constructor-time snapshot would throw
    /// away whatever was saved in between. Starting from the disk copy fixes that — but on
    /// its own it also resurrects everything the user just deleted, because a deletion is the
    /// absence of an entry and absence adds nothing back. Hence <paramref name="removedKeys"/>:
    /// deletions have to travel as their own instruction, not as a gap.
    /// </summary>
    public static AppDefinition MergeOverDisk(AppDefinition onDisk, AppDefinition mine,
        IEnumerable<string> removedKeys)
    {
        AppDefinition merged = onDisk;
        foreach (string key in removedKeys)
            merged = WithoutEntry(merged, key);
        foreach (ShortcutSection section in mine.Sections)
            foreach (ShortcutEntry entry in section.Shortcuts)
                merged = WithEntry(merged, entry, section.Name);
        return merged;
    }

    public static bool IsEmpty(AppDefinition userDef) =>
        userDef.Sections.All(s => s.Shortcuts.Count == 0);

    /// <summary>Keep one empty section so the file still describes the app when the user
    /// deletes their last edit; drop the rest.</summary>
    private static IReadOnlyList<ShortcutSection> PruneEmpty(List<ShortcutSection> sections)
    {
        var kept = sections.Where(s => s.Shortcuts.Count > 0).ToList();
        return kept.Count > 0
            ? kept
            : new List<ShortcutSection> { new(DefaultSection, Array.Empty<ShortcutEntry>()) };
    }
}
