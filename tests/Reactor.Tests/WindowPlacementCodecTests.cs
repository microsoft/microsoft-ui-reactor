using Microsoft.UI.Reactor.Hosting.Persistence;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §8 — fingerprint mismatch path of <see cref="WindowPlacementCodec"/>.
/// Capture requires a real HWND so it's exercised end-to-end by selftests;
/// here we focus on the deserialization + fingerprint-comparison branches
/// that decide whether to invoke <c>SetWindowPlacement</c> at all.
/// </summary>
public class WindowPlacementCodecTests
{
    private static byte[] BuildPayload(IReadOnlyList<MonitorRect> monitors)
    {
        // Mirror WindowPlacementCodec's wire format. Embeds a zero-filled
        // WINDOWPLACEMENT struct so byte counts line up but the contents
        // are inert (Restore won't reach SetWindowPlacement when the
        // fingerprint mismatches).
        using var ms = new global::System.IO.MemoryStream();
        using var bw = new global::System.IO.BinaryWriter(ms);
        bw.Write(monitors.Count);
        foreach (var m in monitors)
        {
            bw.Write(m.DeviceName ?? string.Empty);
            bw.Write(m.Left);
            bw.Write(m.Top);
            bw.Write(m.Right);
            bw.Write(m.Bottom);
        }
        // WINDOWPLACEMENT is 44 bytes on 32-bit, but the struct is fixed-
        // layout: int+int+int+POINT+POINT+RECT = 4*3 + 8*2 + 16 = 44.
        bw.Write(new byte[44]);
        return ms.ToArray();
    }

    [Fact]
    public void Restore_Fingerprint_Mismatch_Returns_False_Without_SetWindowPlacement()
    {
        // Saved payload claims one monitor at (0,0,1920,1080); current
        // layout shows two monitors. Restore must reject without invoking
        // any native call.
        var saved = new[] { new MonitorRect("DISPLAY1", 0, 0, 1920, 1080) };
        var payload = BuildPayload(saved);

        var current = new[]
        {
            new MonitorRect("DISPLAY1", 0, 0, 1920, 1080),
            new MonitorRect("DISPLAY2", 1920, 0, 3840, 1080),
        };
        // hwnd == 0 means SetWindowPlacement WOULD fail if called; the
        // mismatch must short-circuit before that.
        Assert.False(WindowPlacementCodec.Restore(0, payload, current));
    }

    [Fact]
    public void Restore_Bounds_Mismatch_Returns_False()
    {
        var saved = new[] { new MonitorRect("DISPLAY1", 0, 0, 1920, 1080) };
        var payload = BuildPayload(saved);

        // Same monitor count, different bounds — reject.
        var current = new[] { new MonitorRect("DISPLAY1", 0, 0, 2560, 1440) };
        Assert.False(WindowPlacementCodec.Restore(0, payload, current));
    }

    [Fact]
    public void Restore_Implausible_Monitor_Count_Returns_False()
    {
        // Tampered payload claiming 1000 monitors. Spec §0.5: reject
        // without dereferencing.
        using var ms = new global::System.IO.MemoryStream();
        using var bw = new global::System.IO.BinaryWriter(ms);
        bw.Write(1000);
        bw.Write(new byte[64]);
        var payload = ms.ToArray();

        Assert.False(WindowPlacementCodec.Restore(0, payload, new MonitorRect[0]));
    }

    [Fact]
    public void Restore_Truncated_Payload_Returns_False()
    {
        // Header claims one monitor, payload ends after 4 bytes.
        var payload = new byte[] { 1, 0, 0, 0 };
        Assert.False(WindowPlacementCodec.Restore(0, payload, new[] { new MonitorRect("D", 0, 0, 1, 1) }));
    }

    [Fact]
    public void MonitorRect_Equality_Is_Structural()
    {
        var a = new MonitorRect("D", 0, 0, 100, 100);
        var b = new MonitorRect("D", 0, 0, 100, 100);
        var c = new MonitorRect("D", 0, 0, 100, 200);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
