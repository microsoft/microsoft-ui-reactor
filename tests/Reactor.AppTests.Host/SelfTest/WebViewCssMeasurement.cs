using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest;

/// <summary>
/// Hosts a WebView2 in the selftest window so a block of HTML + CSS can be
/// rendered, laid out, and measured alongside a native WinUI panel. We use it
/// to validate FlexPanel's implementation of CSS Flexbox by comparing
/// <c>getBoundingClientRect()</c> results from a real browser with ActualWidth/
/// ActualHeight from the WinUI visual tree, in the same fixture, same frame.
///
/// Usage (WebView2 requires being parented to the visual tree BEFORE
/// EnsureCoreWebView2Async succeeds — so we split creation from loading):
///   1. <see cref="Create"/> to make an un-initialized WebView2 sized for the
///      scenario.
///   2. Mount it into your tree / SetContent.
///   3. <see cref="LoadAsync"/> to initialize it, navigate to the HTML, and
///      wait for load + a short settle frame.
///   4. <see cref="GetBoxAsync"/> to read each element's rect.
/// </summary>
internal static class WebViewCssMeasurement
{
    /// <summary>
    /// Rect in CSS pixels returned from <c>getBoundingClientRect()</c>.
    /// </summary>
    public readonly record struct Box(double X, double Y, double Width, double Height);

    /// <summary>Create a WebView2 sized for the scenario. Not initialized yet.</summary>
    public static WebView2 Create(double width, double height)
    {
        return new WebView2
        {
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    /// <summary>
    /// Initialize the WebView2 (must already be in the visual tree), navigate
    /// to the given HTML, and wait for load + a short settle frame.
    /// </summary>
    public static async Task LoadAsync(WebView2 wv, string html)
    {
        // WebView2 needs to be fully Loaded before the core process will bind.
        if (!wv.IsLoaded)
        {
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler loadedHandler = (s, e) => loaded.TrySetResult(true);
            wv.Loaded += loadedHandler;
            if (wv.IsLoaded) loaded.TrySetResult(true);
            await loaded.Task;
            wv.Loaded -= loadedHandler;
        }

        // Kick off core init, await, then poll CoreWebView2 if needed. In some
        // tree configurations the Task resolves slightly before the property
        // is populated (known WinUI WebView2 quirk, mirrored in the WinUI
        // Community Toolkit wrappers). Poll up to ~5 seconds.
        try
        {
            await wv.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"# WebView2.EnsureCoreWebView2Async threw: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        while (wv.CoreWebView2 is null && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(25);

        if (wv.CoreWebView2 is null)
        {
            Console.WriteLine($"# WebView2 state after 5s: IsLoaded={wv.IsLoaded}, " +
                $"ActualSize=({wv.ActualWidth:F0}x{wv.ActualHeight:F0}), " +
                $"Visibility={wv.Visibility}");
            throw new InvalidOperationException(
                "WebView2.CoreWebView2 did not initialize within 5s after EnsureCoreWebView2Async " +
                "— WebView2 Runtime may be missing or the control failed to parent.");
        }

        var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavHandler(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            wv.CoreWebView2.NavigationCompleted -= NavHandler;
            navDone.TrySetResult(e.IsSuccess);
        }
        wv.CoreWebView2.NavigationCompleted += NavHandler;

        wv.NavigateToString(html);
        await navDone.Task;

        // One settle tick so reflow after <script>/<style> is applied.
        await Task.Delay(80);
    }

    /// <summary>
    /// Read the bounding-client-rect of an element selected by CSS selector
    /// within the loaded page. Returns null if the selector matches nothing.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "WebView2 CSS-measurement selftest helper; serializes a string selector and reads back a string / double[]. WebView2 fixtures are AOT-skip-listed (SelfTestRunner.DefaultAotSkipPatterns).")]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "WebView2 CSS-measurement selftest helper; AOT-skip-listed (see IL2026 note).")]
    public static async Task<Box?> GetBoxAsync(WebView2 wv, string selector)
    {
        var literal = JsonSerializer.Serialize(selector);
        var js = $@"
(function() {{
    var el = document.querySelector({literal});
    if (!el) return 'null';
    var r = el.getBoundingClientRect();
    return JSON.stringify([r.left, r.top, r.width, r.height]);
}})()";
        var raw = await wv.ExecuteScriptAsync(js);
        // ExecuteScriptAsync returns a JSON-encoded string — unwrap once to
        // get the inner string, which itself is either 'null' or an array.
        var inner = JsonSerializer.Deserialize<string>(raw);
        if (inner is null || inner == "null") return null;
        var arr = JsonSerializer.Deserialize<double[]>(inner);
        if (arr is null || arr.Length != 4) return null;
        return new Box(arr[0], arr[1], arr[2], arr[3]);
    }

    /// <summary>
    /// Convenience wrapper around the standard boilerplate for flex tests:
    /// a document with a single flex container and a caller-provided inner
    /// body of children. The container is selectable via <c>#c</c>; children
    /// are expected to be selectable via their supplied ids.
    /// </summary>
    public static string BuildFlexHtml(
        string flexDirection,
        double? widthPx,
        double? heightPx,
        double gapPx,
        string justifyContent,
        string alignItems,
        string childrenHtml,
        string extraContainerCss = "")
    {
        string sizing(string prop, double? v) =>
            v is double d ? $"{prop}:{d:F1}px;" : "";
        return $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/>
<style>
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  html, body {{ margin: 0; padding: 0; width: 100%; height: 100%; background: #fafafa; }}
  #c {{
    display: flex;
    flex-direction: {flexDirection};
    gap: {gapPx:F1}px;
    justify-content: {justifyContent};
    align-items: {alignItems};
    {sizing("width", widthPx)}
    {sizing("height", heightPx)}
    background: #eaeaea;
    {extraContainerCss}
  }}
  .box {{ background: #bdbdbd; color: #000; text-align: center; }}
</style>
</head>
<body><div id='c'>{childrenHtml}</div></body>
</html>";
    }
}
