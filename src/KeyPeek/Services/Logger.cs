using System.IO;

namespace KeyPeek.Services;

/// <summary>
/// Minimal rolling file logger.
///
/// PRIVACY: this log records app lifecycle events and which application a completed
/// hold resolved to — never individual keystrokes. Nothing here (or anywhere else in
/// KeyPeek) leaves the machine.
/// </summary>
internal sealed class Logger : IDisposable
{
    private const long MaxBytes = 1_000_000;
    private readonly object _lock = new();
    private StreamWriter? _writer;

    public string LogDirectory { get; }
    public string LogPath { get; }

    public Logger()
    {
        LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyPeek", "logs");
        Directory.CreateDirectory(LogDirectory);
        LogPath = Path.Combine(LogDirectory, "keypeek.log");
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
            {
                string old = Path.Combine(LogDirectory, "keypeek.old.log");
                File.Delete(old);
                File.Move(LogPath, old);
            }
            _writer = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                System.Text.Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            _writer = null; // logging is best-effort; the app must run without it
        }
    }

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            _writer?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
        }
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
    }

    public void Dispose()
    {
        lock (_lock) { _writer?.Dispose(); _writer = null; }
    }
}
