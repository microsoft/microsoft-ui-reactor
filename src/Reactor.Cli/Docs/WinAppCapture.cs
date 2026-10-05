using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Captures a doc app's window with the winapp CLI (<c>winapp ui screenshot</c>) and crops
/// it to the client area, so the image matches what the old in-app <c>PrintWindow</c>
/// capture produced: client area only, physical pixels, no window frame.
/// </summary>
/// <remarks>
/// <para>
/// The app is still launched and switched between components by the in-app preview host
/// (<c>--preview</c>, <c>POST /preview</c>); only the pixels come from winapp. The preview
/// host's own frame timer starts only when something reads <c>/frame</c>, and nothing
/// does any more, so the app no longer runs <c>PrintWindow</c> at all.
/// </para>
/// <para>
/// winapp never takes input focus here: <c>--focus</c> and <c>--capture-screen</c> (which
/// foreground the window) are deliberately not passed, so a capture run does not steal the
/// keyboard from whoever is using the desktop.
/// </para>
/// </remarks>
internal static class WinAppCapture
{
    /// <summary>
    /// Absolute-path override for the winapp binary, honored ahead of every other candidate.
    /// Same variable, and the same resolution order, as the E2E harness
    /// (<c>tests/Reactor.AppTests/Infrastructure/WinAppUi.cs</c>).
    /// </summary>
    internal const string WinAppExeEnvVar = "REACTOR_WINAPP_EXE";

    internal const string InstallHint =
        "Install the winapp CLI with `winget install Microsoft.WinAppCli` (or run ./bootstrap.ps1), " +
        "or set " + WinAppExeEnvVar + " to the full path of winapp.exe.";

    /// <summary>The window class every WinUI 3 desktop window registers.</summary>
    internal const string WinUIWindowClass = "WinUIDesktopWin32WindowClass";

