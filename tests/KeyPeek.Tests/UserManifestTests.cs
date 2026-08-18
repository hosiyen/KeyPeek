using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class UserManifestTests
{
    private static AppDefinition Shipped() => new()
    {
        AppName = "Notepad",
        PackageName = "+WindowsNT.Notepad",
        ProcessNames = new[] { "notepad" },
        VerifiedAgainst = "Notepad 11.2",
        Updated = "2026-08-01",
        SourceFile = "bundled/+WindowsNT.Notepad.yml",
        Sections = new[]
        {
            new ShortcutSection("File", new[] { Entry("Ctrl+N", "New window") }),
        },
    };

    private static ShortcutEntry Entry(string keys, string description) => new()
    {
        Chords = KeyChordParser.Parse(keys),
        Description = description,
        RawKeys = keys,
    };

    [Fact]
    public void A_new_user_manifest_keeps_the_apps_identity_and_metadata()
    {
        AppDefinition shipped = Shipped();
        AppDefinition user = UserManifest.CreateFor(shipped, @"C:\lib\notepad.user.yml");

        // If any of these were dropped, the merged app would lose its name or its
        // "verified against" badge as soon as the user edited one shortcut.
        Assert.Equal(shipped.AppName, user.AppName);
        Assert.Equal(shipped.PackageName, user.PackageName);
        Assert.Equal(shipped.VerifiedAgainst, user.VerifiedAgainst);
        Assert.Equal(shipped.ProcessNames, user.ProcessNames);
        Assert.Equal(shipped.TitleRegex, user.TitleRegex); // must match or it forks a new app
        Assert.Equal(shipped.MergeKey, user.MergeKey);
        Assert.True(UserManifest.IsEmpty(user));
    }

    [Fact]
    public void Adding_an_entry_puts_it_in_the_named_section()
    {
        AppDefinition user = UserManifest.CreateFor(Shipped(), "x.yml");
        user = UserManifest.WithEntry(user, Entry("Ctrl+Shift+F9", "Test entry"), "Custom");

        ShortcutSection section = Assert.Single(user.Sections);
        Assert.Equal("Custom", section.Name);
        Assert.Equal("Test entry", Assert.Single(section.Shortcuts).Description);
    }

    [Fact]
    public void Re_adding_the_same_chord_replaces_it_instead_of_duplicating()
    {
        AppDefinition user = UserManifest.CreateFor(Shipped(), "x.yml");
        user = UserManifest.WithEntry(user, Entry("Ctrl+N", "My new window"));
        user = UserManifest.WithEntry(user, Entry("Ctrl+N", "Renamed"), "Elsewhere");

        ShortcutEntry only = Assert.Single(user.Sections.SelectMany(s => s.Shortcuts));
        Assert.Equal("Renamed", only.Description);
        Assert.Equal("Elsewhere", Assert.Single(user.Sections).Name);
    }

    [Fact]
    public void Removing_the_last_entry_leaves_a_valid_empty_manifest()
    {
        AppDefinition user = UserManifest.CreateFor(Shipped(), "x.yml");
        user = UserManifest.WithEntry(user, Entry("Ctrl+N", "Mine"));
        user = UserManifest.WithoutEntry(user, "Ctrl+N");

        Assert.True(UserManifest.IsEmpty(user));
        Assert.Single(user.Sections); // still describes the app, so metadata survives
    }

    [Fact]
    public void A_user_entry_overrides_the_shipped_one_rather_than_forking_the_app()
    {
        AppDefinition shipped = Shipped();
        AppDefinition user = UserManifest.WithEntry(
            UserManifest.CreateFor(shipped, "notepad.user.yml"),
            Entry("Ctrl+N", "My own description"));

        var merged = LibraryMerger.Merge(new[]
        {
            (LibraryLayer.Bundled, (IReadOnlyList<AppDefinition>)new[] { shipped }),
            (LibraryLayer.User, (IReadOnlyList<AppDefinition>)new[] { user }),
        });

        AppDefinition app = Assert.Single(merged); // one app, not two
        ShortcutEntry entry = Assert.Single(app.Sections.SelectMany(s => s.Shortcuts));
        Assert.Equal("My own description", entry.Description);
        Assert.Equal("Notepad", app.AppName);
        Assert.Equal("Notepad 11.2", app.VerifiedAgainst); // badge survived the edit
    }

    [Fact]
    public void The_file_name_is_derived_from_the_process_not_the_display_name()
    {
        Assert.Equal("notepad.user.yml", UserManifest.FileNameFor(Shipped()));
        Assert.Equal("windows.user.yml", UserManifest.FileNameFor(Shipped() with
        {
            IsGlobal = true,
            ProcessNames = Array.Empty<string>(),
        }));
    }
}

