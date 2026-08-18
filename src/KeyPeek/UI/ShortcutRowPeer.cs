using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using KeyPeek.Core;

namespace KeyPeek.UI;

/// <summary>
/// Makes a drawn shortcut row readable and operable by a screen reader.
///
/// ShortcutRowElement paints everything itself — caps, star, description — so there are no
/// child elements for automation to describe, and without this a Narrator user hears an
/// empty panel. A keyboard tool that a screen-reader user cannot read is a contradiction.
/// </summary>
internal sealed class ShortcutRowPeer : FrameworkElementAutomationPeer, IInvokeProvider
{
    private readonly ShortcutRowElement _owner;

    public ShortcutRowPeer(ShortcutRowElement owner) : base(owner) => _owner = owner;

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.ListItem;

    protected override string GetClassNameCore() => nameof(ShortcutRowElement);

    /// <summary>Spoken as "Ctrl+Shift+N, New incognito window". The chord text comes from
    /// ChordData, not the drawn labels: those are glyphs (⇧ ⏎ ↑) that a screen reader
    /// either skips or mangles.</summary>
    protected override string GetNameCore()
    {
        EntryVm? entry = _owner.Entry;
        if (entry is null)
            return "";
        string keys = string.Join(", ", entry.ChordData.Select(c => c.ToDisplayString()));
        return keys.Length > 0 ? $"{keys}, {entry.Description}" : entry.Description;
    }

    protected override string GetHelpTextCore() =>
        _owner.Entry is { Executable: false }
            ? "Reference only — this one cannot be run from KeyPeek."
            : "Press to run this shortcut in the app underneath.";

    protected override bool IsEnabledCore() => _owner.Entry?.Executable ?? false;

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

    /// <summary>Same gate as a mouse click: display-only rows (prose keys like
    /// "Underlined letter") are not sendable, so invoking them must do nothing.</summary>
    public void Invoke()
    {
        if (_owner.Entry is not { Executable: true } entry)
            return;
        if (_owner.InvokeCommand is { } command && command.CanExecute(entry))
            command.Execute(entry);
    }
}
