using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.DevtoolsStress;

/// <summary>
/// Reproduction harness for a customer-reported crash in
/// <c>CoreMessagingXP.dll</c> (0xc000027b — stowed WinRT exception) that
/// surfaces while using the Reactor devtools surface. The theory under test
/// is that the devtools HTTP listener interacts badly with the WinUI STA
/// dispatcher over repeated start/stop cycles. This runner spins up a real
/// WinUI window, then cycles <see cref="DevtoolsMcpServer"/> Start/Dispose
/// many times in-process and logs each iteration to disk so that on a native
/// crash we can see exactly where the process died.
/// </summary>
internal static class DevtoolsStressRunner
{
    private sealed class Options
    {
        public int Iterations = 5000;
        public int DelayMs = 0;
        public bool Background;     // cycle on a worker thread instead of the UI dispatcher
        public bool Ping;           // fire an HTTP GET /mcp between Start and Dispose
        public bool PinPort;        // reuse the same port each iteration (stresses TIME_WAIT reuse)
        public string? LogPath;     // flush-per-iteration log; inspect after crash
    }

    public static void Run(string[] args)
    {
        var opts = ParseArgs(args);
        opts.LogPath ??= global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            $"reactor-devtools-stress-{Environment.ProcessId}.log");

        using var log = new IterLog(opts.LogPath);
        log.Meta("stress-start", new()
        {
            ["pid"] = Environment.ProcessId.ToString(),
            ["iterations"] = opts.Iterations.ToString(),
            ["delayMs"] = opts.DelayMs.ToString(),
            ["mode"] = opts.Background ? "bg" : "ui",
            ["ping"] = opts.Ping.ToString(),
            ["pinPort"] = opts.PinPort.ToString(),
        });
        Console.Error.WriteLine($"[stress] log: {opts.LogPath}");
        Console.Error.WriteLine($"[stress] iterations={opts.Iterations} delayMs={opts.DelayMs} mode={(opts.Background ? "bg" : "ui")} ping={opts.Ping} pinPort={opts.PinPort}");

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new ReactorApplication();
            var dispatcher = DispatcherQueue.GetForCurrentThread();