    /// <summary>
    /// Resolves winapp.exe: <see cref="WinAppExeEnvVar"/>, then
    /// <c>%LOCALAPPDATA%\Microsoft\WindowsApps\winapp.exe</c> (the alias winget's MSIX install
    /// drops), then the first absolute <c>PATH</c> entry that contains it. Null when none exists.
    /// </summary>
    internal static string? ResolveWinAppExe(Func<string, string?> getEnv, Func<string, bool> fileExists)
    {
        var overridePath = getEnv(WinAppExeEnvVar);
        // A relative path would resolve against the working directory (often a doc topic's
        // folder), so only absolute candidates are ever executed.
        if (!string.IsNullOrEmpty(overridePath) && Path.IsPathFullyQualified(overridePath) && fileExists(overridePath))
            return Path.GetFullPath(overridePath);

        var local = getEnv("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local) && Path.IsPathFullyQualified(local))
        {
            var candidate = Path.Combine(local, "Microsoft", "WindowsApps", "winapp.exe");
            if (fileExists(candidate)) return Path.GetFullPath(candidate);
        }

        var path = getEnv("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                         .Select(e => e.Trim().Trim('"'))
                         .Where(Path.IsPathFullyQualified))
            {
                var candidate = Path.Combine(entry, "winapp.exe");
                if (fileExists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    /// <summary>
    /// The <c>winapp</c> argument list for one capture. <c>-w</c> targets exactly one window
    /// (no composite of owned windows), and neither <c>--focus</c> nor <c>--capture-screen</c>
    /// is passed, so the window is not brought to the foreground.
    /// </summary>
    internal static IReadOnlyList<string> BuildScreenshotArguments(long hwnd, string outputPath) =>
    [
        "ui", "screenshot",
        "-w", hwnd.ToString(global::System.Globalization.CultureInfo.InvariantCulture),
        "-o", outputPath,
        "--json",
    ];

    /// <summary>Reads the <c>width</c>/<c>height</c> winapp reports for the PNG it wrote.</summary>
    /// <exception cref="InvalidOperationException">The output is not the expected JSON.</exception>
    internal static Size ParseScreenshotResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("width", out var w) && w.TryGetInt32(out var width)
                && root.TryGetProperty("height", out var h) && h.TryGetInt32(out var height)
                && width > 0 && height > 0)
            {
                return new Size(width, height);
            }
        }
        catch (JsonException)
        {
        }
        throw new InvalidOperationException(
            $"winapp ui screenshot did not report the image size: {Truncate(json)}");
    }

    /// <summary>
    /// Where the client area sits inside the image winapp captured.
    /// </summary>
    /// <remarks>
    /// winapp captures the window's DWM frame bounds (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>):
    /// the visible frame including the title bar, without the invisible resize borders.
    /// The old in-app capture took the client area only, so the crop is the client rect
    /// expressed relative to those bounds. All four inputs are physical pixels. A
    /// mismatch between the image and the frame bounds means the two were not taken of
    /// the same window state (a resize, a DPI change mid-capture), and cropping anyway
    /// would cut the wrong region, so that is an error rather than a guess.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The geometry does not line up.</exception>
    internal static Rectangle ComputeClientCrop(Rectangle frameBounds, Point clientOrigin, Size clientSize, Size imageSize)
    {
        if (imageSize != frameBounds.Size)
            throw new InvalidOperationException(
                $"captured image is {imageSize.Width}x{imageSize.Height} but the window frame is " +
                $"{frameBounds.Width}x{frameBounds.Height}; the window changed during capture");

        var crop = new Rectangle(
            clientOrigin.X - frameBounds.X,
            clientOrigin.Y - frameBounds.Y,
            clientSize.Width,
            clientSize.Height);

        if (crop.Width <= 0 || crop.Height <= 0 || !new Rectangle(Point.Empty, imageSize).Contains(crop))
            throw new InvalidOperationException(
                $"client area {crop} does not fit inside the {imageSize.Width}x{imageSize.Height} capture");
        return crop;
    }

    /// <summary>
    /// Crops <paramref name="png"/> to <paramref name="region"/>, squares the window's rounded
    /// corners (<see cref="SquareRoundedCorners"/>) and re-encodes it as PNG.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The decoded image is not <paramref name="expectedSize"/> (the size the crop was computed
    /// against), so the crop would not describe these pixels.
    /// </exception>
    internal static byte[] CropPng(byte[] png, Rectangle region, Size expectedSize)
    {
        using var input = new MemoryStream(png);
        using var source = new Bitmap(input);
        if (source.Size != expectedSize)
            throw new InvalidOperationException(
                $"winapp reported a {expectedSize.Width}x{expectedSize.Height} capture but wrote a " +
                $"{source.Width}x{source.Height} image");
        using var cropped = source.Clone(region, PixelFormat.Format32bppArgb);
        SquareRoundedCorners(cropped);
        using var output = new MemoryStream();
        cropped.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>
    /// Replaces every pixel that is not fully opaque with the nearest opaque pixel in the same
    /// row, searching toward the middle of the row. Returns how many pixels it replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// winapp captures what DWM composes, and on Windows 11 that includes the window's rounded
    /// corners. The bottom corners fall inside the client area, so the crop carries a few dozen
    /// semi-transparent pixels (the corner arc and its anti-aliasing) that the old in-app
    /// <c>PrintWindow</c> capture never had. <c>ImageProcessor</c> reads them as content, so
    /// content-crop would keep the whole frame and every screenshot would come out
    /// full-window.
    /// </para>
    /// <para>
    /// A WinUI client area is otherwise fully opaque, so "alpha below 255" picks out exactly
    /// those pixels, and filling them from the nearest opaque pixel inward reproduces the
    /// square corner <c>PrintWindow</c> rendered: the window background, in practice. A row
    /// with no opaque pixel is left alone.
    /// </para>
    /// </remarks>
    internal static int SquareRoundedCorners(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var replaced = 0;
        try
        {
            var row = new int[bmp.Width];
            for (var y = 0; y < bmp.Height; y++)
            {
                var rowPtr = data.Scan0 + (y * data.Stride);
                Marshal.Copy(rowPtr, row, 0, row.Length);
                var changed = false;
                var mid = row.Length / 2;
                for (var x = 0; x < row.Length; x++)
                {
                    if ((uint)row[x] >> 24 == 0xFF) continue;
                    var step = x < mid ? 1 : -1;
                    for (var s = x + step; s >= 0 && s < row.Length; s += step)
                    {
                        if ((uint)row[s] >> 24 != 0xFF) continue;
                        row[x] = row[s];
                        replaced++;
                        changed = true;
                        break;
                    }
                }
                if (changed) Marshal.Copy(row, 0, rowPtr, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return replaced;
    }

    /// <summary>
    /// Calls <paramref name="capture"/> until it returns a frame with visible content, or the
    /// deadline expires. Same contract the HTTP <c>/frame</c> poller had (issue #989): a cold
    /// window's first frame is often blank, so with <paramref name="requireContent"/> a blank
    /// frame means "not ready yet". If only blank frames arrived, the last one is returned so
    /// the caller reports <see cref="BlankFrameException"/>; if no frame arrived at all, the
    /// result is empty and the caller reports "no frame produced".
    /// </summary>
    /// <remarks>
    /// The deadline bounds each attempt, not just the gaps between them: every call gets a
    /// token cancelled by the time left, and a cancellation the loop asked for ends it.
    /// </remarks>
    internal static async Task<byte[]> CaptureUntilContent(
        Func<CancellationToken, Task<byte[]>> capture,
        TimeSpan deadline,
        bool requireContent = true,
        TimeSpan? interval = null)
    {
        var pause = interval ?? TimeSpan.FromMilliseconds(100);
        var sw = Stopwatch.StartNew();
        var lastBytes = Array.Empty<byte>();
        while (true)
        {
            var remaining = deadline - sw.Elapsed;
            if (remaining <= TimeSpan.Zero) break;

            using var cts = new CancellationTokenSource(remaining);
            try
            {
                var bytes = await capture(cts.Token);
                if (bytes.Length > 0)
                {
                    if (!requireContent) return bytes;
                    lastBytes = bytes;
                    if (ImageProcessor.FrameHasContent(bytes)) return bytes;
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }

            var left = deadline - sw.Elapsed;
            if (left <= TimeSpan.Zero) break;
            await Task.Delay(left < pause ? left : pause);
        }
        return lastBytes;
    }

    /// <summary>
    /// Captures <paramref name="hwnd"/> with winapp and returns the client area as PNG bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">winapp failed, or the geometry did not line up.</exception>
    internal static async Task<byte[]> CaptureClientAreaAsync(string winAppExe, IntPtr hwnd, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"reactor-docs-capture-{Guid.NewGuid():N}.png");
        try
        {
            var psi = new ProcessStartInfo(winAppExe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in BuildScreenshotArguments(hwnd.ToInt64(), temp)) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"could not start {winAppExe}");
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"winapp ui screenshot exited {proc.ExitCode}: {Truncate(stderr.Length > 0 ? stderr : stdout)}");

            var reported = ParseScreenshotResult(stdout);
            var png = await File.ReadAllBytesAsync(temp, ct);

            var geometry = Native.GetClientGeometry(hwnd);
            var crop = ComputeClientCrop(geometry.FrameBounds, geometry.ClientOrigin, geometry.ClientSize, reported);
            return CropPng(png, crop, reported);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Waits for the doc app's WinUI window to appear. The app is a descendant of the
    /// <c>dotnet run</c> process <paramref name="rootPid"/>, so the window is matched by
    /// owning process rather than by title, which the preview host changes when it switches
    /// components. <see cref="IntPtr.Zero"/> on timeout.
    /// </summary>
    internal static async Task<IntPtr> WaitForAppWindowAsync(int rootPid, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var hwnd = Native.FindWinUIWindow(Native.GetDescendantProcessIds(rootPid));
            if (hwnd != IntPtr.Zero) return hwnd;
            await Task.Delay(200);
        }
        return IntPtr.Zero;
    }

    private static string Truncate(string s)
    {
        s = s.Trim();
        return s.Length <= 400 ? s : s[..400] + "…";
    }

    internal readonly record struct ClientGeometry(Rectangle FrameBounds, Point ClientOrigin, Size ClientSize);

    private static class Native
    {
        private static readonly IntPtr PerMonitorAwareV2 = new(-4);
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const uint GW_OWNER = 4;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        /// <summary>
        /// Frame bounds and client rect in physical pixels. The thread switches to
        /// per-monitor-v2 awareness for the calls, because <c>mur</c> itself is DPI-unaware
        /// and would otherwise get coordinates scaled for a 150% window.
        /// </summary>
        public static ClientGeometry GetClientGeometry(IntPtr hwnd)
        {
            var previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
            try
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) != 0)
                    throw new InvalidOperationException("DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS) failed");
                if (!GetClientRect(hwnd, out var client))
                    throw new InvalidOperationException("GetClientRect failed");
                var origin = new POINT();
                if (!ClientToScreen(hwnd, ref origin))
                    throw new InvalidOperationException("ClientToScreen failed");

                return new ClientGeometry(
                    Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom),
                    new Point(origin.X, origin.Y),
                    new Size(client.Right - client.Left, client.Bottom - client.Top));
            }
            finally
            {
                if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
            }
        }

        public static HashSet<int> GetDescendantProcessIds(int rootPid)
        {
            var parentOf = new Dictionary<int, int>();
            var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == new IntPtr(-1)) return [rootPid];
            try
            {
                var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
                if (Process32FirstW(snapshot, ref entry))
                {
                    do { parentOf[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID; }
                    while (Process32NextW(snapshot, ref entry));
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }

            var result = new HashSet<int> { rootPid };
            bool grew;
            do
            {
                grew = false;
                foreach (var (pid, parent) in parentOf)
                    if (pid != parent && result.Contains(parent) && result.Add(pid)) grew = true;
            }
            while (grew);
            return result;
        }

        public static IntPtr FindWinUIWindow(HashSet<int> pids)
        {
            var found = IntPtr.Zero;
            var className = new char[256];
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (!pids.Contains((int)pid) || !IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
                    return true;
                var len = GetClassNameW(hwnd, className, className.Length);
                if (len > 0 && new string(className, 0, len) == WinUIWindowClass)
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, char[] className, int maxCount);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
