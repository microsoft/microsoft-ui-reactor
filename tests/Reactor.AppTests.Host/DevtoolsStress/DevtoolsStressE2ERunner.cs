using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Microsoft.UI.Reactor.AppTests.Host.DevtoolsStress;

/// <summary>
/// End-to-end stress runner: the parent process repeatedly spawns a fresh
/// child via <c>--stress-child --devtools run</c>, parses the child's
/// stdout for the <c>devtools-ready</c> JSON sentinel to learn the
/// child-allocated MCP port, runs a lightweight MCP validation
/// (<c>initialize</c> + <c>tools/list</c> + <c>tools/call version</c>),
/// then terminates the child and repeats. Unlike the in-process runner,
/// this exercises the full process-lifetime paths — WinUI init, devtools
/// CLI dispatch, render-time <c>AnnounceReady</c>, window close → MCP
/// dispose — which is where the customer crash in CoreMessagingXP.dll
/// most likely lives.
/// </summary>
internal static class DevtoolsStressE2ERunner
{
    private sealed class Options
    {
        public int Iterations = 100;
        public int ReadyTimeoutMs = 10_000;
        public int GraceMs = 500;
        public bool HardKill;
        public bool NoDump;
        public string? LogPath;
    }

    public static void Run(string[] args)
    {
        var opts = ParseArgs(args);
        opts.LogPath ??= global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            $"reactor-devtools-stress-e2e-{Environment.ProcessId}.log");

        var ownExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("ProcessPath unavailable");

        using var log = new E2ELog(opts.LogPath);
        log.Meta("e2e-start", new()
        {
            ["parentPid"] = Environment.ProcessId.ToString(),
            ["iterations"] = opts.Iterations.ToString(),
            ["readyTimeoutMs"] = opts.ReadyTimeoutMs.ToString(),
            ["graceMs"] = opts.GraceMs.ToString(),
            ["hardKill"] = opts.HardKill.ToString(),
            ["dump"] = (!opts.NoDump).ToString(),
            ["exe"] = ownExe,
        });
        Console.Error.WriteLine($"[stress-e2e] log: {opts.LogPath}");
        Console.Error.WriteLine($"[stress-e2e] exe: {ownExe}");
        Console.Error.WriteLine($"[stress-e2e] iterations={opts.Iterations} ready-timeout={opts.ReadyTimeoutMs}ms grace={opts.GraceMs}ms hardKill={opts.HardKill}");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var totalSw = Stopwatch.StartNew();
        int suspicious = 0;

        for (int i = 1; i <= opts.Iterations; i++)
        {
            try
            {
                bool flagged = RunIterationAsync(i, ownExe, opts, http, log).GetAwaiter().GetResult();
                if (flagged) suspicious++;
            }
            catch (Exception ex)
            {
                log.Iter(i, -1, $"harness-fail:{ex.GetType().Name}:{ex.Message}", 0);
            }
            Console.Error.WriteLine($"[stress-e2e] i={i}/{opts.Iterations} elapsed={totalSw.Elapsed.TotalSeconds:F1}s suspicious={suspicious}");
        }

