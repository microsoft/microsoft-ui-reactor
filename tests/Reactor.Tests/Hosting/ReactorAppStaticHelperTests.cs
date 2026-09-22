using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml.Markup;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting;

/// <summary>
/// Unit tests for the static <see cref="ReactorApp"/> helpers that can run
/// without a XAML <c>Application.Start</c>. These cover:
///
///   * <see cref="ReactorApp.RegisterControlAssembly(IXamlMetadataProvider)"/> —
///     null-guard, duplicate-suppression, and snapshot growth.
///   * <see cref="ReactorApp.RegisterControlAssembly(Assembly)"/> — assembly
///     scan and the "no provider found" error.
///   * <see cref="ReactorApp.FindXamlMetadataProviderInAssembly"/> — the
///     internal type scanner the public assembly overload delegates to.
///   * <see cref="ReactorApp.EmitDipBehaviorChangeNoticeOnce"/> — the
///     once-per-process stderr info-line gate.
///
/// The XAML <c>Run</c> entry-point bodies themselves cannot run in this
/// process (they call <see cref="Microsoft.UI.Xaml.Application.Start"/>),
/// so those structural paths remain end-to-end-only.
/// </summary>
public class ReactorAppStaticHelperTests
{
    // ── RegisterControlAssembly(IXamlMetadataProvider) ────────────────────

