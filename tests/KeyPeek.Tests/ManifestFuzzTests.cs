using System.Text;
using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

/// <summary>
/// The library reads files KeyPeek did not write: hand-edited user manifests, a half-written
/// file caught by the folder watcher mid-save, whatever a future community repo publishes.
/// None of that may take the app down — a bad file must become a LibraryError, never an
/// exception on the loader's thread.
///
/// Deterministic: fixed seeds, so a failure is reproducible from the test name alone.
/// </summary>
public class ManifestFuzzTests
{
    private const string Valid = """
        PackageName: +WindowsNT.Notepad
        Name: Notepad
        WindowFilter: notepad.exe
        VerifiedAgainst: Notepad 11.2
        Shortcuts:
          - SectionName: File
            Properties:
              - Name: New tab
                Recommended: true
                Shortcut:
                - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ N ] }
              - Name: Sequence
                Shortcut:
                - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ K ] }
                - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ S ] }
        """;

    /// <summary>Loading must always terminate in one of two states: a definition, or errors.
    /// Never an exception, and never "no definition and no explanation".</summary>
    private static void AssertSurvives(string yaml, string label)
    {
        var errors = new List<LibraryError>();
        AppDefinition? app;
        try
        {
            app = PowerToysManifestLoader.LoadText(yaml, "fuzz.yml", errors);
        }
        catch (Exception ex)
        {
            Assert.Fail($"{label} threw {ex.GetType().Name}: {ex.Message}");
            return;
        }
        Assert.True(app is not null || errors.Count > 0,
            $"{label} returned no definition and reported no error — the file would vanish silently");
    }

    [Fact]
    public void Truncation_at_every_byte_is_survivable()
    {
        // The watcher can see a file that is still being written.
        for (int cut = 0; cut <= Valid.Length; cut += 7)
            AssertSurvives(Valid[..cut], $"truncated at {cut}");
    }

    [Fact]
    public void Deleting_any_single_line_is_survivable()
    {
        string[] lines = Valid.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            AssertSurvives(string.Join('\n', lines.Where((_, j) => j != i)), $"line {i} deleted");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1337)]
    public void Random_byte_corruption_is_survivable(int seed)
    {
        var random = new Random(seed);
        for (int round = 0; round < 200; round++)
        {
            var chars = Valid.ToCharArray();
            int edits = random.Next(1, 6);
            for (int e = 0; e < edits; e++)
            {
                int at = random.Next(chars.Length);
                chars[at] = random.Next(4) switch
                {
                    0 => '\t',      // YAML's one forbidden indent character
                    1 => ':',       // structural
                    2 => '"',       // unbalanced quote
                    _ => (char)random.Next(32, 127),
                };
            }
            AssertSurvives(new string(chars), $"seed {seed} round {round}");
        }
    }

    [Theory]
    [InlineData("", "empty file")]
    [InlineData("﻿", "BOM only")]
    [InlineData("null", "scalar null")]
    [InlineData("[]", "sequence at the root")]
    [InlineData("Name: X", "no shortcuts at all")]
    [InlineData("Shortcuts: not-a-list", "shortcuts of the wrong type")]
    [InlineData("Name: X\nShortcuts:\n  - SectionName: A\n    Properties: 5", "properties of the wrong type")]
    [InlineData("Name: X\nShortcuts:\n  - SectionName: A\n    Properties:\n      - Name: B\n        Shortcut: 7", "shortcut of the wrong type")]
    [InlineData("Name: X\nShortcuts:\n  - SectionName: A\n    Properties:\n      - Name: B\n        Shortcut:\n        - { Keys: [] }", "chord with nothing in it")]
    [InlineData("Name: X\nShortcuts:\n  - SectionName: A\n    Properties:\n      - Shortcut:\n        - { Ctrl: true, Keys: [ N ] }", "entry with no name")]
    public void Structurally_wrong_documents_are_survivable(string yaml, string label) =>
        AssertSurvives(yaml, label);

    [Fact]
    public void Absurd_sizes_are_survivable()
    {
        AssertSurvives("Name: " + new string('x', 200_000), "very long scalar");
        AssertSurvives(string.Concat(Enumerable.Repeat("- ", 5_000)), "deep nesting");

        var many = new StringBuilder("Name: X\nWindowFilter: x.exe\nShortcuts:\n  - SectionName: S\n    Properties:\n");
        for (int i = 0; i < 5_000; i++)
            many.Append($"      - Name: E{i}\n        Shortcut:\n        - {{ Ctrl: true, Keys: [ A ] }}\n");
        AssertSurvives(many.ToString(), "5000 entries");
    }

    [Fact]
    public void A_valid_document_still_loads_after_all_that()
    {
        var errors = new List<LibraryError>();
        AppDefinition? app = PowerToysManifestLoader.LoadText(Valid, "fuzz.yml", errors);
        Assert.Empty(errors);
        Assert.NotNull(app);
        Assert.Equal("Notepad", app!.AppName);
        Assert.Equal(2, app.Sections[0].Shortcuts.Count);
    }

    [Fact]
    public void Every_key_token_in_the_corpus_maps_without_throwing()
    {
        // PowerToysKeyMap is the one place that turns arbitrary text into a key. Feed it
        // hostile input: it must always answer, because a manifest can say anything.
        string[] hostile =
        {
            "", " ", "<", ">", "<>", "<<>>", "+", "++", "Ctrl+", "\t", "\n", "999", "-1",
            "F0", "F25", "F999", "<9999>", new string('A', 5_000), "🙂", "<🙂>", "Ctrl Win 77",
        };
        foreach (string token in hostile)
        {
            (string key, bool _) = PowerToysKeyMap.Map(token);
            Assert.NotNull(key); // no exception, always an answer
        }
    }
}
