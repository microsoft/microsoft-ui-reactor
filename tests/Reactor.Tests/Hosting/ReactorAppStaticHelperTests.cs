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

        // OpenWindow validates its spec before emitting, so a spec that is about
        // to be rejected cannot burn the latch and silence the next window that
        // legitimately should report. This drives ValidateAndAnnounceSpec — the
        // actual code OpenWindow runs — rather than re-implementing the order in
        // the test, so reverting the production ordering reddens it.
        [Fact]
        public void ValidateAndAnnounceSpec_InvalidSpec_ThrowsWithoutConsumingLatch()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();

            var origErr = Console.Error;
            using var duringInvalid = new StringWriter();
            Console.SetError(duringInvalid);
            try
            {
                // Width = -1 fails WindowSpec.Validate. Emitting before that check
                // would both print here and latch.
                Assert.ThrowsAny<ArgumentException>(() =>
                    ReactorApp.ValidateAndAnnounceSpec(new WindowSpec { Title = "Bad", Width = -1 }));
                Assert.Empty(duringInvalid.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }

            using var duringValid = new StringWriter();
            Console.SetError(duringValid);
            try
            {
                // The latch must still be unspent, so the next good window reports.
                ReactorApp.ValidateAndAnnounceSpec(new WindowSpec { Title = "Good", Width = 800, Height = 600 });
                Assert.Contains("[reactor]", duringValid.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        // Positive control: the seam does announce a valid sized spec, so the
        // assertion above cannot pass merely because the emit never fires.
        [Fact]
        public void ValidateAndAnnounceSpec_ValidSizedSpec_Announces()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.ValidateAndAnnounceSpec(new WindowSpec { Title = "T", Width = 640, Height = 480 });
                Assert.Contains("[reactor]", sw.ToString());
            }
            finally
            {
                Console.SetError(origErr);
            }
        }

        // A spec that declares no size still must not consume the latch.
        [Fact]
        public void ValidateAndAnnounceSpec_UnsizedSpec_IsSilentAndLeavesLatch()
        {
            ReactorApp.ResetDipBehaviorChangeNoticeForTests();
            var origErr = Console.Error;
            using var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                ReactorApp.ValidateAndAnnounceSpec(new WindowSpec { Title = "T" });
                Assert.Empty(sw.ToString());

                ReactorApp.ValidateAndAnnounceSpec(new WindowSpec { Title = "T2", Width = 800 });
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
