using System.IO;
using System.Runtime.InteropServices;
using KeyPeek.Core;

namespace KeyPeek.Services;

/// <summary>`KeyPeek --validate`: check every library file, print a report, exit.
/// A WinExe has no console, so we attach to the parent terminal's if there is one.</summary>
internal static class ValidateLibrary
{
    public static int Run(string? directory = null)
    {
        AttachConsole(unchecked((uint)-1)); // ATTACH_PARENT_PROCESS; fails harmlessly if none
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        string dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyPeek", "library");
        LibraryLoadResult result = LibraryLoader.LoadDirectory(dir);

        stdout.WriteLine();
        stdout.WriteLine($"KeyPeek library: {dir}");
        stdout.WriteLine($"  {result.Apps.Count} apps, {result.TotalShortcuts} shortcuts");
        foreach (LibraryError error in result.Errors)
            stdout.WriteLine($"  ERROR {error}");
        stdout.WriteLine(result.Errors.Count == 0 ? "  OK — no problems found" : $"  {result.Errors.Count} problem(s)");

        return result.Errors.Count;
    }

    /// <summary>`KeyPeek --convert-yaml <dir>`: rewrite each legacy *.json definition in
    /// <dir> as a PowerToys-format *.yml manifest beside it (the JSON is left in place).</summary>
    public static int ConvertToYaml(string directory)
    {
        AttachConsole(unchecked((uint)-1));
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        var errors = new List<LibraryError>();
        int converted = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            AppDefinition? app = LibraryLoader.LoadFile(file, errors);
            if (app is null)
                continue;
            string target = Path.ChangeExtension(file, ".yml");
            File.WriteAllText(target, PowerToysManifestLoader.Serialize(app));
            stdout.WriteLine($"  {Path.GetFileName(file)} → {Path.GetFileName(target)}");
            converted++;
        }
        foreach (LibraryError error in errors)
            stdout.WriteLine($"  ERROR {error}");
        stdout.WriteLine($"  {converted} file(s) converted, {errors.Count} error(s)");
        return errors.Count;
    }

    /// <summary>`KeyPeek --migrate <dir>`: rewrite legacy files to the v2 table format.</summary>
    public static int Migrate(string directory)
    {
        AttachConsole(unchecked((uint)-1));
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        LibraryLoadResult result = LibraryLoader.LoadDirectory(directory);
        foreach (LibraryError error in result.Errors)
            stdout.WriteLine($"  ERROR {error}");
        if (result.Errors.Count > 0)
        {
            stdout.WriteLine("  fix the errors above first — nothing was rewritten");
            return result.Errors.Count;
        }

        int migrated = 0;
        foreach (AppDefinition app in result.Apps.Where(LibraryMigrator.NeedsMigration))
        {
            File.WriteAllText(app.SourceFile, LibraryLoader.Serialize(LibraryMigrator.ToTables(app)));
            stdout.WriteLine($"  migrated {Path.GetFileName(app.SourceFile)}");
            migrated++;
        }
        stdout.WriteLine($"  {migrated} file(s) rewritten to the v2 table format");
        return 0;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint dwProcessId);
}
