using System.IO;
using System.Runtime.CompilerServices;
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
        var before = ExternalTargetDecoratorHandler.CreatedCount;
        ControlRegistry.RegisterDecorator<ExternalTargetDecoratorElement>(
            static () => new ExternalTargetDecoratorHandler());

        var target = new TextBlockElement("target") with { CallSite = new SourceLocation("Target.cs", 7) };
        var decorator = new ExternalTargetDecoratorElement(target) with { CallSite = new SourceLocation("Decorator.cs", 11) };

        Assert.Same(target, ReactorSourceMap.DecoratorTarget(decorator));
        var afterFirstLookup = ExternalTargetDecoratorHandler.CreatedCount;
        Assert.Same(target, ReactorSourceMap.DecoratorTarget(decorator));
        Assert.Equal(afterFirstLookup, ExternalTargetDecoratorHandler.CreatedCount);
        Assert.InRange(afterFirstLookup - before, 0, 1);
    }

    [Fact]
    public void EveryDecoratorRegistrationEntryPointIsAccountedForByTheFastPathLatch()
    {
        // DecoratorTarget short-circuits the registry entirely unless a decorator has
        // been registered, because it runs on the reconciler's shallow-skip path for
        // every callback-free element. That gate is only safe while EVERY decorator
        // registration path latches ControlRegistry.HasDecoratorRegistrations —
        // RegisterDecoratorForDerivedTypes did not at first, which would have made a
        // base-derived decorator's source attribution silently vanish.
        //
        // A behavioural test cannot guard this: the latch is a process-global one-way
        // flag, so any earlier decorator registration in the run leaves it true and the
        // assertion passes whether or not the path under test sets it (verified — the
        // obvious behavioural version stayed green with the latch deleted). What CAN
        // fail is drift in the set of entry points, so pin that instead: adding a new
        // registration path breaks this test and forces a decision about the latch.
        var entryPoints = typeof(ControlRegistry)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("RegisterDecorator", StringComparison.Ordinal))
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "RegisterDecorator", "RegisterDecoratorForDerivedTypes" },
            entryPoints);
    }

    [Fact]
    public void BaseDerivedRegisteredDecoratorResolvesItsSourceTarget()
    {
        // Coverage for the base-derived path itself. Not a guard for the latch (see
        // above for why that cannot be tested behaviourally) — this asserts the
        // registration actually resolves a target through the inheritance chain.
        ControlRegistry.RegisterDecoratorForDerivedTypes<BaseDerivedDecoratorElement>(
            static () => new BaseDerivedDecoratorHandler());

        var target = new TextBlockElement("target") with { CallSite = new SourceLocation("Target.cs", 21) };
        var decorator = new DerivedDecoratorElement(target) with { CallSite = new SourceLocation("Decorator.cs", 22) };

        Assert.Same(target, ReactorSourceMap.DecoratorTarget(decorator));
    }

    [Fact]
    public void TheDecoratorLatchIsOnlySetByDecoratorRegistrationPaths()
    {
        // NOTE: no [CallerFilePath] parameter — an xUnit [Fact] with parameters is not
        // discovered at all, so the first version of this test silently never ran.
        //
        // The latch must be true ONLY if a decorator was registered. Setting it from
        // Register<TElement,TControl> — which every built-in factory reaches — makes it
        // true in every app and silently defeats the fast path, so DecoratorTarget goes
        // back to doing dictionary lookups (and a first-touch handler construction) on
        // every shallow skip. That regression is invisible to behavioural tests: results
        // stay correct, only the cost changes, and the flag is a process-global one-way
        // latch so its value cannot be asserted in isolation mid-suite.
        //
        // So this reads the source and checks WHERE the assignment appears. A bulk edit
        // that catches the neighbouring ordinary-control registration (exactly how this
        // regressed once) fails here.
        var testDir = Path.GetDirectoryName(ThisFilePath())!;
        var registry = Path.GetFullPath(
            Path.Combine(testDir, @"..\..\src\Reactor\Core\V1Protocol\ControlRegistry.cs"));

        var lines = File.ReadAllLines(registry);

        var owningMethods = lines
            .Select((line, i) => (line, i))
            .Where(x => x.line.Contains("s_hasDecoratorRegistrations = true", StringComparison.Ordinal))
            .Select(x => lines.Take(x.i)
                .Last(l => l.Contains("static void Register", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(owningMethods);
        Assert.All(owningMethods, m =>
            Assert.Contains("RegisterDecorator", m, StringComparison.Ordinal));
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    /// <summary>
    /// A decorator whose handler is registered but deliberately names no source target.
    /// </summary>
    private sealed record NullTargetDecoratorElement(Element Target) : Element;

    private sealed record UnregisteredProbeElement : Element;

    private sealed class NullTargetDecoratorHandler : IDecoratorElementHandler<NullTargetDecoratorElement>
    {
        public UIElement Mount(MountContext ctx, NullTargetDecoratorElement element)
            => throw new NotSupportedException();

        public UIElement Update(UpdateContext ctx, NullTargetDecoratorElement oldEl, NullTargetDecoratorElement newEl, UIElement control)
            => throw new NotSupportedException();

        public V1UnmountDisposition Unmount(UnmountContext ctx, NullTargetDecoratorElement? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;

        // Deliberately null: "I am registered and I name no target."
        public Element? GetSourceTarget(NullTargetDecoratorElement element) => null;
    }

    [Fact]
    public void ARegistrationThatNamesNoTargetIsDistinguishableFromNoRegistration()
    {
        // The tri-state that lets a registered override beat the hard-coded built-in
        // unwrap. Collapsing these two onto a plain Element? is what made a registration
        // that deliberately names no target look identical to "nothing is registered",
        // so ReactorSourceMap fell through and unwrapped to Target anyway — reporting
        // the target factory's line instead of the factory that created the control.
        //
        // Asserted at the registry contract rather than end-to-end through
        // DecoratorTarget: exercising the built-in path would mean hijacking the
        // framework's own FlyoutElement registration, and a custom stand-in element is
        // not matched by the built-in switch, so that version of the test passes whether
        // or not the tri-state exists (verified — it did, which is why it is not here).
        ControlRegistry.RegisterDecorator<NullTargetDecoratorElement>(
            static () => new NullTargetDecoratorHandler());

        var registered = ControlRegistry.TryGetSourceTarget(
            new NullTargetDecoratorElement(new TextBlockElement("t")), out var namedTarget);

        Assert.True(registered);
        Assert.Null(namedTarget);

        // Negative control: an element type with no registration at all reports false,
        // so "true" above is not simply what this method always returns.
        Assert.False(ControlRegistry.TryGetSourceTarget(
            new UnregisteredProbeElement(), out _));
    }

    [Fact]
    public void UnregisteredBuiltInDecoratorStillFallsBackToItsTarget()
    {
        // The built-in fallback must keep working, so the change above cannot pass by
        // having disabled unwrapping altogether.
        var target = new TextBlockElement("t") with { CallSite = new SourceLocation("Target.cs", 41) };
        var flyout = new FlyoutElement(target, new TextBlockElement("c"));

        Assert.Same(target, ReactorSourceMap.DecoratorTarget(flyout));
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
        public static int CreatedCount { get; set; }

        public ExternalTargetDecoratorHandler() => CreatedCount++;

        public UIElement Mount(MountContext ctx, ExternalTargetDecoratorElement element)
            => throw new NotSupportedException();

        public UIElement Update(UpdateContext ctx, ExternalTargetDecoratorElement oldEl, ExternalTargetDecoratorElement newEl, UIElement control)
            => throw new NotSupportedException();

        public V1UnmountDisposition Unmount(UnmountContext ctx, ExternalTargetDecoratorElement? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;

        public Element? GetSourceTarget(ExternalTargetDecoratorElement element) => element.Target;
    }

    private record BaseDerivedDecoratorElement(Element Target) : Element;

    private sealed record DerivedDecoratorElement(Element Target) : BaseDerivedDecoratorElement(Target);

    private sealed class BaseDerivedDecoratorHandler : IDecoratorElementHandler<BaseDerivedDecoratorElement>
    {
        public UIElement Mount(MountContext ctx, BaseDerivedDecoratorElement element)
            => throw new NotSupportedException();

        public UIElement Update(UpdateContext ctx, BaseDerivedDecoratorElement oldEl, BaseDerivedDecoratorElement newEl, UIElement control)
            => throw new NotSupportedException();

        public V1UnmountDisposition Unmount(UnmountContext ctx, BaseDerivedDecoratorElement? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;

        public Element? GetSourceTarget(BaseDerivedDecoratorElement element) => element.Target;
    }
}

#pragma warning restore IL2026, IL2070, IL2072, IL2075
