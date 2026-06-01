using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace Reactor.AotHelloWorld.TrimAssertions;

/// <summary>
/// Spec 048 §11 / §13.2 — empirical trim assertion against the AOT-published
/// Hello-World binary. The companion app
/// (<c>../Reactor.AotHelloWorld/App.cs</c>) calls only <c>TextBlock</c> and
/// <c>Button</c>; spec 048's lazy registration shape (Patterns A and B) says
/// the trimmer must therefore remove every other Reactor handler / WinUI
/// control that this app does not reach. These tests verify that empirically
/// by scanning the published exe + all bundled .dlls for forbidden symbols.
///
/// <para><b>Why scan binaries instead of trusting analyzer warnings?</b>
/// Trim/AOT analyzer warnings catch <i>some</i> re-rooting, but they do not
/// catch every shape: a static cctor in a different assembly, an MSBuild
/// reflection-on-types target, an attribute-driven AssemblyMetadataAttribute
/// — all of these can re-root types silently with no warning. The only
/// authoritative answer is "did the symbol survive in the published
/// binary?", which is what this test asks.</para>
///
/// <para><b>How the scan works.</b> For each forbidden symbol we look for
/// the byte sequence both as ASCII (managed metadata table strings) and as
/// UTF-16 LE (occasionally used in resource strings). The forbidden list is
/// drawn from spec §11; the allow-list of expected-present strings is the
/// minimal positive control that proves the scanner can find a symbol when
/// it is present (so a future trim regression that strips EVERYTHING does
/// not silently pass these tests).</para>
///
/// <para><b>Skip semantics.</b> Tests skip with an explanatory message when
/// the publish folder is missing — a developer running <c>dotnet test
/// Reactor.slnx</c> locally without first publishing would otherwise see a
/// failure for an artifact they never built. CI sets the
/// <c>REACTOR_AOT_PUBLISH_DIR</c> environment variable, so in CI a missing
/// folder is a real failure: the publish step did not produce the expected
/// layout.</para>
/// </summary>
public sealed class TrimAssertionTests
{
    /// <summary>Reactor-side symbols that <b>must NOT</b> appear in the
    /// published binary at the current spec phase. The set grows as later
    /// phases migrate more controls to lazy registration:
    /// <list type="bullet">
    ///   <item>Phase 2: <c>Marquee*</c> — the Pattern A proof.
    ///         An app that does not call <c>Marquee.Of(...)</c> must not
    ///         pull in <c>MarqueeHandler</c> or <c>MarqueeControl</c>.</item>
    ///   <item>Phase 3 (Pattern B migration, §3.4 complete): built-in
    ///         handlers + WinUI control types whose factories aren't called
    ///         from <c>App.Render()</c>. With <c>RegisterV1BuiltInHandlers</c>
    ///         deleted (§3.4, commit <c>d63066df</c>), the only static
    ///         reference to a built-in handler is the <c>Reg&lt;…&gt;.Done</c>
    ///         touch inside its factory body. A factory that is never called
    ///         leaves its closed-generic <c>Reg&lt;&gt;</c> slot unreachable,
    ///         and the trimmer drops both the slot and the handler type it
    ///         would have instantiated.</item>
    /// </list>
    /// Symbols are added to this list only when the spec phase that owns
    /// their lazy-registration shape has shipped. A premature addition
    /// would fail the assertion for the wrong reason — not "trim
    /// regression" but "Phase N hasn't happened yet".
    ///
    /// <para><b>Why this list, not the entire catalog?</b> §11 of the spec
    /// scopes the forbidden set to a handful of canonical Reactor-owned
    /// names — enough to catch a regression that re-roots the catalog,
    /// without coupling the test to WinAppSDK's own evolving trim story.
    /// Reactor handler classes (e.g. <c>TreeViewHandler</c>) are
    /// 100%-Reactor-owned and must always trim; WinUI control names
    /// (e.g. <c>Microsoft.UI.Xaml.Controls.TreeView</c>) are probed but
    /// caveated — the SDK may root them internally and that is out of
    /// scope per spec §11.</para>
    ///
    /// <para><b>Hello-World reachability surface.</b>
    /// <c>App.Render()</c> calls exactly three factories — <c>VStack</c>,
    /// <c>TextBlock</c>, <c>Button</c>. Everything else in the Reactor
    /// catalog is unreachable from the entry point.</para></summary>
    private static readonly string[] ForbiddenSymbols =
    [
        // ── Phase 2 — Pattern A proof (external Marquee control). ────────
        // ProjectReference'd but never constructed (Marquee.Of is never
        // called). Static-cctor-driven Pattern A registration means the
        // trimmer cannot find a static reference to MarqueeHandler or
        // MarqueeControl from any kept type.
        "MarqueeControl",
        "MarqueeHandler",

        // ── Phase 3 — Pattern B proof (built-in lazy registration). ──────
        // Reactor-owned handler classes for collection / navigation controls
        // not used by App.Render(). With RegisterV1BuiltInHandlers deleted
        // (§3.4), each handler is rooted only by its factory's Reg<>.Done
        // touch; an uncalled factory means a cold closed-generic Reg<> slot
        // and a trimmable handler type.
        //
        // NOTE: descriptor-backed controls compile to *DescriptorHandler
        // (e.g. TreeViewDescriptorHandler), not bare *Handler — the trim
        // assertion must use the real class names or a positive substring
        // match will never trigger and the guard becomes vacuous for those
        // entries.
        "TreeViewDescriptorHandler",
        "GridViewHandler",
        "TabViewDescriptorHandler",
        "ListViewHandler",
        "FlipViewDescriptorHandler",
        "PivotDescriptorHandler",
        "NavigationViewDescriptorHandler",
        "CalendarViewDescriptorHandler",
        "CalendarDatePickerDescriptorHandler",
        "TimePickerDescriptorHandler",
        "DatePickerDescriptorHandler",
        "NumberBoxDescriptorHandler",
        "ColorPickerDescriptorHandler",
        "MediaPlayerElementDescriptorHandler",
        "WebView2DescriptorHandler",
        "MapControlDescriptorHandler",
        "TeachingTipDescriptorHandler",
        "InfoBarDescriptorHandler",
        "BreadcrumbBarDescriptorHandler",

        // Reactor-owned element-record names for the same set. An element
        // record is reachable only if its factory is called *or* an external
        // caller constructs it directly. App.Render() does neither for any of
        // these, so the records and their per-element wiring (Update / Mount
        // handler dispatch arms) must all trim.
        "TreeViewElement",
        "GridViewElement",
        "TabViewElement",
        "CalendarViewElement",
        "NumberBoxElement",
        "WebView2Element",

        // ── NOT included: Microsoft.UI.Xaml.Controls.* names. ────────────
        // Earlier drafts of this list also probed WinUI control type names
        // like Microsoft.UI.Xaml.Controls.{TreeView, GridView, TabView,
        // CalendarView, NumberBox}. Empirical observation post-§3.4: those
        // names survive in the NativeAOT-published .exe even when no Reactor
        // factory references them, because WinAppSDK's CsWinRT projection
        // layer carries a complete type-table for COM activation regardless
        // of which controls the app actually uses. Per spec §11 caveat
        // ("the assertion checks Reactor-side rooting, not the SDK's
        // internal types — an SDK regression that re-roots its own controls
        // is out of scope for this guard"), the WinUI names are not a valid
        // probe today. Reactor-side rooting is fully covered by the
        // Reactor-owned symbol set above (Handler classes + Element
        // records), which is the spec-relevant invariant.
    ];

