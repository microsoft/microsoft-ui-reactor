using System.Diagnostics;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Captures screenshots from a running Reactor doc app via the PreviewCaptureServer HTTP API.
/// Launches the app with <c>--preview --vscode</c> to enable the capture endpoint,
/// waits for the startup delay, then captures frames via <c>GET /frame</c>.
/// </summary>
internal static class ScreenshotCapture
{
    public static async Task<bool> CaptureAsync(
        string appDir,
        string topicId,
        DocManifest manifest,
        string outputImagesDir)
    {
        var csprojFiles = Directory.GetFiles(appDir, "*.csproj");
        if (csprojFiles.Length == 0)
        {
            Console.Error.WriteLine($"    ✗ No .csproj found in {appDir}");
            return false;
        }

        var csproj = csprojFiles[0];
        Console.WriteLine($"    Launching {Path.GetFileName(csproj)} for capture...");

        // WindowsAppSDK self-contained run requires an explicit architecture;
        // match the host so dotnet run picks up the matching build output.
        var platform = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
            _ => "x64",
        };

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{csproj}\" -p:Platform={platform} -- --preview --vscode --fps 5",
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            Console.Error.WriteLine("    ✗ Failed to start process");
            return false;
        }

        try
        {
            var (port, token) = await WaitForCaptureHandshake(process, TimeSpan.FromSeconds(30));
            if (port < 0 || token is null)
            {
                Console.Error.WriteLine("    ✗ Timed out waiting for capture port");
                return false;
            }

            Console.WriteLine($"    Capture server on port {port}");

            var delay = manifest.App.StartupDelay;
            Console.WriteLine($"    Waiting {delay}ms for app startup...");
            await Task.Delay(delay);

            var topicDir = Path.Combine(outputImagesDir, topicId);
            Directory.CreateDirectory(topicDir);

            using var http = new HttpClient();
            // SECURITY (TASK-018): the capture server requires a per-launch
            // bearer token on every request. We read it from the app's stdout
            // alongside CAPTURE_PORT.
            http.DefaultRequestHeaders.Authorization =
                new global::System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Warm-up: the capture server starts its capture timer lazily on the
            // first /frame call. Kick it now and wait for the first frame so the
            // first manifest entry doesn't pay the timer-startup latency.
            await PollForFrame(http, port, TimeSpan.FromSeconds(10));

            foreach (var screenshot in manifest.Screenshots)
            {
                Console.Write($"    Capturing {screenshot.Id}...");
                try
                {
                    // Switch to the target component if specified
                    if (!string.IsNullOrEmpty(screenshot.Component))
                    {
                        var json = $"{{\"component\":\"{screenshot.Component}\"}}";
                        var content = new StringContent(json, global::System.Text.Encoding.UTF8, "application/json");
                        var switchResp = await http.PostAsync($"http://localhost:{port}/preview", content);
                        if (!switchResp.IsSuccessStatusCode)
                        {
                            Console.Error.WriteLine($" ✗ Failed to switch to component '{screenshot.Component}' ({switchResp.StatusCode})");
                            continue;
                        }
                        // Wait for the component to render and a new frame to be captured
                        // At 5 fps, frames arrive every 200ms; wait long enough for
                        // the switch + layout + at least one fresh capture cycle.
                        await Task.Delay(1000);
                    }

                    // The capture timer only starts once a reader hits /frame
                    // (TASK-025), so the first call returns 204 with no body.
                    // Poll until a frame is ready or we exceed the deadline.
                    var frameBytes = await PollForFrame(http, port, TimeSpan.FromSeconds(5));
                    if (frameBytes.Length == 0)
                    {
                        Console.Error.WriteLine($" ✗ no frame produced within deadline");
                        continue;
                    }
                    var outputPath = Path.GetFullPath(Path.Combine(topicDir, $"{screenshot.Id}.{screenshot.Format}"));
                    if (!outputPath.StartsWith(Path.GetFullPath(topicDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Screenshot id '{screenshot.Id}' would escape output directory");
                    // Auto-crop whitespace, add border + drop shadow, convert to PNG
                    var processed = ImageProcessor.Process(frameBytes);
                    File.WriteAllBytes(outputPath, processed);
                    Console.WriteLine(" ✓");
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
                {
                    Console.Error.WriteLine($" ✗ {ex}");
                }
            }

            return true;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Polls <c>/frame</c> until the server returns a non-empty body or the
    /// deadline expires. The capture timer starts lazily on first reader, so
    /// early calls return HTTP 204 with no content.
    /// </summary>
    private static async Task<byte[]> PollForFrame(HttpClient http, int port, TimeSpan deadline)
    {
        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < deadline)
        {
            using var resp = await http.GetAsync($"http://localhost:{port}/frame");
            if (resp.StatusCode == global::System.Net.HttpStatusCode.OK)
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                if (bytes.Length > 0) return bytes;
            }
            await Task.Delay(100);
        }
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Reads the app's stdout for the <c>CAPTURE_PORT=</c> and <c>CAPTURE_TOKEN=</c>
    /// handshake lines emitted by <see cref="Reactor.Hosting.PreviewCaptureServer.Start"/>.
    /// Both must arrive within <paramref name="timeout"/> for the capture client to
    /// authenticate. Returns <c>(-1, null)</c> on timeout.
    /// </summary>
    private static async Task<(int Port, string? Token)> WaitForCaptureHandshake(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        int port = -1;
        string? token = null;
        try
        {
            while (!cts.Token.IsCancellationRequested && (port < 0 || token is null))
            {
                var line = await process.StandardOutput.ReadLineAsync(cts.Token);
                if (line == null) break;

                if (port < 0 && line.StartsWith("CAPTURE_PORT=") &&
                    int.TryParse(line.AsSpan("CAPTURE_PORT=".Length), out var parsed))
                {
                    port = parsed;
                }
                else if (token is null && line.StartsWith("CAPTURE_TOKEN="))
                {
                    token = line.Substring("CAPTURE_TOKEN=".Length);
                }
            }
        }
        catch (OperationCanceledException) { }

        if (port >= 0 && token is not null)
        {
            // Drain stdout in background to prevent buffer deadlock
            _ = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync() != null) { }
            });
            return (port, token);
        }
        return (-1, null);
    }
}
