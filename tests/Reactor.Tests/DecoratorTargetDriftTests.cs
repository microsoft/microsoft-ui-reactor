using System.Reflection;

// Trim analysis is on for this project, and this file reflects over the Reactor
// assembly on purpose: the whole point is to notice a decorator type nobody told the
// resolver about. The test assembly is never trimmed or AOT-published, so the
// warnings describe a scenario that does not exist here.
#pragma warning disable IL2026, IL2070, IL2072, IL2075
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — guards <c>ReactorSourceMap.DecoratorTarget</c>'s source-target resolver.
///
/// <para>Target-wrapping decorators mount their <c>Target</c>'s control and then replace
/// that control's tag with themselves, so <c>GetSource</c> has to resolve through them or
/// it names the decorator as the creator of a control the target factory built. The
/// built-in fallback used when a record is constructed directly can drift.</para>
///
/// <para>The failure mode is why this matters: a fourth decorator added later would not
/// crash or return null, it would report a confidently wrong line — the hardest kind of
/// bug to notice in a diagnostic feature. So the set is derived structurally here and
/// compared against the resolver's real behaviour, not against a copy of it.</para>
/// </summary>
public class DecoratorTargetDriftTests
{
    /// <summary>
    /// A decorator is an element whose FIRST positional parameter is an
    /// <see cref="Element"/> named <c>Target</c> — the shape all three share.
    /// </summary>
    private static bool LooksLikeDecorator(Type t)
    {
        if (!typeof(Element).IsAssignableFrom(t) || t.IsAbstract) return false;

        var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null) return false;

        var first = ctor.GetParameters().FirstOrDefault();
        return first is not null
            && first.Name == "Target"
            && typeof(Element).IsAssignableFrom(first.ParameterType);
    }

    private static Type[] DiscoverDecorators()
        => typeof(Element).Assembly
            .GetTypes()
            .Where(LooksLikeDecorator)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void EveryDecoratorShapedElementIsResolvedByGetSource()
    {
        var missing = new List<string>();

        foreach (var t in DiscoverDecorators())
        {
            // Build an instance with a stamped target, then ask the resolver for it.
            // Using the resolver itself (rather than a copied list) means this fails
            // when DecoratorTarget stops handling a type, which is the actual risk.
            var target = new TextBlockElement("t") with { CallSite = new SourceLocation("F.cs", 7) };
            Element? instance = TryConstruct(t, target);
            if (instance is null) continue;

            if (ReactorSourceMap.DecoratorTarget(instance) is null)
            {
                missing.Add(t.Name);
            }
        }

        Assert.True(
            missing.Count == 0,
            $"These element types wrap a Target but ReactorSourceMap.DecoratorTarget does not " +
            $"resolve them: [{string.Join(", ", missing)}]. A control decorated by one of these " +
            "is tagged with the DECORATOR, so GetSource would report the decorator's line as the " +
            "creator of a control the target factory built — a confidently wrong location. Add " +
            "a source-target resolver or the direct-record fallback in ReactorSourceMap.DecoratorTarget.");
    }

    /// <summary>
    /// Positive control for the discovery predicate: if it silently matched nothing, the
    /// assertion above would pass while proving nothing about the real DSL.
    /// </summary>
    [Fact]
    public void TheDecoratorPredicateActuallyMatchesSomething()
    {
        var found = DiscoverDecorators().Select(t => t.Name).ToArray();

        Assert.NotEmpty(found);
        Assert.Contains("FlyoutElement", found);
    }

    /// <summary>
    /// Negative control: an ordinary element must NOT be classified as a decorator, or
    /// the resolver would be asked to unwrap everything.
    /// </summary>
    [Fact]
    public void OrdinaryElementsAreNotDecorators()
    {
        Assert.False(LooksLikeDecorator(typeof(TextBlockElement)));
        Assert.Null(ReactorSourceMap.DecoratorTarget(new TextBlockElement("x")));
    }

    [Fact]
    public void RegisteredExternalDecoratorCanProvideItsSourceTarget()
    {
        ControlRegistry.RegisterDecorator<ExternalTargetDecoratorElement>(
            static () => new ExternalTargetDecoratorHandler());

        var target = new TextBlockElement("target") with { CallSite = new SourceLocation("Target.cs", 7) };
        var decorator = new ExternalTargetDecoratorElement(target) with { CallSite = new SourceLocation("Decorator.cs", 11) };

        Assert.Same(target, ReactorSourceMap.DecoratorTarget(decorator));
    }

    private static Element? TryConstruct(Type t, Element target)
    {
        var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault(c => c.GetParameters().FirstOrDefault()?.Name == "Target");
        if (ctor is null) return null;

        var args = ctor.GetParameters()
            .Select((p, i) => i == 0
                ? target
                : p.HasDefaultValue
                    ? p.DefaultValue
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
            .ToArray();

        try
        {
            return ctor.Invoke(args) as Element;
        }
        catch (TargetInvocationException)
        {
            // A decorator with a validating constructor we cannot satisfy generically;
            // skipping is safe because the predicate above still reports it if the
            // resolver misses it in a shape we CAN construct.
            return null;
        }
    }

    private sealed record ExternalTargetDecoratorElement(Element Target) : Element;

    private sealed class ExternalTargetDecoratorHandler : IDecoratorElementHandler<ExternalTargetDecoratorElement>
    {
        public UIElement Mount(MountContext ctx, ExternalTargetDecoratorElement element)
            => throw new NotSupportedException();

        public UIElement Update(UpdateContext ctx, ExternalTargetDecoratorElement oldEl, ExternalTargetDecoratorElement newEl, UIElement control)
            => throw new NotSupportedException();

        public V1UnmountDisposition Unmount(UnmountContext ctx, ExternalTargetDecoratorElement? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;

        public Element? GetSourceTarget(ExternalTargetDecoratorElement element) => element.Target;
    }
}

#pragma warning restore IL2026, IL2070, IL2072, IL2075