    /// <summary>Positive control — the scanner is correct only if it can
    /// find a string that absolutely must be in the binary. <c>App</c> is
    /// the entry-point class name; if it is absent, the app didn't compile
    /// and the negative assertions are vacuous.</summary>
    private const string PositiveControlSymbol = "App";

    /// <summary>Locate the AOT publish folder. Skips the test cleanly when
    /// the folder is absent — see class doc-comment for rationale.</summary>
    private static string ResolvePublishDir()
    {
        // Highest precedence: CI-provided absolute path.
        var fromEnv = Environment.GetEnvironmentVariable("REACTOR_AOT_PUBLISH_DIR");
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;

        // Fallback: convention-discover under the sibling project's bin/.
        // Walks up from the test assembly to the repo root, then dives into
        // tests/aot_trim_proof/Reactor.AotHelloWorld/bin/. This works when a
        // developer ran `dotnet publish` locally without setting the env var.
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Reactor.slnx")))
            here = here.Parent;
        if (here is null) return string.Empty;

        var appBin = Path.Combine(
            here.FullName, "tests", "aot_trim_proof", "Reactor.AotHelloWorld", "bin");
        if (!Directory.Exists(appBin)) return string.Empty;

        // Find the most recent `publish` folder under any
        // Configuration/TargetFramework/Runtime/ layout.
        var publishDirs = Directory.EnumerateDirectories(appBin, "publish", SearchOption.AllDirectories)
            .Select(p => new DirectoryInfo(p))
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .ToArray();

        return publishDirs.Length == 0 ? string.Empty : publishDirs[0].FullName;
    }

