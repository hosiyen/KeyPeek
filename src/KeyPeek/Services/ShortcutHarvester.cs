using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using KeyPeek.Core;

namespace KeyPeek.Services;

/// <summary>
/// `KeyPeek --harvest &lt;process&gt;`: read an app's own UI to draft a shortcut definition.
///
/// Windows exposes an AcceleratorKey property on menu items and commands through UI
/// Automation, so an app that has no manifest can still describe itself. This is a
/// PROTOTYPE whose purpose is to measure the yield on real apps — it writes a draft to the
/// reports folder and prints what it found, and nothing feeds the library automatically.
///
/// Strictly read-only: it reads properties, never invokes a pattern, never opens a menu.
/// Opening menus would raise the yield (most apps only build menu items when shown) but it
/// would also mean driving someone's application behind their back.
/// </summary>
internal static class ShortcutHarvester
{
    private const int MaxDepth = 12;
    private const int MaxElements = 3000;
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(5);

    public static int Run(string processName)
    {
        AttachConsole(unchecked((uint)-1));
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        string wanted = AppMatcher.NormalizeProcessName(processName);
        Process[] processes = Process.GetProcesses()
            .Where(p => string.Equals(p.ProcessName, wanted, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (processes.Length == 0)
        {
            stdout.WriteLine($"No running process named \"{processName}\".");
            return 2;
        }

        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // keys → name
        var failures = new List<string>();
        int windows = 0, visited = 0;
        var clock = Stopwatch.StartNew();

        foreach (Process process in processes)
        {
            foreach (AutomationElement window in TopLevelWindowsOf(process))
            {
                windows++;
                Walk(window, 0, ref visited, found, failures);
            }
        }

        stdout.WriteLine();
        stdout.WriteLine($"KeyPeek harvest: {processName}");
        stdout.WriteLine($"  {windows} window(s), {visited} elements walked in {clock.ElapsedMilliseconds} ms");
        stdout.WriteLine($"  {found.Count} shortcut(s) found, {failures.Count} unparsable");
        foreach (var pair in found.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            stdout.WriteLine($"    {pair.Key,-22} {pair.Value}");
        foreach (string failure in failures.Distinct().Take(20))
            stdout.WriteLine($"    (skipped) {failure}");

        if (found.Count > 0)
        {
            string path = WriteDraft(processName, found);
            stdout.WriteLine($"  draft written to {path}");
            stdout.WriteLine("  Review it before copying anything into the library folder.");
        }
        return 0;
    }

    private static IEnumerable<AutomationElement> TopLevelWindowsOf(Process process)
    {
        AutomationElement? root;
        try
        {
            root = AutomationElement.RootElement;
        }
        catch (Exception)
        {
            yield break; // UIA unavailable (rare, but never worth crashing over)
        }
        if (root is null)
            yield break;

        AutomationElementCollection children;
        try
        {
            children = root.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
        }
        catch (Exception)
        {
            yield break;
        }
        foreach (AutomationElement child in children)
            yield return child;
    }

    /// <summary>Depth-first walk with hard caps. UIA trees can be enormous and slow, and a
    /// hung provider blocks the caller — bail rather than freeze.</summary>
    private static void Walk(AutomationElement element, int depth, ref int visited,
        Dictionary<string, string> found, List<string> failures)
    {
        if (depth > MaxDepth || visited >= MaxElements)
            return;
        visited++;

        try
        {
            string accelerator = element.Current.AcceleratorKey;
            string name = element.Current.Name;
            if (!string.IsNullOrWhiteSpace(accelerator) && !string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    IReadOnlyList<KeyChord> chords = KeyChordParser.Parse(accelerator);
                    string keys = string.Join(" ", chords.Select(c => c.ToDisplayString()));
                    found.TryAdd(keys, name.Trim());
                }
                catch (FormatException ex)
                {
                    failures.Add($"{accelerator} — {ex.Message}");
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            return; // the element went away mid-walk: normal in a live UI
        }
        catch (Exception)
        {
            return;
        }

        AutomationElementCollection children;
        try
        {
            children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
        }
        catch (Exception)
        {
            return;
        }
        foreach (AutomationElement child in children)
        {
            if (visited >= MaxElements)
                return;
            Walk(child, depth + 1, ref visited, found, failures);
        }
    }

    /// <summary>Writes to the reports folder, never the library: a draft is a starting point
    /// for a human, not a definition.</summary>
    private static string WriteDraft(string processName, Dictionary<string, string> found)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyPeek", "reports");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"harvest-{AppMatcher.NormalizeProcessName(processName)}.yml");

        var entries = found
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ShortcutEntry
            {
                Chords = KeyChordParser.Parse(p.Key),
                Description = p.Value,
                RawKeys = p.Key,
            })
            .ToList();

        var draft = new AppDefinition
        {
            AppName = processName,
            ProcessNames = new[] { AppMatcher.NormalizeProcessName(processName) },
            Sections = new[] { new ShortcutSection("Harvested from the app's menus", entries) },
            SourceFile = path,
        };
        File.WriteAllText(path, PowerToysManifestLoader.Serialize(draft));
        return path;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint dwProcessId);
}