    [Fact]
    public void RegisterControlAssembly_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ReactorApp.RegisterControlAssembly((IXamlMetadataProvider)null!));
    }

    [Fact]
    public void RegisterControlAssembly_AddsProviderToRegistry()
    {
        ReactorApp.ResetRegisteredControlAssembliesForTests();
        try
        {
            var fake = new FakeXamlMetadataProvider();
            ReactorApp.RegisterControlAssembly(fake);

            var registered = ReactorApp.RegisteredControlAssemblyProviders;
            Assert.Contains(fake, registered);
            Assert.Single(registered);
        }
        finally
        {
            ReactorApp.ResetRegisteredControlAssembliesForTests();
        }
    }

    [Fact]
    public void RegisterControlAssembly_SameInstanceTwice_IsIdempotent()
    {
        ReactorApp.ResetRegisteredControlAssembliesForTests();
        try
        {
            var fake = new FakeXamlMetadataProvider();
            ReactorApp.RegisterControlAssembly(fake);
            ReactorApp.RegisterControlAssembly(fake);

            Assert.Single(ReactorApp.RegisteredControlAssemblyProviders);
        }
        finally
        {
            ReactorApp.ResetRegisteredControlAssembliesForTests();
        }
    }

    [Fact]
    public void RegisterControlAssembly_MultipleProviders_PreservesOrder()
    {
        ReactorApp.ResetRegisteredControlAssembliesForTests();
        try
        {
            var a = new FakeXamlMetadataProvider();
            var b = new FakeXamlMetadataProvider();
            var c = new FakeXamlMetadataProvider();
            ReactorApp.RegisterControlAssembly(a);
            ReactorApp.RegisterControlAssembly(b);
            ReactorApp.RegisterControlAssembly(c);

            var registered = ReactorApp.RegisteredControlAssemblyProviders;
            Assert.Equal(3, registered.Length);
            Assert.Same(a, registered[0]);
            Assert.Same(b, registered[1]);
            Assert.Same(c, registered[2]);
        }
        finally
        {
            ReactorApp.ResetRegisteredControlAssembliesForTests();
        }
    }

    // ── RegisterControlAssembly(Assembly) + FindXamlMetadataProviderInAssembly ─

    [Fact]
    public void RegisterControlAssembly_NullAssembly_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ReactorApp.RegisterControlAssembly((Assembly)null!));
    }

    [Fact]
    public void RegisterControlAssembly_AssemblyWithoutProvider_ThrowsInvalidOperation()
    {
        // System.Private.CoreLib (typeof(string).Assembly) cannot contain a
        // XAML metadata provider — it has no dependency on WinUI.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ReactorApp.RegisterControlAssembly(typeof(string).Assembly));
        Assert.Contains("No IXamlMetadataProvider found", ex.Message);
    }

    [Fact]
    public void RegisterControlAssembly_AssemblyWithProvider_RegistersFakeFromTestAssembly()
    {
        // FakeXamlMetadataProvider lives in this test assembly, so the
        // scanner should find it and register an instance.
        ReactorApp.ResetRegisteredControlAssembliesForTests();
        try
        {
            ReactorApp.RegisterControlAssembly(typeof(FakeXamlMetadataProvider).Assembly);

            var registered = ReactorApp.RegisteredControlAssemblyProviders;
            // At least one provider was registered — the scanner picked up one
            // of our fakes (it returns the first viable type).
            Assert.NotEmpty(registered);
            Assert.Contains(registered,
                p => p is FakeXamlMetadataProvider or AnotherFakeXamlMetadataProvider);
        }
        finally
        {
            ReactorApp.ResetRegisteredControlAssembliesForTests();
        }
    }

    [Fact]
    public void FindXamlMetadataProviderInAssembly_ReturnsNullWhenNoCandidates()
    {
        // System.Private.CoreLib again — no IXamlMetadataProvider types.
        var found = ReactorApp.FindXamlMetadataProviderInAssembly(typeof(string).Assembly);
        Assert.Null(found);
    }

    [Fact]
    public void FindXamlMetadataProviderInAssembly_ReturnsConcreteCandidate()
    {
        // Pull the candidate out of our own test assembly. The scanner skips
        // abstract types (AbstractXamlMetadataProviderShouldBeSkipped) and
        // types without a parameterless ctor — only concrete fakes survive.
        var found = ReactorApp.FindXamlMetadataProviderInAssembly(
            typeof(FakeXamlMetadataProvider).Assembly);
        Assert.NotNull(found);
        Assert.True(
            found is FakeXamlMetadataProvider or AnotherFakeXamlMetadataProvider,
            $"Unexpected concrete type: {found.GetType().FullName}");
    }

    // ── EmitDipBehaviorChangeNoticeOnce ──────────────────────────────────

    [Collection("ConsoleTests")]
    public class DipNoticeTests
    {
        [Fact]
        public void EmitDipBehaviorChangeNoticeOnce_FirstCall_WritesStderrInfoLine()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.EmitDipBehaviorChangeNoticeOnce(width: 800, height: 600);
                var stderr = sw.ToString();
                Assert.Contains("[reactor]", stderr);
                Assert.Contains("DIP", stderr);
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        [Fact]
        public void EmitDipBehaviorChangeNoticeOnce_SecondCall_IsSilent()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            ReactorApp.EmitDipBehaviorChangeNoticeOnce(width: 800, height: 600); // first call latches

            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.EmitDipBehaviorChangeNoticeOnce(width: 800, height: 600);
                Assert.Empty(sw.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        // The notice announces a migration that an app declaring no size has
        // nothing to do about: it already gets the OS-chosen extent. Emitting it
        // anyway contradicts the documented guidance to omit width/height, so a
        // size-less Run must stay quiet.
        [Fact]
        public void EmitDipBehaviorChangeNoticeOnce_NoSizeSupplied_IsSilent()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.EmitDipBehaviorChangeNoticeOnce();
                Assert.Empty(sw.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        // A size-less call must also not consume the one-shot latch, or the next
        // app that *does* declare a size would silently lose its notice.
        [Fact]
        public void EmitDipBehaviorChangeNoticeOnce_NoSizeThenSize_StillNotifies()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            ReactorApp.EmitDipBehaviorChangeNoticeOnce();

            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.EmitDipBehaviorChangeNoticeOnce(width: 1024);
                Assert.Contains("[reactor]", sw.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        // OpenWindow announces only after the window actually opened, so a spec
        // that is about to be rejected cannot burn the one-shot latch and silence
        // the next window that legitimately should report. This drives
        // OpenAndAnnounce — the code OpenWindow runs — with a failing open, which
        // is how the ordering is reachable without constructing WinUI types.
        [Fact]
        public void OpenAndAnnounce_FailedOpen_DoesNotConsumeLatch()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var sized = new WindowSpec { Title = "Bad", Width = 800, Height = 600 };

            var origErr = Console.Error;
            using var duringFailure = new StringWriter();
            Console.SetError(duringFailure);
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    ReactorApp.OpenAndAnnounce(sized, () => throw new InvalidOperationException("rejected")));
                Assert.Empty(duringFailure.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }

            using var duringSuccess = new StringWriter();
            Console.SetError(duringSuccess);
            try
            {
                // Latch must still be unspent, so the next window that opens reports.
                ReactorApp.EmitDipBehaviorChangeNoticeOnce(sized.Width, sized.Height);
                Assert.Contains("[reactor]", duringSuccess.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        // The open callback is the only thing that may validate. WindowSpec.Validate
        // is not side-effect free — it maintains edge-triggered warning state — so a
        // second call inside OpenAndAnnounce would double-count those warnings and
        // defeat the edge trigger. This asserts the seam calls the opener exactly
        // once and validates zero times itself.
        [Fact]
        public void OpenAndAnnounce_DoesNotValidateSpecItself()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var before = WindowSpec.NoDragAffordanceWarningCountForTests;
            try
            {
                // A spec whose combination trips the edge-triggered warning.
                var suspicious = new WindowSpec
                {
                    Title = "No drag",
                    Width = 320,
                    Height = 240,
                    Style = WindowStyle.None,
                    IsMovableByBackground = false,
                };

                var opens = 0;
                var origErr = Console.Error;
                using var sw = new StringWriter();
                Console.SetError(sw);
                try
                {
                    // Stand in for the real opener, which is what validates.
                    ReactorApp.OpenAndAnnounce(suspicious, () => { opens++; suspicious.Validate(); return null!; });
                }
                finally
                {
                    Console.SetError(origErr);
                }

                Assert.Equal(1, opens);
                // Exactly one validation — the opener's. A seam that validated too
                // would leave 2 here and break the edge trigger the warning relies on.
                Assert.Equal(before + 1, WindowSpec.NoDragAffordanceWarningCountForTests);
            }
            finally
            {
                WindowSpec.NoDragAffordanceWarningCountForTests = before;
            }
        }

        // Positive control: the seam does announce after a successful open, so the
        // assertions above cannot pass merely because the emit never fires.
        [Fact]
        public void OpenAndAnnounce_SuccessfulOpen_Announces()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.OpenAndAnnounce(
                    new WindowSpec { Title = "T", Width = 640, Height = 480 },
                    () => null!);
                Assert.Contains("[reactor]", sw.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        // A window that declares no size still must not consume the latch.
        [Fact]
        public void OpenAndAnnounce_UnsizedSpec_IsSilentAndLeavesLatch()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.OpenAndAnnounce(new WindowSpec { Title = "T" }, () => null!);
                Assert.Empty(sw.ToString());

                ReactorApp.OpenAndAnnounce(new WindowSpec { Title = "T2", Width = 800 }, () => null!);
                Assert.Contains("[reactor]", sw.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        // Half-specified sizes are a supported shape (spec 036 §12.2a): the
        // declared axis applies and the other takes the OS extent. That is still
        // an explicit size, so it must notify.
        [Theory]
        [InlineData(640d, null)]
        [InlineData(null, 480d)]
        public void EmitDipBehaviorChangeNoticeOnce_SingleAxis_Notifies(double? width, double? height)
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.EmitDipBehaviorChangeNoticeOnce(width, height);
                Assert.Contains("[reactor]", sw.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }
    }
}

// ── Test-only IXamlMetadataProvider fakes ────────────────────────────────
//
// These exist solely so the assembly-scanning path in
// FindXamlMetadataProviderInAssembly has something concrete to find when
// pointed at this test assembly. They never participate in real XAML markup.

internal sealed partial class FakeXamlMetadataProvider : IXamlMetadataProvider
{
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}

internal sealed partial class AnotherFakeXamlMetadataProvider : IXamlMetadataProvider
{
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}

// Negative cases: these should be skipped by the scanner. They exist to
// raise confidence that FindXamlMetadataProviderInAssembly's filter
// (skip abstract, skip interface, skip no-default-ctor, swallow throwing
// ctor) doesn't return them.

internal abstract partial class AbstractXamlMetadataProviderShouldBeSkipped : IXamlMetadataProvider
{
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}

internal sealed partial class NoDefaultCtorXamlMetadataProviderShouldBeSkipped : IXamlMetadataProvider
{
    public NoDefaultCtorXamlMetadataProviderShouldBeSkipped(int _) { }
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}

internal sealed partial class ThrowingCtorXamlMetadataProviderShouldBeSwallowed : IXamlMetadataProvider
{
    public ThrowingCtorXamlMetadataProviderShouldBeSwallowed()
    {
        throw new InvalidOperationException("intentional ctor failure for scanner test");
    }
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}
