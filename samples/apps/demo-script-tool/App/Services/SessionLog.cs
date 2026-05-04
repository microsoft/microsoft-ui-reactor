using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace DemoScriptTool.App.Services;

/// <summary>
/// File-backed log for diagnosing the app after the fact. Each app launch
/// opens a fresh <c>session-YYYYMMDD-HHMMSS.log</c> file under
/// <c>%LOCALAPPDATA%\DemoScriptTool\logs\</c>; the last 10 sessions are kept
/// and older files are pruned. Lines are timestamped and flushed on every
/// write so a crash leaves no buffered tail behind.
///
/// <para>
/// All writes ALSO mirror to <see cref="System.Diagnostics.Debug.WriteLine(string)"/>
/// so the existing dev-time inner loop (debugger Output / mur devtools logs)
/// keeps working unchanged. Use <see cref="Write"/> instead of bare
/// <c>Debug.WriteLine</c> in this app so events survive across runs without
/// requiring an attached debugger.
/// </para>
///
/// <para>
/// Logs are intentionally outside the project root — accidental commits of
/// debug spew aren't a risk, and the "Reveal log folder" devtools menu item
/// gives a one-click route to share a session log when something goes wrong.
/// </para>
/// </summary>
public static class SessionLog
{
    const int MaxRetainedSessions = 10;

    static readonly object _gate = new();
    static StreamWriter? _writer;

    /// <summary>Path of the current session's log file once <see cref="Init"/> has run.</summary>
    public static string? CurrentPath { get; private set; }

    /// <summary>Directory that holds rotated session logs.</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemoScriptTool", "logs");

    /// <summary>
    /// Open the session log file. Safe to call multiple times — second and
    /// later calls are no-ops. Failures (read-only volume, permission issues,
    /// concurrent locking) fall back to debugger-only logging via the mirror
    /// in <see cref="Write"/>; the app keeps running.
    /// </summary>
    public static void Init()
    {
        if (_writer is not null) return;

        try
        {
            Directory.CreateDirectory(LogDirectory);

            // Prune older sessions BEFORE opening the new one so the directory
            // doesn't grow unbounded across many launches.
            try
            {
                var stale = Directory.GetFiles(LogDirectory, "session-*.log")
                    .OrderByDescending(p => p)
                    .Skip(MaxRetainedSessions - 1)
                    .ToArray();
                foreach (var path in stale)
                {
                    try { File.Delete(path); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SessionLog] prune skip '{path}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionLog] prune failed: {ex.Message}");
            }

            var name = $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log";
            CurrentPath = Path.Combine(LogDirectory, name);
            // FileShare.Read so an external tail / editor can read the live
            // file while we're still writing.
            var stream = new FileStream(CurrentPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream) { AutoFlush = true };

            var os = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            var clr = Environment.Version;
            WriteInternal($"=== Session started {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} ===");
            WriteInternal($"=== OS={os} arch={arch} CLR={clr} pid={Environment.ProcessId} ===");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionLog] init failed: {ex.Message}");
            _writer = null;
            CurrentPath = null;
        }
    }

    /// <summary>
    /// Append <paramref name="line"/> to the session log with a timestamp
    /// and mirror to the debugger. Thread-safe; never throws (failures are
    /// swallowed so logging itself doesn't bring down a release build).
    /// </summary>
    public static void Write(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss.fff} t{Environment.CurrentManagedThreadId,3} {line}";
        WriteInternal(stamped);
    }

    static void WriteInternal(string stamped)
    {
        // Mirror to debugger so existing dev-loop tooling keeps observing the
        // same line stream.
        System.Diagnostics.Debug.WriteLine(stamped);

        var w = _writer;
        if (w is null) return;
        lock (_gate)
        {
            try
            {
                w.WriteLine(stamped);
            }
            catch (Exception ex)
            {
                // Last-resort: drop the file writer so subsequent calls
                // don't keep throwing. Debugger mirror still works.
                System.Diagnostics.Debug.WriteLine($"[SessionLog] write failed, disabling file sink: {ex.Message}");
                try { _writer?.Dispose(); } catch { }
                _writer = null;
            }
        }
    }
}
