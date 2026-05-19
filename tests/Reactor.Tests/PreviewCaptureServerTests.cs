using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Unit tests for the pure helpers in <see cref="PreviewCaptureServer"/>.
/// The constructor requires a <c>DispatcherQueue</c> + <c>Window</c> which
/// xUnit can't provide; we reach the static helpers via reflection and the
/// instance helpers via <c>RuntimeHelpers.GetUninitializedObject</c> with
/// the relevant private fields populated.
///
/// Covers security-critical paths (token comparison, host header check,
/// origin allow-list, body-cap reader) that a regression would silently
/// loosen.
/// </summary>
public class PreviewCaptureServerTests
{
    private static readonly Type ServerType = typeof(PreviewCaptureServer);

    // ══════════════════════════════════════════════════════════════
    //  GenerateToken — base64-url variant (no '+', '/', '=' padding)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateToken_Produces_NonEmpty_UrlSafe_Base64()
    {
        var mi = ServerType.GetMethod("GenerateToken",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var token = (string)mi.Invoke(null, null)!;

        // 32-byte payload → 43 base64 chars after stripping '=' padding.
        Assert.Equal(43, token.Length);
        // base64-url variant: '+' → '-', '/' → '_', no '=' padding.
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void GenerateToken_Yields_Different_Tokens_Across_Calls()
    {
        // RandomNumberGenerator.Fill — birthday-bound collision on a
        // 256-bit space is effectively zero. A regression that, say,
        // statically initialised the buffer to zeroes would fail
        // this — the bug would surface as a fixed token across launches
        // (catastrophic auth weakening).
        var mi = ServerType.GetMethod("GenerateToken",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var t1 = (string)mi.Invoke(null, null)!;
        var t2 = (string)mi.Invoke(null, null)!;
        Assert.NotEqual(t1, t2);
    }

    // ══════════════════════════════════════════════════════════════
    //  IsAllowedOrigin — CORS allow-list
    // ══════════════════════════════════════════════════════════════

    private static bool InvokeIsAllowedOrigin(string origin)
    {
        var mi = ServerType.GetMethod("IsAllowedOrigin",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)mi.Invoke(null, new object?[] { origin })!;
    }

    [Theory]
    [InlineData("vscode-webview://abc123")]
    [InlineData("VSCODE-WEBVIEW://abc")] // case-insensitive
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1/")]            // trailing slash → path delimiter
    [InlineData("http://localhost")]
    [InlineData("HTTP://LOCALHOST:1234")]
    [InlineData("http://localhost/")]
    [InlineData("http://localhost?q=1")]         // query delimiter
    [InlineData("http://localhost#frag")]        // fragment delimiter
    [InlineData("https://localhost")]
    [InlineData("https://localhost:8443")]
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
    // Shape-preservation pin: the pre-refactor allow-list rejected
    // `https://127.0.0.1` (only `http://127.0.0.1` was accepted, and
    // `https` was reserved for `localhost`). A refactor that collapsed
    // the scheme/host pair into a generic "http-or-https + loopback"
    // rule would silently widen the surface.
    [InlineData("https://127.0.0.1")]
    [InlineData("https://127.0.0.1:8443")]
    // Suffix attack — the host is "localhost.evil.com", not "localhost".
    // A naive StartsWith allow-list would wave these through because the
    // string genuinely starts with "http://localhost". Uri.TryCreate parses
    // the host field separately, so exact-match against "localhost" /
    // "127.0.0.1" fails closed.
    [InlineData("http://localhost.evil.com")]
    [InlineData("https://localhost.evil.com")]
    [InlineData("http://localhost.evil.com:8080")]
    [InlineData("http://127.0.0.1.evil.com")]
    [InlineData("http://127.0.0.1.evil.com:80")]
    // Hyphen — same suffix-attack shape, different separator char.
    [InlineData("http://localhost-evil.com")]
    // Userinfo trick — `user@host` syntax; the real host is evil.com.
    [InlineData("http://localhost@evil.com")]
    [InlineData("http://127.0.0.1@evil.com")]
    public void IsAllowedOrigin_Rejects_Unallowed_Origins(string origin)
    {
        Assert.False(InvokeIsAllowedOrigin(origin));
    }

    // ══════════════════════════════════════════════════════════════
    //  ReadCappedBody — TASK-023 size cap
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ReadCappedBody_Reads_Within_Cap()
    {
        var payload = "{\"name\":\"value\"}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        var result = PreviewCaptureServer.ReadCappedBody(stream, Encoding.UTF8, cap: 1024);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void ReadCappedBody_Throws_On_Body_Over_Cap()
    {
        // Pin: a regression that elided the cap check would let an
        // attacker DoS the preview server with a multi-GB body.
        // The cap is 4 MB in production; tests use 64 B to keep the
        // payload small.
        var payload = new byte[256];
        using var stream = new MemoryStream(payload);
        var ex = Assert.Throws<InvalidDataException>(() =>
            PreviewCaptureServer.ReadCappedBody(stream, Encoding.UTF8, cap: 64));
        Assert.Contains("body too large", ex.Message);
    }

    [Fact]
    public void ReadCappedBody_Empty_Stream_Returns_Empty_String()
    {
        using var stream = new MemoryStream();
        var result = PreviewCaptureServer.ReadCappedBody(stream, Encoding.UTF8, cap: 1024);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ReadCappedBody_Boundary_At_Exact_Cap_Succeeds()
    {
        // Cap is inclusive in spirit — a body of exactly `cap` bytes is
        // accepted, only `cap + 1` throws. The check is `total > cap`,
        // not `total >= cap`.
        var payload = new byte[64];
        Array.Fill(payload, (byte)'x');
        using var stream = new MemoryStream(payload);
        var result = PreviewCaptureServer.ReadCappedBody(stream, Encoding.UTF8, cap: 64);
        Assert.Equal(64, result.Length);
    }

    // ══════════════════════════════════════════════════════════════
    //  AcquireFreePortHolding — loopback port reservation
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void AcquireFreePortHolding_Returns_Bound_Loopback_Port()
    {
        var mi = ServerType.GetMethod("AcquireFreePortHolding",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = mi.Invoke(null, null)!;

        // ValueTuple (int Port, TcpListener Holder)
        var port = (int)result.GetType().GetField("Item1")!.GetValue(result)!;
        var holder = (TcpListener)result.GetType().GetField("Item2")!.GetValue(result)!;

        try
        {
            Assert.True(port > 0);
            Assert.True(port <= 65535);
            // Holder must be listening on loopback at that port.
            var ep = (IPEndPoint)holder.LocalEndpoint;
            Assert.Equal(port, ep.Port);
            Assert.Equal(IPAddress.Loopback, ep.Address);
        }
        finally
        {
            try { holder.Stop(); } catch { }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  BearerMatches — constant-time token comparison
    //
    //  The instance method reads _authToken. Bypass the ctor so we don't
    //  need a DispatcherQueue / Window — manually plant the token field.
    // ══════════════════════════════════════════════════════════════

    private static PreviewCaptureServer MakeServerWithToken(string token)
    {
        var instance = (PreviewCaptureServer)global::System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(ServerType);
        var f = ServerType.GetField("_authToken",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        f.SetValue(instance, token);
        return instance;
    }

    private static bool InvokeBearerMatches(PreviewCaptureServer s, string? auth)
    {
        var mi = ServerType.GetMethod("BearerMatches",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)mi.Invoke(s, new object?[] { auth })!;
    }

    [Fact]
    public void BearerMatches_Null_Or_Empty_Header_Returns_False()
    {
        var s = MakeServerWithToken("expected-token");
        Assert.False(InvokeBearerMatches(s, null));
        Assert.False(InvokeBearerMatches(s, ""));
    }

    [Fact]
    public void BearerMatches_Missing_Prefix_Returns_False()
    {
        // "Bearer " prefix is mandatory. A regression that accepted bare
        // tokens would let `Authorization: <token>` succeed — that's
        // semantically wrong and breaks the WWW-Authenticate contract.
        var s = MakeServerWithToken("expected-token");
        Assert.False(InvokeBearerMatches(s, "expected-token"));
        Assert.False(InvokeBearerMatches(s, "Basic dXNlcjpwYXNz"));
    }

    [Fact]
    public void BearerMatches_Correct_Token_Returns_True()
    {
        var s = MakeServerWithToken("abc123");
        Assert.True(InvokeBearerMatches(s, "Bearer abc123"));
    }

    [Fact]
    public void BearerMatches_Wrong_Token_Same_Length_Returns_False()
    {
        // The constant-time-XOR comparison. Bug: a regression that fell
        // back to `==` would still return false here, but would also
        // leak timing info — pin the false output regardless of impl
        // choice.
        var s = MakeServerWithToken("abc123");
        Assert.False(InvokeBearerMatches(s, "Bearer xyz789"));
    }

    [Fact]
    public void BearerMatches_Wrong_Length_Returns_False_Without_Comparing()
    {
        // The `presented.Length != expected.Length` early-out. Pin: a
        // regression that proceeded to compare different-length spans
        // would IndexOutOfRange — the early-out is load-bearing.
        var s = MakeServerWithToken("abc123");
        Assert.False(InvokeBearerMatches(s, "Bearer abc"));
        Assert.False(InvokeBearerMatches(s, "Bearer abc1234"));
    }

    [Fact]
    public void BearerMatches_Token_With_Leading_Whitespace_Is_Trimmed()
    {
        // The `.Trim()` call on the presented span. A regression that
        // dropped the trim would reject `Bearer  abc123` (two spaces),
        // which is permitted per RFC 6750.
        var s = MakeServerWithToken("abc123");
        Assert.True(InvokeBearerMatches(s, "Bearer  abc123"));
        Assert.True(InvokeBearerMatches(s, "Bearer abc123\t"));
    }

    // ══════════════════════════════════════════════════════════════
    //  IsAllowedHost — DNS rebinding defense (TASK-020)
    // ══════════════════════════════════════════════════════════════

    private static PreviewCaptureServer MakeServerWithPort(int port)
    {
        var instance = (PreviewCaptureServer)global::System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(ServerType);
        // `Port` is `{ get; }` auto-property — backed by a compiler-generated
        // private field with `<Port>k__BackingField` name.
        var field = ServerType.GetField("<Port>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(instance, port);
        return instance;
    }

    private static bool InvokeIsAllowedHost(PreviewCaptureServer s, string? hostHeader)
    {
        var mi = ServerType.GetMethod("IsAllowedHost",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)mi.Invoke(s, new object?[] { hostHeader })!;
    }

    [Fact]
    public void IsAllowedHost_Null_Or_Empty_Returns_False()
    {
        var s = MakeServerWithPort(54321);
        Assert.False(InvokeIsAllowedHost(s, null));
        Assert.False(InvokeIsAllowedHost(s, ""));
    }

    [Fact]
    public void IsAllowedHost_Localhost_Or_Loopback_With_Correct_Port_Returns_True()
    {
        var s = MakeServerWithPort(54321);
        Assert.True(InvokeIsAllowedHost(s, "127.0.0.1:54321"));
        Assert.True(InvokeIsAllowedHost(s, "localhost:54321"));
        // localhost is case-insensitive per spec — pin the
        // OrdinalIgnoreCase comparison.
        Assert.True(InvokeIsAllowedHost(s, "LOCALHOST:54321"));
    }

    [Fact]
    public void IsAllowedHost_DNS_Rebinding_Attack_Surface_Rejected()
    {
        // Bug shape (TASK-020): an attacker resolves attacker.com to
        // 127.0.0.1 and uses `Host: attacker.com:54321` to reach the
        // preview server. The IsAllowedHost gate must reject by name —
        // pin that even a port-matching attacker host fails.
        var s = MakeServerWithPort(54321);
        Assert.False(InvokeIsAllowedHost(s, "attacker.com:54321"));
        Assert.False(InvokeIsAllowedHost(s, "evil.localhost.attacker.com:54321"));
    }

    [Fact]
    public void IsAllowedHost_Wrong_Port_Rejected()
    {
        // The port comparison is exact-string — `127.0.0.1:54322` does
        // not match port 54321 even though the prefix matches.
        var s = MakeServerWithPort(54321);
        Assert.False(InvokeIsAllowedHost(s, "127.0.0.1:54322"));
        Assert.False(InvokeIsAllowedHost(s, "localhost:80"));
    }

    [Fact]
    public void IsAllowedHost_127_0_0_1_Is_Case_Sensitive_For_Numeric_Match()
    {
        // Numeric loopback is OrdinalEquals (not OrdinalIgnoreCase) —
        // there is no case to consider for digits, but pin the exact-
        // string contract so a regression that loosened to
        // OrdinalIgnoreCase doesn't accidentally accept anything weird.
        // (The localhost arm uses OrdinalIgnoreCase deliberately.)
        var s = MakeServerWithPort(80);
        Assert.True(InvokeIsAllowedHost(s, "127.0.0.1:80"));
    }
}