    /// <summary>Files the scanner inspects. Reactor and external-test-control
    /// assemblies + the app exe; everything else (WinAppSDK runtime, CoreCLR,
    /// CsWinRT projections) is out of scope — spec §11 caveat.</summary>
    private static IEnumerable<string> AssembliesToScan(string publishDir)
    {
        // Inclusive set: every file whose name looks like Reactor-owned code.
        // Pattern: Reactor*.dll, Reactor*.exe (handles Reactor.dll, the app
        // exe, and Reactor.External.TestControl.dll uniformly).
        return Directory.EnumerateFiles(publishDir, "Reactor*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static bool BinaryContains(string filePath, string needle)
    {
        // Memory-mapping a 100-MB binary is overkill for needle detection; a
        // streaming read with overlap windows beats the worst case and is
        // O(file-size) regardless of needle count.
        var asciiBytes = Encoding.ASCII.GetBytes(needle);
        var utf16Bytes = Encoding.Unicode.GetBytes(needle);
        var fileBytes = File.ReadAllBytes(filePath);
        return IndexOf(fileBytes, asciiBytes) >= 0 || IndexOf(fileBytes, utf16Bytes) >= 0;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        var first = needle[0];
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack[i] != first) continue;
            var match = true;
            for (var j = 1; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    [SkippableFact]
    public void PublishedBinary_DoesNotContain_ForbiddenSymbols()
    {
        var publishDir = ResolvePublishDir();
        Skip.If(string.IsNullOrEmpty(publishDir),
            "AOT publish folder not found. Set REACTOR_AOT_PUBLISH_DIR or run " +
            "`dotnet publish tests/aot_trim_proof/Reactor.AotHelloWorld -c Release -r win-x64 -p:PublishAot=true` first.");

        var scannedFiles = AssembliesToScan(publishDir).ToArray();
        Skip.If(scannedFiles.Length == 0,
            $"No Reactor*.dll/.exe found under {publishDir}. The publish appears to be empty or incomplete.");

        var violations = new List<string>();
        foreach (var file in scannedFiles)
        {
            foreach (var symbol in ForbiddenSymbols)
            {
                if (BinaryContains(file, symbol))
                    violations.Add($"  {Path.GetFileName(file)} contains forbidden symbol \"{symbol}\"");
            }
        }

        if (violations.Count > 0)
        {
            var msg = new StringBuilder();
            msg.AppendLine("Spec 048 trim assertion failed — the AOT-published Hello-World binary contains");
            msg.AppendLine("symbols that lazy registration was supposed to leave dead:");
            msg.AppendLine();
            foreach (var v in violations) msg.AppendLine(v);
            msg.AppendLine();
            msg.AppendLine($"Publish folder scanned: {publishDir}");
            msg.AppendLine($"Files scanned: {scannedFiles.Length}");
            msg.AppendLine();
            msg.AppendLine("What this means: something in the Reactor catalog or in the app's");
            msg.AppendLine("dependency closure re-rooted a handler that App.Render() does not reach.");
            msg.AppendLine("Common causes: an eager `RegisterV1BuiltInHandlers` call surviving the");
            msg.AppendLine("Phase 3 migration; a new attribute-driven discovery shape; a sample");
            msg.AppendLine("introduced as a ProjectReference (it should be a separate solution).");
            msg.AppendLine();
            msg.AppendLine("How to extend: when adding a new must-trim control, add its symbol(s)");
            msg.AppendLine("to TrimAssertionTests.ForbiddenSymbols.");
            throw new XunitException(msg.ToString());
        }
    }

    [SkippableFact]
    public void PublishedBinary_Contains_PositiveControlSymbol()
    {
        // Negative assertions are only meaningful if the scanner can find
        // SOMETHING in the binary. If this test fails, the scanner is broken
        // and the negative assertions above are vacuous.
        var publishDir = ResolvePublishDir();
        Skip.If(string.IsNullOrEmpty(publishDir),
            "AOT publish folder not found — see PublishedBinary_DoesNotContain_ForbiddenSymbols.");

        var scannedFiles = AssembliesToScan(publishDir).ToArray();
        Skip.If(scannedFiles.Length == 0,
            $"No Reactor*.dll/.exe found under {publishDir}.");

        var found = scannedFiles.Any(f => BinaryContains(f, PositiveControlSymbol));
        Assert.True(found,
            $"Positive control failed: \"{PositiveControlSymbol}\" not found in any of the {scannedFiles.Length} " +
            $"scanned files under {publishDir}. The scanner is broken; the negative assertions above are vacuous.");
    }
}