public class ChordCaptureTests
{
    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x30, "0")]
    [InlineData(0x78, "F9")]
    [InlineData(0xBB, "+")]   // OemPlus — must spell the way manifests do
    [InlineData(0xBC, ",")]
    [InlineData(0x25, "Left")]
    [InlineData(0x0D, "Enter")]
    public void Captures_canonical_key_names(int vk, string expected)
    {
        KeyChord? chord = ChordCapture.FromKeyPress(vk, Modifiers.Ctrl);
        Assert.NotNull(chord);
        Assert.Equal(expected, chord!.Key);
        Assert.Equal(Modifiers.Ctrl, chord.Mods);
    }

    [Theory]
    [InlineData(0xA2)] // Ctrl
    [InlineData(0x5B)] // Win
    [InlineData(0x10)] // Shift
    public void A_lone_modifier_is_not_a_shortcut(int vk) =>
        Assert.Null(ChordCapture.FromKeyPress(vk, Modifiers.None));

    [Theory]
    [InlineData(0x60)] // Numpad0 — no canonical spelling in the format
    [InlineData(0xAD)] // Volume mute
    public void Keys_the_format_cannot_spell_are_refused(int vk) =>
        Assert.Null(ChordCapture.FromKeyPress(vk, Modifiers.Ctrl));

    [Fact]
    public void Captured_chords_round_trip_through_the_parser()
    {
        KeyChord? captured = ChordCapture.FromKeyPress(0xBB, Modifiers.Ctrl | Modifiers.Shift);
        string text = captured!.ToDisplayString();
        KeyChord reparsed = Assert.Single(KeyChordParser.Parse(text));
        Assert.Equal(captured, reparsed);
    }
}

// ---- merging our edits with the file as it stands on disk ----

public class UserManifestMergeTests
{
    private static AppDefinition Shipped() => new()
    {
        AppName = "Notepad",
        PackageName = "+WindowsNT.Notepad",
        ProcessNames = new[] { "notepad" },
        VerifiedAgainst = "Notepad 11.2",
        SourceFile = "bundled/+WindowsNT.Notepad.yml",
        Sections = Array.Empty<ShortcutSection>(),
    };

    private static ShortcutEntry Entry(string keys, string description) => new()
    {
        Chords = KeyChordParser.Parse(keys),
        Description = description,
        RawKeys = keys,
    };

    private static AppDefinition UserFile(params ShortcutEntry[] entries) =>
        UserManifest.CreateFor(Shipped(), @"C:\lib\notepad.user.yml") with
        {
            Sections = new[] { new ShortcutSection(UserManifest.DefaultSection, entries) },
        };

    private static IEnumerable<string> Keys(AppDefinition def) =>
        def.Sections.SelectMany(s => s.Shortcuts).Select(s => s.KeysText);

    [Fact]
    public void A_delete_survives_the_merge_with_disk()
    {
        // The regression this pins: the editor re-reads the file before writing so it does
        // not clobber a concurrent text edit. Starting from the disk copy and re-applying
        // our entries loses nothing — except a deletion, which IS the absence of an entry
        // and so adds nothing back. "Remove" then silently did nothing.
        AppDefinition onDisk = UserFile(Entry("Ctrl+1", "One"), Entry("Ctrl+2", "Two"));
        AppDefinition mine = UserManifest.WithoutEntry(onDisk, "Ctrl+2");

        AppDefinition merged = UserManifest.MergeOverDisk(onDisk, mine, new[] { "Ctrl+2" });

        Assert.Equal(new[] { "Ctrl+1" }, Keys(merged));
    }

    [Fact]
    public void An_edit_made_in_a_text_editor_meanwhile_is_not_clobbered()
    {
        AppDefinition onDisk = UserFile(Entry("Ctrl+1", "One"), Entry("Ctrl+9", "Added in Notepad"));
        AppDefinition mine = UserManifest.WithEntry(UserFile(Entry("Ctrl+1", "One")),
            Entry("Ctrl+3", "Three"));

        AppDefinition merged = UserManifest.MergeOverDisk(onDisk, mine, Array.Empty<string>());

        // Order matters too: re-applying a row we already had must leave it where it was,
        // or every save quietly reshuffles the user's list.
        Assert.Equal(new[] { "Ctrl+1", "Ctrl+9", "Ctrl+3" }, Keys(merged));
    }

    [Fact]
    public void Our_version_of_a_shortcut_wins_over_the_disk_copy()
    {
        AppDefinition onDisk = UserFile(Entry("Ctrl+1", "Stale description"));
        AppDefinition mine = UserFile(Entry("Ctrl+1", "What I just typed"));

        AppDefinition merged = UserManifest.MergeOverDisk(onDisk, mine, Array.Empty<string>());

        ShortcutEntry only = Assert.Single(merged.Sections.SelectMany(s => s.Shortcuts));
        Assert.Equal("What I just typed", only.Description);
    }

    [Fact]
    public void Re_adding_a_chord_you_just_deleted_keeps_it()
    {
        // Delete Ctrl+1, then add Ctrl+1 again with a new description, then save. Removals
        // are applied before additions, so the second thought wins.
        AppDefinition onDisk = UserFile(Entry("Ctrl+1", "Old"));
        AppDefinition mine = UserManifest.WithEntry(
            UserManifest.WithoutEntry(onDisk, "Ctrl+1"), Entry("Ctrl+1", "New"));

        AppDefinition merged = UserManifest.MergeOverDisk(onDisk, mine, new[] { "Ctrl+1" });

        ShortcutEntry only = Assert.Single(merged.Sections.SelectMany(s => s.Shortcuts));
        Assert.Equal("New", only.Description);
    }

    [Fact]
    public void Deleting_the_last_shortcut_leaves_a_file_that_still_describes_the_app()
    {
        AppDefinition onDisk = UserFile(Entry("Ctrl+1", "One"));
        AppDefinition mine = UserManifest.WithoutEntry(onDisk, "Ctrl+1");

        AppDefinition merged = UserManifest.MergeOverDisk(onDisk, mine, new[] { "Ctrl+1" });

        Assert.True(UserManifest.IsEmpty(merged));
        Assert.Equal("Notepad", merged.AppName);
        Assert.Equal("Notepad 11.2", merged.VerifiedAgainst);
    }
}