            var window = new Window { Title = "Devtools Stress — starting…" };
            window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(520, 220));
            var statusText = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "initializing…",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 14,
                Margin = new Microsoft.UI.Xaml.Thickness(16),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            };
            window.Content = statusText;
            window.Activate();

            // Suppress the per-iteration banner lines that DevtoolsMcpServer.Start
            // writes to stdout — otherwise 10k iterations spam 40k lines and the
            // cycling is hard to see. We still keep stderr (our own progress).
            Console.SetOut(TextWriter.Null);

            // Pick a stable port up front if pinning — the server probes a fresh
            // loopback port each construction, so without pinning ports differ
            // every cycle.
            int? pinnedPort = opts.PinPort ? LoopbackHttpListener.ProbeFreePort() : null;

            if (opts.Background)
            {
                _ = Task.Run(() => RunLoop(dispatcher, window, opts, pinnedPort, log, statusText));
            }
            else
            {
                dispatcher.TryEnqueue(() =>
                {
                    _ = RunLoopAsync(dispatcher, window, opts, pinnedPort, log, statusText);
                });
            }
        });
    }

    // -- Loop bodies --------------------------------------------------------

    private static void RunLoop(DispatcherQueue dispatcher, Window window, Options opts, int? pinnedPort, IterLog log, Microsoft.UI.Xaml.Controls.TextBlock statusText)
    {
        RunLoopAsync(dispatcher, window, opts, pinnedPort, log, statusText).GetAwaiter().GetResult();
    }

    private static async Task RunLoopAsync(DispatcherQueue dispatcher, Window window, Options opts, int? pinnedPort, IterLog log, Microsoft.UI.Xaml.Controls.TextBlock statusText)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var sw = Stopwatch.StartNew();

        for (int i = 1; i <= opts.Iterations; i++)
        {
            var iterSw = Stopwatch.StartNew();
            DevtoolsMcpServer? server = null;
            try
            {
                // Construct on whatever thread we're on — the ctor only captures
                // the dispatcher/window handles, it does not touch them.
                server = new DevtoolsMcpServer(
                    dispatcher,
                    window,
                    preferredPort: pinnedPort,
                    logger: null,
                    transport: McpTransport.Http);

                server.Start();
                log.Iter(i, "started", server.Port, iterSw.ElapsedMilliseconds);

                if (opts.Ping)
                {
                    // GET /mcp is the self-describing schema endpoint — cheap and
                    // exercises the listener's accept loop without registering
                    // any tools.
                    try
                    {
                        var resp = await http.GetAsync($"http://127.0.0.1:{server.Port}/mcp");
                        log.Iter(i, $"ping:{(int)resp.StatusCode}", server.Port, iterSw.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        log.Iter(i, $"ping-fail:{ex.GetType().Name}", server.Port, iterSw.ElapsedMilliseconds);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Iter(i, $"start-fail:{ex.GetType().Name}:{ex.Message}", pinnedPort ?? -1, iterSw.ElapsedMilliseconds);
            }
            finally
            {
                try { server?.Dispose(); }
                catch (Exception ex) { log.Iter(i, $"dispose-fail:{ex.GetType().Name}:{ex.Message}", server?.Port ?? -1, iterSw.ElapsedMilliseconds); }
            }

            log.Iter(i, "disposed", server?.Port ?? -1, iterSw.ElapsedMilliseconds);

            // Visible feedback — update window title + status body every cycle so
            // the user can see the loop ticking. Marshal to the UI thread if we're
            // running in --bg mode; no-op if we're already on it.
            int iCap = i;
            int portCap = server?.Port ?? -1;
            long dtCap = iterSw.ElapsedMilliseconds;
            dispatcher.TryEnqueue(() =>
            {
                window.Title = $"Devtools Stress — {iCap}/{opts.Iterations}  port {portCap}  ({dtCap} ms)";
                statusText.Text =
                    $"iteration: {iCap} / {opts.Iterations}\n" +
                    $"last port: {portCap}\n" +
                    $"cycle dt:  {dtCap} ms\n" +
                    $"elapsed:   {sw.Elapsed.TotalSeconds:F1} s\n" +
                    $"mode:      {(opts.Background ? "bg" : "ui")}  ping={opts.Ping}  pinPort={opts.PinPort}";
            });

            // Console line every iteration so `tail -f` shows the cycle — tiny
            // overhead, and makes "is it actually cycling?" obvious.
            if (opts.Iterations <= 500 || (i % 25) == 0 || i == opts.Iterations)
                Console.Error.WriteLine($"[stress] i={iCap}/{opts.Iterations} port={portCap} dt={dtCap}ms elapsed={sw.Elapsed.TotalSeconds:F1}s");

            if (opts.DelayMs > 0)
                await Task.Delay(opts.DelayMs);
        }

        log.Meta("stress-end", new()
        {
            ["iterations"] = opts.Iterations.ToString(),
            ["elapsedMs"] = sw.ElapsedMilliseconds.ToString(),
        });
        Console.Error.WriteLine($"[stress] DONE {opts.Iterations} iterations in {sw.Elapsed.TotalSeconds:F1}s");
        Environment.Exit(0);
    }

    // -- Helpers ------------------------------------------------------------

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--iterations" when i + 1 < args.Length && int.TryParse(args[++i], out var n): o.Iterations = n; break;
                case "--delay" when i + 1 < args.Length && int.TryParse(args[++i], out var d): o.DelayMs = d; break;
                case "--log" when i + 1 < args.Length: o.LogPath = args[++i]; break;
                case "--bg": o.Background = true; break;
                case "--ping": o.Ping = true; break;
                case "--pin-port": o.PinPort = true; break;
            }
        }
        return o;
    }

    /// <summary>
    /// Append-only per-iteration log that flushes every write. Native crashes
    /// skip managed finalizers, so we can't rely on buffered writers — the
    /// last persisted line is our post-mortem breadcrumb for when the process
    /// goes down without a managed stack trace.
    /// </summary>
    private sealed class IterLog : IDisposable
    {
        private readonly FileStream _fs;
        private readonly Lock _lock = new();

        public IterLog(string path)
        {
            Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(path)!);
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        }

        public void Iter(int i, string phase, int port, long elapsedMs)
        {
            var line = $"{DateTime.UtcNow:O}\titer={i}\tphase={phase}\tport={port}\tdt={elapsedMs}ms\n";
            Write(line);
        }

        public void Meta(string kind, Dictionary<string, string> fields)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.UtcNow.ToString("O")).Append('\t').Append(kind);
            foreach (var kv in fields) sb.Append('\t').Append(kv.Key).Append('=').Append(kv.Value);
            sb.Append('\n');
            Write(sb.ToString());
        }

        private void Write(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            lock (_lock)
            {
                _fs.Write(bytes, 0, bytes.Length);
                _fs.Flush(flushToDisk: true);
            }
        }

        public void Dispose()
        {
            try { _fs.Dispose(); } catch { }
        }
    }
}
