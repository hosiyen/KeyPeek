using System.Reflection;

namespace KeyPeek.Core;

/// <summary>
/// Vietnamese translations for the shortcut LIBRARY — descriptions and section names.
///
/// This is deliberately a separate layer from <see cref="L10n"/> (the app's own chrome):
/// the library corpus is data, ~2,200 unique strings that change with library updates, so
/// its translations live as data too — one embedded TSV, English on the left, exact-match
/// at display time, and any string the table does not know simply stays English. The
/// manifests themselves are never touched: the on-disk format stays byte-compatible with
/// PowerToys, and an English-language machine renders exactly what it always did.
/// </summary>
public static class ShortcutL10n
{
    private const string ResourceName = "KeyPeek.Core.Resources.vi-shortcuts.tsv";

    private static Dictionary<string, string>? _vi;

    private static Dictionary<string, string> Vi => _vi ??= Load();

    /// <summary>Translate a library string (a shortcut description or a section name) into
    /// the current UI language. English in, English out unless the language is Vietnamese
    /// AND the table knows the string — a missing translation is never a hole in the UI.</summary>
    public static string T(string libraryText) =>
        L10n.Language == UiLanguage.Vietnamese && Vi.TryGetValue(libraryText, out string? vi)
            ? vi
            : libraryText;

    public static int Count => Vi.Count;

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(2400, StringComparer.Ordinal);
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
                return map; // no resource (source build without the data file): English everywhere
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                int tabAt = line.IndexOf('\t');
                if (tabAt <= 0 || tabAt == line.Length - 1)
                    continue;
                map[line[..tabAt]] = line[(tabAt + 1)..];
            }
        }
        catch (Exception)
        {
            // A malformed resource must degrade to English, never to a crash at first render.
            map.Clear();
        }
        return map;
    }
}
