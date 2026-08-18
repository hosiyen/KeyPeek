using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class KeyChordParserTests
{
    private static KeyChord One(string keys) => Assert.Single(KeyChordParser.Parse(keys));

    [Fact]
    public void Simple_chord()
    {
        var c = One("Ctrl+Shift+P");
        Assert.Equal(Modifiers.Ctrl | Modifiers.Shift, c.Mods);
        Assert.Equal("P", c.Key);
    }

    [Theory]
    [InlineData("Control+C")]
    [InlineData("ctrl+c")]
    [InlineData("CTL+C")]
    public void Ctrl_aliases_and_case(string keys)
    {
        var c = One(keys);
        Assert.Equal(Modifiers.Ctrl, c.Mods);
        Assert.Equal("C", c.Key);
    }

    [Theory]
    [InlineData("Win+E", Modifiers.Win)]
    [InlineData("Meta+E", Modifiers.Win)]
    [InlineData("Super+E", Modifiers.Win)]
    [InlineData("Windows+E", Modifiers.Win)]
    [InlineData("Option+E", Modifiers.Alt)]
    public void Modifier_aliases(string keys, Modifiers expected)
    {
        Assert.Equal(expected, One(keys).Mods);
    }

    [Theory]
    [InlineData("Ctrl+Enter", "Enter")]
    [InlineData("Ctrl+Return", "Enter")]
    [InlineData("Escape", "Esc")]
    [InlineData("Ctrl+Del", "Delete")]
    [InlineData("PgUp", "PageUp")]
    [InlineData("Ctrl+Grave", "`")]
    [InlineData("Ctrl+`", "`")]
    [InlineData("Win+.", ".")]
    [InlineData("Ctrl+Plus", "+")]
    [InlineData("Ctrl+F12", "F12")]
    [InlineData("f5", "F5")]
    public void Key_aliases_normalize(string keys, string expectedKey)
    {
        Assert.Equal(expectedKey, One(keys).Key);
    }

    [Fact]
    public void Ctrl_comma_is_the_comma_key_not_a_sequence_separator()
    {
        var c = One("Ctrl+,");
        Assert.Equal(Modifiers.Ctrl, c.Mods);
        Assert.Equal(",", c.Key);
    }

    [Fact]
    public void Literal_plus_via_double_plus()
    {
        var c = One("Ctrl++");
        Assert.Equal(Modifiers.Ctrl, c.Mods);
        Assert.Equal("+", c.Key);
    }

    [Fact]
    public void Spaces_around_plus_are_tolerated()
    {
        var c = One("Ctrl + Shift + P");
        Assert.Equal(Modifiers.Ctrl | Modifiers.Shift, c.Mods);
        Assert.Equal("P", c.Key);
    }

    [Fact]
    public void Modifier_only_chord_is_valid()
    {
        var c = One("Win");
        Assert.Equal(Modifiers.Win, c.Mods);
        Assert.Equal("", c.Key);
    }

    [Fact]
    public void Multi_chord_sequence()
    {
        var chords = KeyChordParser.Parse("Ctrl+K Ctrl+S");
        Assert.Equal(2, chords.Count);
        Assert.Equal(new KeyChord(Modifiers.Ctrl, "K"), chords[0]);
        Assert.Equal(new KeyChord(Modifiers.Ctrl, "S"), chords[1]);
    }

    [Fact]
    public void Comma_separated_sequence()
    {
        var chords = KeyChordParser.Parse("Ctrl+K, Ctrl+S");
        Assert.Equal(2, chords.Count);
    }

    [Fact]
    public void Display_order_is_canonical()
    {
        var c = One("Shift+Alt+Ctrl+Win+P");
        Assert.Equal("Win+Ctrl+Alt+Shift+P", c.ToDisplayString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Bogus")]
    [InlineData("Ctrl+Ctrl+C")]
    [InlineData("Ctrl+A+B")]
    [InlineData("F25")]
    public void Malformed_input_throws(string keys)
    {
        Assert.Throws<FormatException>(() => KeyChordParser.Parse(keys));
    }

    [Fact]
    public void Error_message_names_the_bad_key()
    {
        var ex = Assert.Throws<FormatException>(() => KeyChordParser.Parse("Ctrl+Shift+Blorp"));
        Assert.Contains("Blorp", ex.Message);
    }
}
