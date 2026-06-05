using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Compile.Analyzer.Tests;

/// <summary>
/// Fixtures for REACTOR0050, the descriptor analyzer that warns when an
/// Optional&lt;T&gt; OneWay entry has no dependency property to clear.
/// </summary>
public class REACTOR0050Tests
{
    private const string Stubs = @"
using System;
using System.Collections.Generic;

namespace Microsoft.UI.Xaml
{
    public class FrameworkElement { }
    public sealed class DependencyProperty { }
}

namespace Microsoft.UI.Xaml.Media
{
    public class Brush { }
}

namespace Microsoft.UI.Reactor
{
    public readonly struct Optional<T>
    {
        public bool HasValue { get; }
        public T Value { get; }
        public static Optional<T> Unset => default;
        public static Optional<T> Of(T value) => new Optional<T>();
    }
}

namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element { }
}

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class NoClearValueAttribute : Attribute { }

    public sealed class ControlDescriptor<TElement, TControl>
        where TElement : Element
        where TControl : Microsoft.UI.Xaml.FrameworkElement, new()
    {
        public ControlDescriptor<TElement, TControl> OneWay<TValue>(
            Func<TElement, TValue> get,
            Action<TControl, TValue> set,
            IEqualityComparer<TValue>? comparer = null) => this;

        public ControlDescriptor<TElement, TControl> OneWay<TValue>(
            Func<TElement, Microsoft.UI.Reactor.Optional<TValue>> get,
            Action<TControl, TValue> set,
            Microsoft.UI.Xaml.DependencyProperty dp,
            IEqualityComparer<TValue>? comparer = null) => this;

        public ControlDescriptor<TElement, TControl> OneWayConditional<TValue>(
            Func<TElement, TValue> get,
            Action<TControl, TValue> set,
            Func<TElement, bool> shouldWrite,
            IEqualityComparer<TValue>? comparer = null) => this;
    }
}

namespace TestApp
{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Media;

    public sealed record WidgetElement : Element
    {
        public Optional<Brush> OptionalBrush { get; init; }
        public Brush PlainBrush { get; init; } = new Brush();
    }

    public sealed class WidgetControl : FrameworkElement
    {
        public static readonly DependencyProperty BrushProperty = new DependencyProperty();
        public Brush Brush { get; set; } = new Brush();
        public Optional<Brush> OptionalBrush { get; set; }
    }
}
";

    /// <summary>
    /// Optional getter plus plain OneWay means Unset can only skip the write;
    /// the analyzer asks descriptor authors to provide the dp channel.
    /// </summary>
    [Fact]
    public async Task Fires_For_Optional_OneWay_Without_Dp()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

    public static class Descriptors
    {
        public static readonly ControlDescriptor<WidgetElement, WidgetControl> Descriptor =
            {|REACTOR0050:new ControlDescriptor<WidgetElement, WidgetControl>()
                .OneWay(get: e => e.OptionalBrush, set: (c, v) => c.OptionalBrush = v)|};
    }
}
";

        await RunAsync(source);
    }

    /// <summary>
    /// Supplying dp selects the ClearValue-capable overload and is the preferred
    /// fix for dependency-property-backed setters.
    /// </summary>
    [Fact]
    public async Task Does_Not_Fire_When_Dp_Is_Supplied()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

    public static class Descriptors
    {
        public static readonly ControlDescriptor<WidgetElement, WidgetControl> Descriptor =
            new ControlDescriptor<WidgetElement, WidgetControl>()
                .OneWay(get: e => e.OptionalBrush, set: (c, v) => c.Brush = v, dp: WidgetControl.BrushProperty);
    }
}
";

        await RunAsync(source);
    }

    /// <summary>
    /// OneWayConditional is an explicit skip-write shape, so Optional getters
    /// there are not diagnosed.
    /// </summary>
    [Fact]
    public async Task Does_Not_Fire_For_OneWayConditional()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

    public static class Descriptors
    {
        public static readonly ControlDescriptor<WidgetElement, WidgetControl> Descriptor =
            new ControlDescriptor<WidgetElement, WidgetControl>()
                .OneWayConditional(get: e => e.OptionalBrush, set: (c, v) => c.OptionalBrush = v, shouldWrite: e => e.OptionalBrush.HasValue);
    }
}
";

        await RunAsync(source);
    }

    /// <summary>
    /// Plain value getters keep the existing OneWay semantics and should not
    /// participate in the Optional ClearValue rule.
    /// </summary>
    [Fact]
    public async Task Does_Not_Fire_For_Plain_T_Getter()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

    public static class Descriptors
    {
        public static readonly ControlDescriptor<WidgetElement, WidgetControl> Descriptor =
            new ControlDescriptor<WidgetElement, WidgetControl>()
                .OneWay(get: e => e.PlainBrush, set: (c, v) => c.Brush = v);
    }
}
";

        await RunAsync(source);
    }

    /// <summary>
    /// Descriptor authors can acknowledge a no-DP-backed control once at the
    /// descriptor field.
    /// </summary>
    [Fact]
    public async Task Suppresses_With_NoClearValue_Attribute_On_Field()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

    public static class Descriptors
    {
        [NoClearValue]
        public static readonly ControlDescriptor<WidgetElement, WidgetControl> Descriptor =
            new ControlDescriptor<WidgetElement, WidgetControl>()
                .OneWay(get: e => e.OptionalBrush, set: (c, v) => c.OptionalBrush = v);
    }
}
";

        await RunAsync(source);
    }

    /// <summary>
    /// The line-comment pragma is a narrow suppression for individual entries.
    /// </summary>
    [Fact]
    public async Task Suppresses_With_Intentional_Skip_Pragma()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

    public static class Descriptors
    {
        public static readonly ControlDescriptor<WidgetElement, WidgetControl> Descriptor =
            new ControlDescriptor<WidgetElement, WidgetControl>()
                // REACTOR0050: intentional skip — no DP backing
                .OneWay(get: e => e.OptionalBrush, set: (c, v) => c.OptionalBrush = v);
    }
}
";

        await RunAsync(source);
    }

    /// <summary>
    /// The analyzer ships one public warning descriptor under REACTOR0050.
    /// </summary>
    [Fact]
    public void DiagnosticDescriptor_Is_Registered()
    {
        var analyzer = new OneWayClearValueAnalyzer();
        var descriptors = analyzer.SupportedDiagnostics;
        Assert.Single(descriptors);
        Assert.Equal("REACTOR0050", descriptors[0].Id);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors[0].DefaultSeverity);
        Assert.Equal("Reactor.Descriptor", descriptors[0].Category);
    }

    private static async Task RunAsync(string source)
    {
        await new CSharpAnalyzerTest<OneWayClearValueAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            CompilerDiagnostics = CompilerDiagnostics.None,
        }.RunAsync();
    }
}
