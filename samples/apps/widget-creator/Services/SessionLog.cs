using System;
using System.IO;
using System.Text;

namespace WidgetCreator.Services;

/// <summary>
/// Tiny append-only session log written next to the executable. Generation,
/// build, and sandbox steps all funnel through here so a single file captures
/// the full lifecycle even when the UI has moved on. Thread-safe via a lock —
/// services raise from background tasks.
/// </summary>
public static class SessionLog
{
    static readonly object _gate = new();
    static string? _path;

    public static string Path => _path ?? "(session log not initialised)";

    public static void Init()
    {
        lock (_gate)
        {
            if (_path is not null) return;
            var dir = System.IO.Path.Join(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            _path = System.IO.Path.Join(dir, $"widget-creator-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(_path, $"# Widget Creator session {DateTime.Now:O}{Environment.NewLine}");
        }
    }

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
        lock (_gate)
        {
            if (_path is null) return;
            try { File.AppendAllText(_path, line, Encoding.UTF8); }
            catch { /* logging must never throw */ }
        }
    }
}
