using System.Reflection;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Markdown;

/// <summary>
/// Tests for the internal MarkdownHtml.SanitizeUrl scheme-allow-list.
/// TASK-045 — the XSS fence on href/src attributes emitted by the
/// Markdown renderer. Allow-list: http, https, mailto. Anything else
/// becomes `about:blank`.
/// </summary>
public class SanitizeUrlTests
{
    private static readonly Type MarkdownHtmlType =
        typeof(Microsoft.UI.Reactor.Factories).Assembly
            .GetType("Microsoft.UI.Reactor.Markdown.MarkdownHtml")
        ?? throw new InvalidOperationException(
            "MarkdownHtml type not found in Reactor assembly.");

    private static string Sanitize(string url, bool unsafeAllowed = false)
    {
        var mi = MarkdownHtmlType.GetMethod("SanitizeUrl",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)mi.Invoke(null, new object?[] { url, unsafeAllowed })!;
    }

    // ── Allow-listed schemes pass through unchanged ───────────────

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("HTTPS://EXAMPLE.COM")]         // case-insensitive scheme
    [InlineData("mailto:foo@bar.com")]
    [InlineData("mailto:foo@bar.com?subject=hi")]
    public void Safe_Schemes_Pass_Through(string url)
    {
        Assert.Equal(url, Sanitize(url));
    }

    // ── Relative URLs are not absolute — pass through unchanged ───

    [Theory]
    [InlineData("/path/to/thing")]
    [InlineData("relative/path.html")]
    [InlineData("../up/one")]
    [InlineData("#fragment-only")]
    [InlineData("?query=only")]
    public void Relative_Urls_Pass_Through(string url)
    {
        Assert.Equal(url, Sanitize(url));
    }

    // ── XSS vectors are rewritten to about:blank ──────────────────

    [Theory]
    // The canonical XSS payload. A renderer that emits this in an href
    // attribute lets the page execute arbitrary JS on click.
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]            // case-insensitive
    [InlineData("javascript:void(0)")]
    // data: URIs can carry script in text/html or SVG payloads.
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("data:image/svg+xml,<svg onload=alert(1)>")]
    // vbscript: — IE legacy but still a known fingerprint payload.
    [InlineData("vbscript:msgbox(1)")]
    // file:// — disclosure / SSRF surface even on non-script targets.
    [InlineData("file:///C:/windows/win.ini")]
    // ftp, ssh, etc. — not on the allow-list.
    [InlineData("ftp://example.com/")]
    [InlineData("ssh://user@host")]
    // Capitalized variants — System.Uri normalizes scheme to lowercase
    // before comparison, and SafeUrlSchemes is OrdinalIgnoreCase, so
    // both layers fail closed regardless.
    [InlineData("Javascript:alert(1)")]
    [InlineData("DATA:text/html,x")]
    public void Disallowed_Schemes_Become_AboutBlank(string url)
    {
        Assert.Equal("about:blank", Sanitize(url));
    }

    // ── Empty / null-ish input ────────────────────────────────────

    [Fact]
    public void Empty_String_Passes_Through()
    {
        Assert.Equal("", Sanitize(""));
    }

    // ── unsafeAllowed escape hatch bypasses the filter entirely ───

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("file:///C:/x")]
    public void UnsafeAllowed_Bypasses_AllowList(string url)
    {
        // Pin: when the renderer is configured with AllowUnsafeUrls (host
        // is presenting trusted markdown, e.g. local devtools), every URL
        // passes through. A regression that filtered even in unsafe mode
        // would silently break that opt-in.
        Assert.Equal(url, Sanitize(url, unsafeAllowed: true));
    }
}
