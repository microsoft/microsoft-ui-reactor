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
                ReactorApp.EmitDipBehaviorChangeNoticeOnce();
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
            ReactorApp.EmitDipBehaviorChangeNoticeOnce(); // first call latches

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
    }
}

// ── Test-only IXamlMetadataProvider fakes ────────────────────────────────
//
// These exist solely so the assembly-scanning path in
// FindXamlMetadataProviderInAssembly has something concrete to find when
// pointed at this test assembly. They never participate in real XAML markup.

internal sealed class FakeXamlMetadataProvider : IXamlMetadataProvider
{
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}

internal sealed class AnotherFakeXamlMetadataProvider : IXamlMetadataProvider
{
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}

// Negative cases: these should be skipped by the scanner. They exist to
// raise confidence that FindXamlMetadataProviderInAssembly's filter
// (skip abstract, skip interface, skip no-default-ctor, swallow throwing
// ctor) doesn't return them.

internal abstract class AbstractXamlMetadataProviderShouldBeSkipped : IXamlMetadataProvider
{
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}

internal sealed class NoDefaultCtorXamlMetadataProviderShouldBeSkipped : IXamlMetadataProvider
{
    public NoDefaultCtorXamlMetadataProviderShouldBeSkipped(int _) { }
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}

internal sealed class ThrowingCtorXamlMetadataProviderShouldBeSwallowed : IXamlMetadataProvider
{
    public ThrowingCtorXamlMetadataProviderShouldBeSwallowed()
    {
        throw new InvalidOperationException("intentional ctor failure for scanner test");
    }
    public IXamlType GetXamlType(global::System.Type type) => null!;
    public IXamlType GetXamlType(string fullName) => null!;
    public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
}
