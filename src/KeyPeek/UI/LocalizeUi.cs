using System.Windows;
using System.Windows.Controls;
using KeyPeek.Core;

namespace KeyPeek.UI;

/// <summary>
/// Applies the current UI language to a window that was written in English.
///
/// The XAML stays English — it is the source of truth and the translation key — and this
/// walks the logical tree translating every string it recognises. The table is
/// bidirectional, so calling this again after a language switch re-translates text that is
/// already on screen; strings it does not recognise (user input, app names, key caps) are
/// left exactly as they are. Anything built later in code goes through
/// <see cref="L10n.T"/> at the call site instead.
/// </summary>
internal static class LocalizeUi
{
    public static void Apply(Window window)
    {
        if (window.Title is { Length: > 0 } title && L10n.TryLocalize(title) is { } newTitle)
            window.Title = newTitle;
        Walk(window);
    }

    private static void Walk(object node)
    {
        if (node is not DependencyObject d)
            return;

        switch (node)
        {
            case TextBlock text:
                if (L10n.TryLocalize(text.Text) is { } translated)
                    text.Text = translated;
                break;

            // Buttons, toggles, labels, list items with a plain string face.
            case ContentControl { Content: string content } control:
                if (L10n.TryLocalize(content) is { } newContent)
                    control.Content = newContent;
                break;
        }

        if (node is HeaderedContentControl { Header: string header } headered &&
            L10n.TryLocalize(header) is { } newHeader)
            headered.Header = newHeader;

        if (node is FrameworkElement { ToolTip: string tip } element &&
            L10n.TryLocalize(tip) is { } newTip)
            element.ToolTip = newTip;

        foreach (object child in LogicalTreeHelper.GetChildren(d))
            Walk(child);
    }
}