        log.Meta("e2e-end", new()
        {
            ["iterations"] = opts.Iterations.ToString(),
            ["elapsedMs"] = totalSw.ElapsedMilliseconds.ToString(),
            ["suspicious"] = suspicious.ToString(),
        });
        Console.Error.WriteLine($"[stress-e2e] DONE {opts.Iterations} iterations in {totalSw.Elapsed.TotalSeconds:F1}s  suspicious-exits={suspicious}");
        Environment.Exit(suspicious > 0 ? 2 : 0);
    }

    // -- One iteration ------------------------------------------------------

    private static async Task<bool> RunIterationAsync(int i, string exe, Options opts, HttpClient http, E2ELog log)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false, // let the real WinUI window show — matches customer scenario
        };
        psi.ArgumentList.Add("--stress-child");
        psi.ArgumentList.Add("--devtools");
        psi.ArgumentList.Add("run");

        if (!opts.NoDump)
        {
            var dumpDir = global::System.IO.Path.Combine(
                global::System.IO.Path.GetTempPath(), "reactor-stress-dumps");
            Directory.CreateDirectory(dumpDir);
            // %p expands to the child pid — one dump file per crashing child.
            psi.Environment["COMPlus_DbgEnableMiniDump"] = "1";
            psi.Environment["COMPlus_DbgMiniDumpType"] = "2"; // full heap
            psi.Environment["COMPlus_DbgMiniDumpName"] = global::System.IO.Path.Combine(dumpDir, "reactor-stress-%p.dmp");
            psi.Environment["DOTNET_DbgEnableMiniDump"] = "1";
            psi.Environment["DOTNET_DbgMiniDumpType"] = "2";
            psi.Environment["DOTNET_DbgMiniDumpName"] = global::System.IO.Path.Combine(dumpDir, "reactor-stress-%p.dmp");
        }

        Process child;
        try { child = Process.Start(psi)!; }
        catch (Exception ex)
        {
            log.Iter(i, -1, $"spawn-fail:{ex.GetType().Name}:{ex.Message}", 0);
            return true;
        }

        int pid = child.Id;
        var iterSw = Stopwatch.StartNew();
        log.Iter(i, pid, "spawned", 0);

        // Stdout/stderr tails — keep the last N lines in case the child crashes.
        var stdoutTail = new TailBuffer(capacity: 200);
        var stderrTail = new TailBuffer(capacity: 200);
        var readyTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // stdout reader: mirror into tail + sniff for the ready JSON sentinel
        var stdoutTask = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await child.StandardOutput.ReadLineAsync()) is not null)
                {
                    stdoutTail.Add(line);
                    if (!readyTcs.Task.IsCompleted &&
                        line.StartsWith("{\"event\":\"devtools-ready\"", StringComparison.Ordinal))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var port = doc.RootElement.GetProperty("port").GetInt32();
                            readyTcs.TrySetResult(port);
                        }
                        catch (Exception ex) { readyTcs.TrySetException(ex); }
                    }
                }
            }
            catch { /* pipe closed on child exit */ }
        });
        var stderrTask = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await child.StandardError.ReadLineAsync()) is not null)
                    stderrTail.Add(line);
            }
            catch { }
        });

        // -- Wait for ready ------------------------------------------------
        int port;
        using (var readyCts = new CancellationTokenSource(opts.ReadyTimeoutMs))
        {
            // Abort the wait if the child exits before emitting the sentinel.
            var childExited = child.WaitForExitAsync(readyCts.Token);
            var winner = await Task.WhenAny(readyTcs.Task, childExited, Task.Delay(opts.ReadyTimeoutMs, readyCts.Token));
            if (winner == readyTcs.Task)
            {
                port = await readyTcs.Task;
                log.Iter(i, pid, $"ready:{port}", iterSw.ElapsedMilliseconds);
            }
            else if (child.HasExited)
            {
                log.Iter(i, pid, $"child-died-pre-ready:exit=0x{UnsignedHex(child.ExitCode)}", iterSw.ElapsedMilliseconds);
                await DrainAsync(stdoutTask, stderrTask);
                LogTails(log, i, pid, stdoutTail, stderrTail);
                return IsSuspicious(child.ExitCode);
            }
            else
            {
                log.Iter(i, pid, "ready-timeout", iterSw.ElapsedMilliseconds);
                KillSafe(child);
                await child.WaitForExitAsync();
                await DrainAsync(stdoutTask, stderrTask);
                LogTails(log, i, pid, stdoutTail, stderrTail);
                return true;
            }
        }

        // -- MCP validation ------------------------------------------------
        bool mcpOk = true;
        try
        {
            var init = await PostJsonRpcAsync(http, port, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "reactor-stress-e2e", version = "1.0" },
                },
            });
            if (!init.TryGetProperty("result", out _))
                throw new Exception("initialize returned no result");
            log.Iter(i, pid, "initialize:ok", iterSw.ElapsedMilliseconds);

            var list = await PostJsonRpcAsync(http, port, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
            });
            int toolCount = list.GetProperty("result").GetProperty("tools").GetArrayLength();
            log.Iter(i, pid, $"tools/list:{toolCount}", iterSw.ElapsedMilliseconds);

            var ver = await PostJsonRpcAsync(http, port, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { name = "version", arguments = new { } },
            });
            if (!ver.TryGetProperty("result", out _))
                throw new Exception("tools/call version returned no result");
            log.Iter(i, pid, "version:ok", iterSw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            mcpOk = false;
            log.Iter(i, pid, $"mcp-fail:{ex.GetType().Name}:{ex.Message}", iterSw.ElapsedMilliseconds);
        }

        // -- Terminate -----------------------------------------------------
        string termPhase;
        if (opts.HardKill)
        {
            KillSafe(child);
            termPhase = "hard-killed";
        }
        else
        {
            bool closed = false;
            try { closed = child.CloseMainWindow(); } catch { }

            var exit = child.WaitForExitAsync();
            var winner = await Task.WhenAny(exit, Task.Delay(opts.GraceMs));
            if (winner != exit)
            {
                KillSafe(child);
                termPhase = closed ? "kill-after-grace" : "kill-no-close-handle";
            }
            else termPhase = "graceful";
        }

        await child.WaitForExitAsync();
        await DrainAsync(stdoutTask, stderrTask);

        int exitCode = child.ExitCode;
        bool suspicious = IsSuspicious(exitCode);
        log.Iter(i, pid, $"exit:0x{UnsignedHex(exitCode)} term={termPhase}{(suspicious ? " SUSPICIOUS" : "")}", iterSw.ElapsedMilliseconds);

        if (suspicious || !mcpOk)
            LogTails(log, i, pid, stdoutTail, stderrTail);

        return suspicious;
    }

    // -- Helpers ------------------------------------------------------------

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Devtools stress E2E runner; serializes an anonymous JSON-RPC envelope. Devtools fixtures are AOT-skip-listed (SelfTestRunner.DefaultAotSkipPatterns).")]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Devtools stress E2E runner; AOT-skip-listed (see IL2026 note).")]
    private static async Task<JsonElement> PostJsonRpcAsync(HttpClient http, int port, object envelope)
    {
        var json = JsonSerializer.Serialize(envelope);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static void KillSafe(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }

    private static async Task DrainAsync(params Task[] tasks)
    {
        // Reader tasks complete when their pipes hit EOF (child exit). Bound
        // the wait so a stuck pipe doesn't hang the harness.
        await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(2000));
    }

    /// <summary>
    /// A crash-like exit code from the Windows/CLR surface: NT status codes
    /// (0xCxxxxxxx), or the SEH/CLR rethrow tag (0xE0434352). Graceful exits
    /// are 0; <c>Process.Kill</c> produces -1 (0xFFFFFFFF) which is NOT
    /// flagged here — operator-initiated.
    /// </summary>
    private static bool IsSuspicious(int exitCode)
    {
        uint u = unchecked((uint)exitCode);
        if (u == 0) return false;
        if (u == 0xFFFFFFFF) return false; // our own Kill
        if (u == 0xE0434352) return true;  // CLR managed exception
        if (u >= 0xC0000000 && u <= 0xCFFFFFFF) return true; // NT status range
        return false;
    }

    private static string UnsignedHex(int exit) => unchecked((uint)exit).ToString("X8");

    private static void LogTails(E2ELog log, int i, int pid, TailBuffer stdoutTail, TailBuffer stderrTail)
    {
        foreach (var line in stdoutTail.Snapshot())
            log.Tail(i, pid, "stdout", line);
        foreach (var line in stderrTail.Snapshot())
            log.Tail(i, pid, "stderr", line);
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--iterations" when i + 1 < args.Length && int.TryParse(args[++i], out var n): o.Iterations = n; break;
                case "--ready-timeout" when i + 1 < args.Length && int.TryParse(args[++i], out var t): o.ReadyTimeoutMs = t; break;
                case "--grace-ms" when i + 1 < args.Length && int.TryParse(args[++i], out var g): o.GraceMs = g; break;
                case "--hard-kill": o.HardKill = true; break;
                case "--no-dump": o.NoDump = true; break;
                case "--log" when i + 1 < args.Length: o.LogPath = args[++i]; break;
            }
        }
        return o;
    }

    // -- Ring-buffer tail ---------------------------------------------------

    private sealed class TailBuffer
    {
        private readonly int _capacity;
        private readonly Queue<string> _queue;
        private readonly object _lock = new();
        public TailBuffer(int capacity) { _capacity = capacity; _queue = new Queue<string>(capacity); }

        public void Add(string s)
        {
            lock (_lock)
            {
                if (_queue.Count == _capacity) _queue.Dequeue();
                _queue.Enqueue(s);
            }
        }

        public string[] Snapshot()
        {
            lock (_lock) return _queue.ToArray();
        }
    }

    // -- Flush-per-write log -----------------------------------------------

    private sealed class E2ELog : IDisposable
    {
        private readonly FileStream _fs;
        private readonly object _lock = new();

        public E2ELog(string path)
        {
            Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(path)!);
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        }

        public void Iter(int i, int pid, string phase, long dtMs)
            => Write($"{DateTime.UtcNow:O}\titer={i}\tpid={pid}\tphase={phase}\tdt={dtMs}ms\n");

        public void Tail(int i, int pid, string stream, string line)
            => Write($"{DateTime.UtcNow:O}\titer={i}\tpid={pid}\t{stream}\t{line}\n");

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

        public void Dispose() { try { _fs.Dispose(); } catch { } }
    }
}
