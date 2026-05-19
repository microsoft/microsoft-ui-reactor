using System.Reflection;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Tests for the private <see cref="DevtoolsMcpServer"/>.IsAllowedOrigin
/// CORS allow-list. Sister to <c>PreviewCaptureServerTests.IsAllowedOrigin*</c>;
/// the two servers share the same allow-list shape (loopback + vscode-webview)
/// and the same suffix-attack failure mode that an unguarded StartsWith would
/// produce.
/// </summary>
public class DevtoolsMcpServerOriginTests
{
    private static readonly Type ServerType = typeof(DevtoolsMcpServer);

    private static bool InvokeIsAllowedOrigin(string origin)
    {
        var mi = ServerType.GetMethod("IsAllowedOrigin",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)mi.Invoke(null, new object?[] { origin })!;
    }

    [Theory]
    [InlineData("vscode-webview://abc123")]
    [InlineData("VSCODE-WEBVIEW://abc")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost")]
    [InlineData("HTTP://LOCALHOST:1234")]
    [InlineData("http://localhost/")]
    [InlineData("http://localhost?q=1")]
    [InlineData("http://localhost#frag")]
    public void IsAllowedOrigin_Accepts_Expected_Origins(string origin)
    {
        Assert.True(InvokeIsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("http://evil.com")]
    [InlineData("https://example.com")]
    [InlineData("file:///C:/foo")]
    [InlineData("ftp://localhost")]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://evil.com/localhost")]
    // MCP traffic is HTTP only — https://localhost is rejected to keep the
    // allow-list shape distinct from PreviewCaptureServer's (which DOES
    // accept https). Pin: a regression that added a generic
    // `uri.Scheme is http or https` would silently widen the surface.
    [InlineData("https://localhost")]
    [InlineData("https://localhost:8080")]
    // Suffix attack — the host is "localhost.evil.com", not "localhost".
    // A naive StartsWith allow-list would wave these through because the
    // string genuinely starts with "http://localhost".
    [InlineData("http://localhost.evil.com")]
    [InlineData("http://localhost.evil.com:8080")]
    [InlineData("http://127.0.0.1.evil.com")]
    [InlineData("http://127.0.0.1.evil.com:80")]
    [InlineData("http://localhost-evil.com")]
    // Userinfo trick — the real host is evil.com.
    [InlineData("http://localhost@evil.com")]
    [InlineData("http://127.0.0.1@evil.com")]
    public void IsAllowedOrigin_Rejects_Unallowed_Origins(string origin)
    {
        Assert.False(InvokeIsAllowedOrigin(origin));
    }
}
