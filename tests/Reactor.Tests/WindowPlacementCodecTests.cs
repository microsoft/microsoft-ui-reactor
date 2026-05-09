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

    // -- IsPlausiblePlacement (W-5 hardening) ----------------------------------

    private static WindowPlacementCodec.WINDOWPLACEMENT MakePlausible() => new()
    {
        length = 44,
        flags = 0,
        showCmd = 1, // SW_NORMAL
        ptMinPosition = new WindowPlacementCodec.POINT { X = -1, Y = -1 },
        ptMaxPosition = new WindowPlacementCodec.POINT { X = -1, Y = -1 },
        rcNormalPosition = new WindowPlacementCodec.RECT { Left = 100, Top = 100, Right = 1100, Bottom = 800 },
    };

    [Fact]
    public void IsPlausible_Accepts_Reasonable_Placement()
    {
        Assert.True(WindowPlacementCodec.IsPlausiblePlacement(MakePlausible()));
    }

    [Fact]
    public void IsPlausible_Rejects_Zero_Or_Negative_Width()
    {
        var p = MakePlausible();
        p.rcNormalPosition.Right = p.rcNormalPosition.Left; // width = 0
        Assert.False(WindowPlacementCodec.IsPlausiblePlacement(p));

        p = MakePlausible();
        p.rcNormalPosition.Right = p.rcNormalPosition.Left - 50; // width < 0
        Assert.False(WindowPlacementCodec.IsPlausiblePlacement(p));
    }

    [Fact]
    public void IsPlausible_Rejects_Zero_Or_Negative_Height()
    {
        var p = MakePlausible();
        p.rcNormalPosition.Bottom = p.rcNormalPosition.Top;
        Assert.False(WindowPlacementCodec.IsPlausiblePlacement(p));
    }

    [Fact]
    public void IsPlausible_Rejects_Oversized_Rect()
    {
        var p = MakePlausible();
        p.rcNormalPosition.Right = p.rcNormalPosition.Left + WindowPlacementCodec.MaxPlausibleDimensionPx + 1;
        Assert.False(WindowPlacementCodec.IsPlausiblePlacement(p));
    }

    [Fact]
    public void IsPlausible_Accepts_Negative_Coordinates_Within_Sanity_Box()
    {
        // Secondary monitor to the left of / above the primary is normal.
        var p = MakePlausible();
        p.rcNormalPosition.Left = -1500;
        p.rcNormalPosition.Top = -200;
        p.rcNormalPosition.Right = -100;
        p.rcNormalPosition.Bottom = 600;
        Assert.True(WindowPlacementCodec.IsPlausiblePlacement(p));
    }

    [Fact]
    public void IsPlausible_Rejects_Coordinates_Outside_Sanity_Box()
    {
        var p = MakePlausible();
        p.rcNormalPosition.Left = -(WindowPlacementCodec.MaxPlausibleCoordinateMagnitude + 1);
        Assert.False(WindowPlacementCodec.IsPlausiblePlacement(p));

        p = MakePlausible();
        p.rcNormalPosition.Right = WindowPlacementCodec.MaxPlausibleCoordinateMagnitude + 1;
        Assert.False(WindowPlacementCodec.IsPlausiblePlacement(p));
    }

    [Fact]
    public void IsPlausible_Rejects_Bogus_ShowCmd()
    {
        var p = MakePlausible();
        p.showCmd = 7777;
        Assert.False(WindowPlacementCodec.IsPlausiblePlacement(p));
    }

    [Fact]
    public void IsPlausible_Accepts_Documented_ShowCmd_Values()
    {
        // SW_NORMAL=1, SW_SHOWMINIMIZED=2, SW_MAXIMIZE=3, SW_SHOW=5, SW_RESTORE=9.
        foreach (var sw in new[] { 1, 2, 3, 5, 9 })
        {
            var p = MakePlausible();
            p.showCmd = sw;
            Assert.True(WindowPlacementCodec.IsPlausiblePlacement(p), $"showCmd={sw} should be accepted");
        }
    }
}
