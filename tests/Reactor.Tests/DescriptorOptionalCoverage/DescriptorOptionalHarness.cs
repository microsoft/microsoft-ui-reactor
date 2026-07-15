using System;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

internal static class DescriptorOptionalHarness
{
    // NumberBox is intentionally NOT covered by a headless DescriptorOptionalCoverage
    // test: NumberBoxElement.Descriptor's static initializer evaluates
    // `WinUI.NumberBox.TextProperty` (for the .Immediate observe entry),
    // which requires a live WinUI XAML runtime and throws COMException in
    // the headless Reactor.Tests process. Real runtime coverage for the
    // NumberBox Optional<double> gate lives in
    // tests/Reactor.AppTests.Host/SelfTest/Fixtures/ControlledOptionalNumericFamilyFixture.cs
    // (NumberBoxScenario).
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only harness: reflects a known property/field on the concrete descriptor/entry objects the test constructs (rooted); behaviour-neutral (no DAM contract that could prune other members — avoiding the narrowing regression noted in issue #70). JIT only.")]
    public static void AssertOptionalGate<TValue>(
        object descriptor,
        object unset,
        object value,
        object sameValue,
        object unsetAgain)
    {
        var properties = Assert.IsAssignableFrom<global::System.Collections.IEnumerable>(
            descriptor.GetType().GetProperty("Properties")!.GetValue(descriptor));
        var entries = properties.Cast<object>().Where(p =>
            p.GetType().Name.StartsWith("ControlledPropEntry", StringComparison.Ordinal)
            || p.GetType().Name.StartsWith("HandCodedControlledPropEntry", StringComparison.Ordinal));
        var entry = Assert.Single(entries);
        var field = entry.GetType().GetField("_get", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var get = Assert.IsAssignableFrom<Delegate>(field.GetValue(entry));

        Optional<TValue> Read(object element) => (Optional<TValue>)get.DynamicInvoke(element)!;

        Assert.False(Read(unset).HasValue);
        Assert.False(Read(unsetAgain).HasValue);
        Assert.True(Read(value).HasValue);
        Assert.True(Read(sameValue).HasValue);
        Assert.Equal(Read(value).Value, Read(sameValue).Value);
    }
}
