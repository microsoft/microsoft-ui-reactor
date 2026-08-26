using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Analyzers;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Reactor's element surface, read from the real assemblies as metadata.
/// </summary>
/// <remarks>
/// <para>
/// Two maps, both taken from the same signals <see cref="NoOpModifierAnalyzer"/> reads at compile
/// time: a factory's declared return type, and the control named by an element's
/// <c>Set(this TElement, Action&lt;TControl&gt;)</c> overload. Nothing is derived from a parallel
/// list, so widening a gate or re-pointing a factory moves this type with it.
/// </para>
/// <para>
/// Reflection is metadata-only — no WinUI object is constructed — which is what makes it legal in
/// the headless <c>Reactor.Tests</c> host, where instantiating any <c>Microsoft.UI.Xaml</c> type
/// throws <c>COMException</c>.
/// </para>
/// </remarks>
internal sealed class ReactorSurface
{
    private readonly Dictionary<string, Type?> _factories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<Type>> _factoriesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, HashSet<Type>> _setControls = new();
    private readonly Dictionary<string, HashSet<Type>> _modifierArguments = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Modifier, string Parameter), HashSet<Type>> _modifierArgumentsByParameter = new();
    private readonly Dictionary<string, Type?> _typeChangingModifiers = new(StringComparer.Ordinal);

    [UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Test-only surface reader: enumerates the Reactor assemblies' types and factory methods by design. This host is never trimmed; behaviour-neutral.")]
    [UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "Test-only surface reader: reflects the public static methods of Factories/ElementExtensions, resolved by name from the Reactor assembly. Intentional and JIT-only; behaviour-neutral.")]
    private ReactorSurface()
    {
        ElementType = typeof(Microsoft.UI.Reactor.Core.Element);
        var reactor = ElementType.Assembly;

        var elementExtensions = reactor.GetType("Microsoft.UI.Reactor.ElementExtensions")
            ?? throw new InvalidOperationException("Microsoft.UI.Reactor.ElementExtensions not found.");

        ElementExtensionsType = elementExtensions;

        foreach (var parameters in elementExtensions
                     .GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.Name == "Set")
                     .Select(m => m.GetParameters())
                     .Where(p => p.Length == 2
                                 && p[1].ParameterType.IsGenericType
                                 && p[1].ParameterType.GetGenericTypeDefinition() == typeof(Action<>)))
        {
            var receiver = parameters[0].ParameterType;
            if (receiver.IsGenericParameter)
                continue;   // Set<T>(this T, …) says nothing about which control T mounts.

            if (receiver.IsGenericType)
                receiver = receiver.GetGenericTypeDefinition();

            if (!_setControls.TryGetValue(receiver, out var controls))
                _setControls[receiver] = controls = new HashSet<Type>();

            controls.Add(parameters[1].ParameterType.GetGenericArguments()[0]);
        }

        // Single-argument generic modifiers, keyed by name. Feeds DefaultIsProvablyNull: `default`
        // is target-typed, so only the real parameter types can say whether it means null.
        foreach (var method in elementExtensions.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.IsGenericMethodDefinition)
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length != 2 || !parameters[0].ParameterType.IsGenericParameter)
                continue;   // not `M<T>(this T el, TArg value)`

            var argument = parameters[1].ParameterType;
            if (argument.IsGenericParameter)
                continue;   // unconstrained: says nothing about reference-ness.

            if (!_modifierArguments.TryGetValue(method.Name, out var types))
                _modifierArguments[method.Name] = types = new HashSet<Type>();

            types.Add(argument);

            // Indexed by parameter name too, because a named argument picks the overload:
            // `.Background(brush: default)` binds only to the Brush one, so judging `default`
            // against the whole ambiguous set answers a question the call did not ask.
            var key = (method.Name, parameters[1].Name ?? string.Empty);
            if (!_modifierArgumentsByParameter.TryGetValue(key, out var named))
                _modifierArgumentsByParameter[key] = named = new HashSet<Type>();

            named.Add(argument);
        }

        // Type-changing modifiers: `M<T>(this T el, …)` that returns a concrete element rather than
        // T. The chain's element is decided by the last of these, not by the head factory.
        foreach (var method in elementExtensions.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.IsGenericMethodDefinition)
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 0 || !parameters[0].ParameterType.IsGenericParameter)
                continue;

            var returned = method.ReturnType;
            if (returned.IsGenericParameter
                || returned.ContainsGenericParameters
                || !ElementType.IsAssignableFrom(returned))
                continue;   // type-preserving (returns T) or not an element at all.

            MergeTypeChange(_typeChangingModifiers, method.Name, returned);
        }

        var factories = reactor.GetType("Microsoft.UI.Reactor.Factories")
            ?? throw new InvalidOperationException("Microsoft.UI.Reactor.Factories not found.");
        FactoriesType = factories;

        foreach (var method in factories.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var returned = method.ReturnType;
            if (returned.IsGenericParameter || !ElementType.IsAssignableFrom(returned))
                continue;

            // A generic factory returns an open constructed type — `ListView<T>` gives
            // `TemplatedListViewElement<T>`. Keyed by its generic type definition so the map has an
            // entry at all: dropping these made `ListView<Todo>(...)`, `LazyVStack<T>(...)` and
            // friends invisible to the walker, and because a skipped chain neither reports nor
            // counts, the aggregate floor stayed green over the blind spot. Those factories appear
            // in the shipped corpus.
            if (returned.ContainsGenericParameters)
                returned = returned.GetGenericTypeDefinition();

            var key = Key(method.Name, method.GetGenericArguments().Length);

            // An overload set that returns two different element types at the same arity cannot be
            // resolved from the call site, so the entry is poisoned to null rather than resolved to
            // whichever overload reflection happened to hand back first.
            if (_factories.TryGetValue(key, out var existing))
            {
                if (existing != returned)
                    _factories[key] = null;
            }
            else
            {
                _factories[key] = returned;
            }
        }

        // Name → the element types it can produce at any arity, for the inferred-generic fallback
        // in Element(...). Built after the loop so poisoned entries are already resolved to null.
        foreach (var (key, element) in _factories)
        {
            if (element is null)
                continue;

            var tick = key.IndexOf('`');
            var name = tick < 0 ? key : key[..tick];

            if (!_factoriesByName.TryGetValue(name, out var types))
                _factoriesByName[name] = types = new HashSet<Type>();

            types.Add(element);
        }
    }

    public static ReactorSurface Instance { get; } = new();

    public Type ElementType { get; }

    public Type ElementExtensionsType { get; }

    public Type FactoriesType { get; }

    /// <summary>
    /// Number of factory signatures that resolve to exactly one element type. A floor over this is
    /// what tells a reading of "no findings" apart from "the reflection stopped resolving".
    /// </summary>
    public int ResolvableFactoryCount => _factories.Count(pair => pair.Value is not null);

    /// <summary>
    /// The element type a DSL factory call produces, or <see langword="null"/> when the name is
    /// unknown or its overloads at that arity disagree.
    /// </summary>
    /// <param name="factoryName">Factory name as written, e.g. <c>ListView</c>.</param>
    /// <param name="typeArgumentCount">
    /// Number of type arguments at the call site — <c>0</c> for <c>Border(...)</c>, <c>1</c> for
    /// <c>ListView&lt;Todo&gt;(...)</c>.
    /// </param>
    /// <remarks>
    /// Keyed by arity as well as name because several factories are both:
    /// <c>ListView(...)</c> returns <c>ListViewElement</c> while <c>ListView&lt;T&gt;(...)</c>
    /// returns <c>TemplatedListViewElement&lt;T&gt;</c>. Keyed by name alone the two collide and the
    /// name is discarded as ambiguous, which silently excluded every such factory from the walker.
    /// The call site states its own arity, so nothing has to be guessed.
    /// </remarks>
    public Type? Element(string factoryName, int typeArgumentCount)
    {
        // A type-changing modifier is not a factory, but ResolveHead reports it in the same slot
        // because it decides the chain's element the same way.
        if (_typeChangingModifiers.TryGetValue(factoryName, out var changed))
            return changed;   // null when the name is poisoned by disagreeing overloads.

        if (_factories.TryGetValue(Key(factoryName, typeArgumentCount), out var exact))
            return exact;

        // An explicit `<T>` that matched nothing is simply unknown.
        if (typeArgumentCount != 0)
            return null;

        // No arity-0 entry, but the name may still be a generic factory whose T the compiler infers
        // — `LazyVStack(items, (x, _) => Row(x))` is written without `<T>` and arrives here as a
        // plain identifier. Resolve it only when every arity for the name agrees on one element
        // type; disagreement means the binding cannot be recovered without a semantic model, and
        // guessing there would attribute the chain to the wrong control.
        if (!_factoriesByName.TryGetValue(factoryName, out var candidates))
            return null;

        return candidates.Count == 1 ? candidates.Single() : null;
    }

    private static string Key(string factoryName, int typeArgumentCount) =>
        typeArgumentCount == 0 ? factoryName : factoryName + "`" + typeArgumentCount;

    /// <summary>
    /// The controls an element's <c>Set</c> overloads name, empty when it declares none.
    /// </summary>
    public IReadOnlyCollection<Type> SetControls(Type element)
    {
        var key = element.IsGenericType ? element.GetGenericTypeDefinition() : element;
        return _setControls.TryGetValue(key, out var controls) ? controls : Array.Empty<Type>();
    }

    /// <summary>
    /// The single control <c>ApplyModifiers</c> will be handed for this element, or
    /// <see langword="null"/> when that cannot be established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the <c>Set(this TElement, Action&lt;TControl&gt;)</c> overload, and <b>only</b>
    /// from there — no generator-attribute fallback. <c>NoOpModifierAnalyzer.TryGetMountedControl</c>
    /// resolves the same way and stays silent when an element declares no <c>Set</c> overload
    /// (pinned by its <c>Does_Not_Fire_For_An_Element_With_No_Set_Overload</c> test), because the
    /// generator attributes live in <c>Reactor.Wrappers.Abstractions</c> and do not flow to
    /// consumers.
    /// </para>
    /// <para>
    /// An earlier revision fell back to the attribute, which made this gate report on receivers the
    /// packaged analyzer cannot resolve — <c>SemanticElement</c> among them. A documentation gate
    /// stricter than the rule it enforces produces findings a reader cannot act on, so the two
    /// resolve identically or the claim "this mirrors REACTOR_MOD_003" is not true.
    /// </para>
    /// </remarks>
    public Type? MountedControl(Type element)
    {
        var fromSet = SetControls(element);
        return fromSet.Count == 1 ? fromSet.Single() : null;
    }

    /// <summary>
    /// True when <paramref name="modifier"/> resolves as a method on <paramref name="element"/> —
    /// either a type-specific overload or a generic one.
    /// </summary>
    /// <remarks>
    /// The analyzer offers a replacement only once it resolves as an invocable member on the
    /// receiver, which is why <c>LineElement</c> — having no <c>Fill</c> — falls through to
    /// <c>Stroke</c>. Naming a replacement that does not exist would hand the reader a remedy that
    /// does not compile, and would reject a Line counterexample that correctly documents
    /// <c>.Stroke(...)</c>.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "Test-only surface reader: reflects the public static methods of ElementExtensions, resolved by name from the Reactor assembly. Intentional and JIT-only; behaviour-neutral.")]
    public bool HasModifier(Type element, string modifier) =>
        ElementExtensionsType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == modifier)
            .Select(m => m.GetParameters())
            .Any(p => p.Length > 0
                      && (p[0].ParameterType.IsGenericParameter
                          || p[0].ParameterType.IsAssignableFrom(element)));

    /// <summary>
    /// The control named by an element's <c>[GenerateReactorWrapper]</c> /
    /// <c>[GenerateReactorDescriptor]</c> attribute, walking up the record hierarchy.
    /// </summary>
    /// <remarks>
    /// <c>GetCustomAttributesData</c>, not <c>GetCustomAttributes</c>: the former reads metadata
    /// without constructing the attribute, which matters because the constructor argument is a
    /// WinUI <c>Type</c> and the generator assembly is not always loadable here.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "Test-only contract guard: reads the generator attribute off an element type resolved from the Reactor assembly. Behaviour-neutral.")]
    public static Type? DeclaredControl(Type element)
    {
        for (var current = element; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetCustomAttributesData())
            {
                var name = attribute.AttributeType.Name;
                if (name is not ("GenerateReactorWrapperAttribute" or "GenerateReactorDescriptorAttribute")
                    || attribute.ConstructorArguments.Count < 1)
                    continue;

                if (attribute.ConstructorArguments[0].Value is Type control)
                    return control;
            }
        }

        return null;
    }

    /// <summary>
    /// The parameter types of every single-argument generic overload of a modifier on
    /// <c>ElementExtensions</c>, used to decide whether <c>default</c> at that position is
    /// provably <see langword="null"/>.
    /// </summary>
    public IReadOnlyCollection<Type> SingleArgumentModifierTypes(string modifier) =>
        _modifierArguments.TryGetValue(modifier, out var types) ? types : Array.Empty<Type>();

    /// <summary>
    /// True when a <c>default</c> at this modifier's single argument position must be
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="modifier">Modifier name, e.g. <c>Background</c>.</param>
    /// <param name="explicitTypeName">
    /// The <c>T</c> of <c>default(T)</c> as written, or <see langword="null"/> for a bare
    /// <c>default</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <c>default</c> is target-typed, so syntax alone says nothing: <c>default(Brush)</c> is null,
    /// <c>default(double)</c> is <c>0</c>, and <c>0</c> is a real write that <c>ApplyModifiers</c>
    /// really does drop on a Flex receiver. Exempting it would hide a true finding — a silent miss,
    /// which is the failure this gate is least able to detect in itself.
    /// </para>
    /// <para>
    /// With an explicit <c>T</c> the answer comes from <c>T</c>. Without one, every overload the
    /// call could bind to must be nullable, which is strictly conservative:
    /// <c>.Background(default)</c> is in fact ambiguous across
    /// <c>string</c>/<c>Brush</c>/<c>ThemeRef</c> and does not compile, so refusing to exempt it
    /// costs nothing. Unprovable never exempts.
    /// </para>
    /// </remarks>
    public bool DefaultIsProvablyNull(string modifier, string? explicitTypeName = null, string? parameterName = null)
    {
        // A named argument selects the overload, so narrow to it before deciding. Judging
        // `.Background(brush: default)` against the ambiguous string/Brush/ThemeRef set answered a
        // question the call did not ask and could refuse to exempt a sample the analyzer accepts —
        // this gate being stricter than the analyzer it mirrors is worse than the drift it catches.
        // An unrecognised name falls back to the full set rather than to an empty one.
        var types = parameterName is not null
                    && _modifierArgumentsByParameter.TryGetValue((modifier, parameterName), out var named)
            ? named
            : SingleArgumentModifierTypes(modifier);

        if (types.Count == 0)
            return false;

        if (explicitTypeName is null)
            return types.All(IsNullable);

        // `default(Microsoft.UI.Xaml.Media.Brush)`, `default(global::…Brush)` and `default(Brush?)`
        // all name the same type as `default(Brush)`. Comparing the written syntax verbatim found
        // no match for the qualified forms and refused to exempt them, making this gate stricter
        // than the analyzer it mirrors — a finding on a sample REACTOR_MOD_003 accepts.
        var simpleName = SimpleTypeName(explicitTypeName);

        // A value-type keyword settles it without any lookup, and covers the `default(double)` case
        // whose parameter type may not even be in this modifier's overload set.
        if (ValueTypeKeywords.Contains(simpleName))
            return false;

        if (simpleName is "string" or "object")
            return true;

        var matches = types.Where(t => string.Equals(t.Name, simpleName, StringComparison.Ordinal)).ToList();
        return matches.Count > 0 && matches.All(IsNullable);
    }

    /// <summary>
    /// Reduces written type syntax to the bare name reflection reports: strips <c>global::</c>,
    /// any namespace qualification, a trailing <c>?</c>, and generic arguments.
    /// </summary>
    private static string SimpleTypeName(string written)
    {
        var text = written.Trim().TrimEnd('?').Trim();

        var generic = text.IndexOf('<');
        if (generic >= 0)
            text = text[..generic];

        var lastDot = text.LastIndexOf('.');
        if (lastDot >= 0)
            text = text[(lastDot + 1)..];

        return text.Trim();
    }

    private static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static readonly HashSet<string> ValueTypeKeywords = new(StringComparer.Ordinal)
    {
        "int", "double", "float", "bool", "char", "byte", "sbyte",
        "short", "ushort", "uint", "long", "ulong", "decimal", "nint", "nuint",
    };

    /// <summary>
    /// Fluent modifiers that return a <em>different</em> element type than their receiver, mapped
    /// to that type. <c>Semantics</c> is the framework's only one today.
    /// </summary>
    /// <remarks>
    /// These break the assumption that a chain's element is decided by its head factory.
    /// <c>Border(child).Semantics(...)</c> is a <c>SemanticElement</c> mounting a
    /// <c>SemanticPanel</c>, which is a <c>Panel</c> and therefore outside <c>Padding</c>'s gate —
    /// so a trailing <c>.Padding(16)</c> is dropped at runtime even though the head is a Border,
    /// a perfectly legal receiver. Walking through them resolved the wrong element and reported
    /// nothing, while still incrementing the resolution count that is supposed to detect exactly
    /// that kind of silent shortfall.
    /// </remarks>
    public IReadOnlyDictionary<string, Type> TypeChangingModifiers =>
        _typeChangingModifiers
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);

    /// <summary>
    /// True for any name known to change the chain's element type, including one poisoned by
    /// disagreeing overloads.
    /// </summary>
    /// <remarks>
    /// The walk must stop at a poisoned name too. Treating "ambiguous" as "not type-changing" would
    /// resume walking down to the head factory and resolve the chain to an element it is no longer
    /// — the very substitution this stop exists to prevent. Stopping yields an unresolvable name,
    /// the chain is skipped, and nothing is claimed.
    /// </remarks>
    public bool IsTypeChanging(string name) => _typeChangingModifiers.ContainsKey(name);

    /// <summary>
    /// Folds one overload's return type into the type-changing map, poisoning the name to
    /// <see langword="null"/> the moment two overloads disagree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The poison must be <b>irreversible</b>, which is why this writes null rather than removing
    /// the entry. Removing let the next overload re-add the name: with return types arriving as
    /// A, B, A — the ordinary shape when several overloads return A and one returns B — the B
    /// removed it and the second A restored it, leaving a genuinely ambiguous modifier resolving to
    /// one arbitrary answer. <c>GetMethods</c> order is unspecified, so that was not even
    /// reproducible.
    /// </para>
    /// <para>
    /// Extracted so the ordering can be tested directly. The live surface has one type-changing
    /// modifier whose overloads all agree, so no reflection over the real assembly can reach the
    /// state this guards.
    /// </para>
    /// </remarks>
    internal static void MergeTypeChange(Dictionary<string, Type?> map, string name, Type returned)
    {
        if (map.TryGetValue(name, out var existing))
        {
            if (existing != returned)
                map[name] = null;
        }
        else
        {
            map[name] = returned;
        }
    }

    /// <summary>
    /// The control's own name followed by every base type name, most derived first.</summary>
    public static IEnumerable<string> ControlBaseChain(Type control)
    {
        for (var current = control; current is not null; current = current.BaseType)
            yield return current.Name;
    }
}

/// <summary>What a finding accuses a shipped snippet of.</summary>
internal enum AgentKitFindingKind
{
    /// <summary>
    /// A common modifier applied to a receiver <c>Reconciler.ApplyModifiers</c> never writes it
    /// to — the shipped-documentation form of <c>REACTOR_MOD_003</c>.
    /// </summary>
    DroppedModifier,

    /// <summary>
    /// A wrapper element introduced only to receive a modifier the inner element drops, where the
    /// inner element has a first-class replacement.
    /// </summary>
    WrapperWorkaround,
}

/// <summary>One accusation against one line of one shipped document.</summary>
/// <param name="Line">Line of the offending modifier itself — where a reader looks.</param>
/// <param name="ChainStartLine">Line the fluent chain starts on. A multi-line chain puts its
/// <c>// Wrong:</c> marker above the <em>head</em>, not above the modifier three lines down, so
/// counterexample detection anchors here while the message points at <paramref name="Line"/>.</param>
internal sealed record AgentKitFinding(
    AgentKitFindingKind Kind,
    string Path,
    int Line,
    int ChainStartLine,
    string Modifier,
    string ElementName,
    string? Replacement,
    string Detail);

/// <summary>Everything one pass over the corpus produced, findings and instrumentation alike.</summary>
internal sealed record AgentKitScan(
    IReadOnlyList<AgentKitFinding> Findings,
    int SnippetCount,
    int ResolvedChains)
{
    public IEnumerable<AgentKitFinding> Of(AgentKitFindingKind kind) => Findings.Where(f => f.Kind == kind);
}

/// <summary>
/// Runs Reactor's own modifier-gate rules over the C# in the shipped agent kit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Syntax only.</b> The snippets are fragments — undeclared <c>children</c>, no enclosing class,
/// no usings — so they do not bind. A semantic pass would resolve every receiver to an error type,
/// every check would skip, and the fact would report "no findings" while measuring nothing. Instead
/// the chain head is resolved by <em>name</em> through <see cref="ReactorSurface"/>, and anything
/// that does not resolve is skipped. That asymmetry is deliberate and mirrors
/// <see cref="NoOpModifierAnalyzer"/>: uncertainty must never produce a finding, because this gate
/// runs in CI over prose-adjacent artifacts and one false positive gets it deleted.
/// </para>
/// <para>
/// The floors reported in <see cref="AgentKitScan"/> are what keep the skipping honest — a parser
/// that stopped matching reads identically to a clean corpus otherwise.
/// </para>
/// </remarks>
internal static class AgentKitSnippetWalker
{
    /// <summary>
    /// Modifiers that can move from the wrapper to the element it wraps without changing
    /// behaviour, so their presence does not stop the wrapper being removable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <b>allowlist</b>, deliberately. The earlier denylist had the failure mode backwards: an
    /// unrecognised modifier counted as "wrapper contributes nothing" and produced a finding, so
    /// every modifier nobody had thought of became a false positive.
    /// <c>Border(FlexColumn(children)).Padding(16).OnTapped(...)</c> was reported as removable even
    /// though the Border is the routed-event source and hit-test boundary. The same applies to
    /// flyouts, tooltips, connected animations and anything else keyed on control identity — an
    /// open-ended set that cannot be enumerated safely.
    /// </para>
    /// <para>
    /// Everything here describes the wrapper's <em>position in its parent</em> or its reconciler
    /// identity, which the inner element inherits when it takes the wrapper's place. Anything else
    /// suppresses the finding. Adding an entry means claiming it is relocatable, so the burden sits
    /// where the risk is.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> RelocatableModifiers = new(StringComparer.Ordinal)
    {
        "Flex", "Grid", "Margin", "WithKey", "HAlign", "VAlign", "Dock",
    };

    /// <summary>
    /// DSL <b>factories</b> whose sole contribution is decoration, so relocating a modifier inward
    /// and deleting the call preserves behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed by factory name, not element type, and that distinction is load-bearing.
    /// <c>Factories.Card</c> also returns a <c>BorderElement</c>, but it is
    /// <c>Border(child).Background(...).WithBorder(...).CornerRadius(8).Padding(16)</c> — its
    /// decoration is baked into the factory where no chain inspection can see it, and its own
    /// documentation offers <c>Card(child).Padding(24)</c> as the sanctioned way to override the
    /// preset. Judging by element type reported that as a workaround: a false positive on
    /// documented, correct usage.
    /// </para>
    /// <para>
    /// Exactly one entry, and it should stay hard to add to. <c>Border</c> contributes background,
    /// corner radius, border brush and padding, so a plain <c>Border</c> supplying only padding
    /// contributes nothing once <c>.FlexPadding(...)</c> exists.
    /// </para>
    /// <para>
    /// "Single child" is <b>not</b> a substitute for this test.
    /// <c>ScrollViewer(FlexColumn(children)).Padding(16)</c> has one child and is a gate-legal
    /// receiver, yet deleting it deletes scrolling — the wrapper is load-bearing and the sample is
    /// correct. Before adding a factory here, establish that removing it changes nothing but
    /// appearance <em>and</em> that it applies no decoration of its own; if either fails, the rule
    /// does not apply to it.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> PassiveWrapperFactories =
        new(StringComparer.Ordinal) { "Border" };

    /// <summary>
    /// A preprocessor directive line, matched so it can be blanked without moving any other line.
    /// </summary>
    /// <remarks>
    /// The directive text is replaced with nothing rather than the line being removed, so every
    /// following line keeps its index and a finding still reports the line it is really on.
    /// </remarks>
    private static readonly Regex Preprocessor = new(
        @"^[ \t]*#[ \t]*(if|elif|else|endif|define|undef)\b[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static AgentKitScan Scan(IEnumerable<AgentKitSnippet> snippets)
    {
        var findings = new List<AgentKitFinding>();
        var snippetCount = 0;
        var resolvedChains = 0;

        foreach (var snippet in snippets)
        {
            snippetCount++;

            // Cheap pre-filter. The walker can only ever report on an invocation whose name is a
            // ModifierTable key, so a snippet mentioning none of them can produce neither a finding
            // nor a resolved chain — skipping its Roslyn parse is exactly equivalent, not merely
            // cheaper, and it is most of the corpus. Note this is a bare-name test, deliberately
            // looser than the syntax it stands in for: over-matching costs a parse, under-matching
            // would cost coverage.
            if (!MentionsAGatedModifier(snippet.Text))
                continue;

            // Conditional compilation: `ParseText` uses the default symbol set, so an inactive
            // branch survives only as DisabledTextTrivia and never reaches DescendantNodes — a
            // sample inside `#if DEBUG` would ship unscanned, and the corpus already contains one
            // (`skills/devtools.md`). Blanking the directive lines makes every branch ordinary
            // text while keeping each remaining line at its original index, so reported line
            // numbers stay right. Concatenated `#else` arms may not parse; that costs nothing,
            // because an unresolvable chain is skipped rather than reported.
            var text = Preprocessor.IsMatch(snippet.Text)
                ? Preprocessor.Replace(snippet.Text, string.Empty)
                : snippet.Text;

            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax access)
                    continue;

                var modifier = access.Name.Identifier.Text;
                if (!ModifierTable.Properties.TryGetValue(modifier, out var info))
                    continue;

                var head = ResolveHead(access.Expression);
                if (head is null)
                    continue;

                var element = ReactorSurface.Instance.Element(head.Value.Factory, head.Value.TypeArgumentCount);
                if (element is null)
                    continue;

                resolvedChains++;

                // Mirrors NoOpModifierAnalyzer's constant-null gate (its line 232). Reporting here
                // would let this gate reject a sample the packaged analyzer accepts, which is worse
                // than the drift it exists to catch.
                if (HasConstantNullArgument(invocation, modifier))
                    continue;

                var line = LineOf(tree, snippet, access.Name);
                var chainStart = LineOf(tree, snippet, invocation);

                if (DroppedModifier(element, modifier, info) is { } dropped)
                {
                    findings.Add(dropped with { Path = snippet.Path, Line = line, ChainStartLine = chainStart });
                    continue;
                }

                if (WrapperWorkaround(element, head.Value.Factory, head.Value.Arguments, modifier, invocation) is { } wrapper)
                    findings.Add(wrapper with { Path = snippet.Path, Line = line, ChainStartLine = chainStart });
            }
        }

        return new AgentKitScan(findings, snippetCount, resolvedChains);
    }

    /// <summary>
    /// Mirrors <c>NoOpModifierAnalyzer.HasConstantNullArgument</c>: a syntactically constant
    /// <see langword="null"/> argument makes the modifier inert whatever the receiver is, so the
    /// control gate is not what stops it doing anything, and the replacement would not be
    /// equivalent — <c>Optional&lt;T&gt;</c> turns <c>with { X = null }</c> into an explicit clear
    /// rather than <c>Unset</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, the documentation gate would reject a sample the packaged analyzer accepts
    /// (<c>NoOpModifierAnalyzerTests.Does_Not_Fire_For_A_Constant_Null_Argument</c> pins the
    /// analyzer's side of it). A gate stricter than the rule it enforces produces unfixable
    /// findings, and the only remedy a reader would find is to delete the gate.
    /// </para>
    /// <para>
    /// Syntactic only, matching this walker's boundary rather than the analyzer's: the analyzer
    /// uses <c>GetConstantValue</c>, which additionally resolves <c>const</c> fields, and there is
    /// no semantic model here. The gap is safe in one direction only — a constant this fails to
    /// recognise yields a finding, never a silent miss — and a documentation sample passing a
    /// <c>const</c> null is not a shape that occurs.
    /// </para>
    /// </remarks>
    private static bool HasConstantNullArgument(InvocationExpressionSyntax invocation, string modifier)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var expression = argument.Expression;
            string? castType = null;

            // (Brush)null, ((Brush)null), null! — unwrap to whatever is really passed. The cast's
            // type is remembered rather than discarded, because for `(Brush)default` it is the only
            // statement of the target type: reduced to a bare `default`, `Background` looks
            // unprovable thanks to its ThemeRef overload and the call would be reported even though
            // the analyzer skips it. `!` is unwrapped for the same reason the chain walkers treat it
            // as transparent — it changes nullability analysis, never the value.
            while (true)
            {
                switch (expression)
                {
                    case CastExpressionSyntax cast:
                        castType = cast.Type.ToString();
                        expression = cast.Expression;
                        continue;
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } forgiving:
                        expression = forgiving.Operand;
                        continue;
                }

                break;
            }

            // `null` is null whatever it is assigned to.
            if (expression.IsKind(SyntaxKind.NullLiteralExpression))
                return true;

            // `default` and `default(T)` are not. `.Background(default(Brush))` is null, but
            // `.Padding(default(double))` is 0 — a real write the analyzer reports and
            // ApplyModifiers really does drop on a Flex receiver. Exempting it would hide a true
            // finding, so the question is answered from the written type where there is one and
            // from the modifier's overload set otherwise; unprovable never exempts.
            if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) || expression is DefaultExpressionSyntax)
            {
                // `default(T)` states its own type; `(T)default` states it on the cast.
                var written = (expression as DefaultExpressionSyntax)?.Type.ToString() ?? castType;

                if (invocation.ArgumentList.Arguments.Count == 1
                    && ReactorSurface.Instance.DefaultIsProvablyNull(
                        modifier,
                        written,
                        invocation.ArgumentList.Arguments[0].NameColon?.Name.Identifier.Text))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The shipped-documentation form of <c>REACTOR_MOD_003</c>: does
    /// <c>Reconciler.ApplyModifiers</c> write <paramref name="modifier"/> to the control this
    /// element mounts?
    /// </summary>
    /// <remarks>
    /// Every early return mirrors one of the analyzer's hard gates. In particular a null
    /// <see cref="ModifierInfo.ControlGate"/> is <b>not</b> read as "applies everywhere" — the
    /// table leaves it null for properties WinUI only declares on <c>Control</c>, where the
    /// <c>.Set</c> direction needs no predicate.
    /// </remarks>
    private static AgentKitFinding? DroppedModifier(Type element, string modifier, ModifierInfo info)
    {
        if (info.ControlGate is not { } gate)
            return null;

        // A type-specific overload writes the element record directly and is always sound.
        //
        // Known imprecision, in the safe direction. This exempts the *element*, not the *call*, so
        // it also covers a call that actually binds the generic modifier because no type-specific
        // overload accepts its argument: RichTextBlockElement declares Foreground(string) and
        // Foreground(Brush) but no ThemeRef overload, so `RichTextBlock(...).Foreground(Theme.X)`
        // binds the generic one and REACTOR_MOD_003 does report it, while this returns null.
        //
        // Narrowing it needs the argument's type, which a syntax-only walk cannot have: refusing to
        // exempt would report `RichTextBlock(...).Foreground("#fff")`, which binds type-specific and
        // is correct. That trades a latent missed finding for a live false positive on valid
        // documentation — the one outcome this gate must not produce. The shape does not occur in
        // the corpus today; if it ever does, the fix is a semantic model, not a sharper guess.
        if (info.ElementTypes is { } elementTypes && elementTypes.Contains(element.Name, StringComparer.Ordinal))
            return null;

        // The element's own descriptor consumes the common-modifier slot, so the gate is not the
        // authority — RichTextBlockElement.Padding is the framework's only such case today.
        if (NoOpModifierAnalyzer.DescriptorAppliedModifiers.Contains(
                NoOpModifierAnalyzer.ElementModifierKey(element.FullName ?? element.Name, modifier)))
            return null;

        var control = ReactorSurface.Instance.MountedControl(element);
        if (control is null)
            return null;

        // Polymorphic hosts stand in for "whatever was handed to us"; the declared type says
        // nothing about what ApplyModifiers will see.
        if (control.Name is "FrameworkElement" or "UIElement")
            return null;

        if (ReactorSurface.ControlBaseChain(control).Any(name => gate.Contains(name, StringComparer.Ordinal)))
            return null;

        return new AgentKitFinding(
            AgentKitFindingKind.DroppedModifier,
            Path: string.Empty,
            Line: 0,
            ChainStartLine: 0,
            modifier,
            element.Name,
            Replacement(element, modifier),
            $".{modifier}(...) on {element.Name} mounts {control.Name}, which is outside the gate " +
            $"[{string.Join("|", gate.OrderBy(g => g, StringComparer.Ordinal))}] — ApplyModifiers drops the value");
    }

    /// <summary>
    /// The #1119 shape: a wrapper element introduced purely to receive a modifier that the element
    /// it wraps silently drops, when that element has a first-class replacement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the rule <c>reactor-design/SKILL.md</c> states in prose — "don't add a Border solely
    /// to get padding; a Border is still right when you also need its background, corner radius, or
    /// border brush" — applied to the documents that ship alongside it. It is parameterised off
    /// <see cref="NoOpModifierAnalyzer.ElementReplacements"/>, so a second entry in that table
    /// extends the rule with no edit here.
    /// </para>
    /// <para>
    /// Note the deliberate difference from <see cref="DroppedModifier"/>: the wrapper is a
    /// <em>legal</em> receiver, so no gate is violated and <c>REACTOR_MOD_003</c> is correctly
    /// silent. That is exactly why #1119 shipped — the contradiction lived in a sample the analyzer
    /// had nothing to say about.
    /// </para>
    /// </remarks>
    private static AgentKitFinding? WrapperWorkaround(
        Type wrapperElement,
        string wrapperFactory,
        SeparatedSyntaxList<ArgumentSyntax> wrapperArguments,
        string modifier,
        InvocationExpressionSyntax invocation)
    {
        foreach (var (key, replacement) in NoOpModifierAnalyzer.ElementReplacements)
        {
            var separator = key.LastIndexOf('|');
            if (separator < 0)
                continue;

            var innerFullName = key[..separator];
            if (!string.Equals(key[(separator + 1)..], modifier, StringComparison.Ordinal))
                continue;

            // The wrapper must itself be a legal receiver; if it is not, the dropped-modifier rule
            // already owns this line and reporting twice helps nobody.
            if (!ModifierTable.Properties.TryGetValue(modifier, out var info) || info.ControlGate is not { } gate)
                continue;

            var wrapperControl = ReactorSurface.Instance.MountedControl(wrapperElement);
            if (wrapperControl is null
                || !ReactorSurface.ControlBaseChain(wrapperControl).Any(name => gate.Contains(name, StringComparer.Ordinal)))
                continue;

            // The wrapper must be semantically passive: something whose *only* contribution is
            // decoration, so moving the modifier inward and deleting it is behaviour-preserving.
            // Neither a single child nor the element type establishes that — `Card(...)` is also a
            // one-child BorderElement, and `ScrollViewer(...)` is also a one-child gate-legal
            // receiver, but deleting either loses something the sample needs.
            if (!PassiveWrapperFactories.Contains(wrapperFactory))
                continue;

            if (wrapperArguments.Count != 1)
                continue;

            var innerHead = ResolveHead(wrapperArguments[0].Expression);
            if (innerHead is null)
                continue;

            var inner = ReactorSurface.Instance.Element(innerHead.Value.Factory, innerHead.Value.TypeArgumentCount);
            if (inner is null || !string.Equals(inner.FullName, innerFullName, StringComparison.Ordinal))
                continue;

            // Report only when every *other* modifier on the chain would survive the wrapper being
            // removed. Inverted from a denylist deliberately: an unrecognised modifier now
            // suppresses rather than reports, so the open-ended set of behaviour-bearing modifiers
            // (events, flyouts, tooltips, animations) costs a missed finding instead of a false one.
            if (ChainModifiers(invocation).Any(m => m.Invocation != invocation && !IsRelocatable(m)))
                continue;

            // A `with` initializer anywhere on the chain is opaque to the checks above: the chain
            // walkers step through it to reach the receiver, so its assignments are never seen.
            // `Border(inner).Padding(16) with { Background = brush }` supplies a background, and
            // `with { Child = other }` replaces the very element the finding names — both make the
            // report wrong, and element records are configured this way by design.
            if (HasWithMutation(invocation))
                continue;

            // The remedy deletes the wrapper, which reparents the inner element. Modifiers on the
            // *inner* chain that are interpreted by the parent — `.Flex`, `.Grid`, `.Dock` and the
            // alignment family — then resolve against a different container than they were written
            // for: in `Border(FlexColumn(children).Flex(grow: 1)).Padding(16)` the `.Flex` is inert
            // inside the Border and starts driving layout once it is gone. Suppress rather than
            // recommend a fix that is not behaviour-preserving. Reusing the relocation allowlist
            // keeps one table for "modifiers whose meaning depends on the parent", and the #1119
            // shape is unaffected — its inner chain carries a `with` initializer, not modifiers.
            if (ModifiersFrom(wrapperArguments[0].Expression).Any(IsRelocatable))
                continue;

            return new AgentKitFinding(
                AgentKitFindingKind.WrapperWorkaround,
                Path: string.Empty,
                Line: 0,
                ChainStartLine: 0,
                modifier,
                inner.Name,
                replacement,
                $"{wrapperElement.Name} wraps a {inner.Name} only to carry .{modifier}(...) — use " +
                $".{replacement}(...) on the {inner.Name} instead. A {wrapperElement.Name} is still " +
                "right when it also supplies background, corner radius, or border brush");
        }

        return null;
    }

    /// <summary>
    /// The replacement Reactor documents for this dropped modifier, element-specific first, then
    /// the shape paint modifiers, then <see langword="null"/> when the framework offers none.
    /// </summary>
    private static string? Replacement(Type element, string modifier)
    {
        if (NoOpModifierAnalyzer.ElementReplacements.TryGetValue(
                NoOpModifierAnalyzer.ElementModifierKey(element.FullName ?? element.Name, modifier),
                out var elementSpecific))
            return elementSpecific;

        var control = ReactorSurface.Instance.MountedControl(element);
        if (control is not null
            && ReactorSurface.ControlBaseChain(control).Contains("Shape", StringComparer.Ordinal)
            && NoOpModifierAnalyzer.ShapeReplacements.TryGetValue(modifier, out var shape))
        {
            // In order, and only if it exists on this element — the analyzer's rule, and the reason
            // LineElement falls through from Fill to Stroke. Taking the first candidate blindly
            // would name a remedy that does not compile, and would reject a Line counterexample
            // that correctly documents `.Stroke(...)`.
            return shape.FirstOrDefault(candidate => ReactorSurface.Instance.HasModifier(element, candidate));
        }

        return null;
    }

    /// <summary>
    /// True when a modifier on the wrapper's chain would survive the wrapper being removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ways to qualify: it is a positional/identity modifier the inner element inherits when it
    /// takes the wrapper's place, or it is a <em>gated common modifier</em> with a constant-null
    /// argument, which does nothing at all.
    /// </para>
    /// <para>
    /// The null arm is restricted to <see cref="ModifierTable.Properties"/> on purpose. "A null
    /// argument makes it inert" holds for the gated modifiers <c>HasConstantNullArgument</c>
    /// mirrors the analyzer over; it does not generalise. <c>.Stagger(delay, null)</c> still
    /// installs a stagger configuration, and moving it onto the inner element changes which
    /// children animate — so an unknown modifier taking a null must keep suppressing the finding,
    /// like any other unknown.
    /// </para>
    /// </remarks>
    private static bool IsRelocatable((string Name, InvocationExpressionSyntax Invocation) modifier) =>
        RelocatableModifiers.Contains(modifier.Name)
        || (ModifierTable.Properties.ContainsKey(modifier.Name)
            && HasConstantNullArgument(modifier.Invocation, modifier.Name));

    /// <summary>
    /// True when any <c>with</c> expression appears on the fluent chain this invocation belongs to.
    /// </summary>
    /// <remarks>
    /// Conservative by construction: a <c>with</c> initializer can supply the very decoration that
    /// would justify the wrapper (<c>with { Background = brush }</c>) or replace the element the
    /// finding names (<c>with { Child = other }</c>), and reading it properly would mean modelling
    /// record initializers well enough to be trusted. Skipping costs a finding on an unusual shape;
    /// guessing costs a false report on correct documentation, which is the more expensive mistake
    /// for a gate that runs in CI.
    /// </remarks>
    private static bool HasWithMutation(InvocationExpressionSyntax invocation)
    {
        for (var node = Outermost(invocation); node is not null;)
        {
            switch (node)
            {
                case WithExpressionSyntax:
                    return true;

                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }:
                    node = access.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    continue;

                case PostfixUnaryExpressionSyntax postfix:
                    node = postfix.Operand;
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>Every modifier applied anywhere on the fluent chain this invocation belongs to.</summary>
    private static IEnumerable<(string Name, InvocationExpressionSyntax Invocation)> ChainModifiers(
        InvocationExpressionSyntax invocation) =>
        ModifiersFrom(Outermost(invocation));

    /// <summary>
    /// Every modifier from <paramref name="start"/> inward, without first climbing out of the chain.
    /// </summary>
    /// <remarks>
    /// The inner expression of a wrapper is already the outermost node of its own chain, and
    /// <see cref="Outermost"/> would walk straight back out of it into the wrapper's chain — so a
    /// check meant to inspect the wrapped element would have re-read the wrapper's modifiers.
    /// </remarks>
    private static IEnumerable<(string Name, InvocationExpressionSyntax Invocation)> ModifiersFrom(
        SyntaxNode? start)
    {
        for (var node = start; node is not null;)
        {
            if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } chained)
            {
                yield return (access.Name.Identifier.Text, chained);
                node = access.Expression;
                continue;
            }

            node = node switch
            {
                ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
                WithExpressionSyntax with => with.Expression,
                PostfixUnaryExpressionSyntax postfix => postfix.Operand,
                _ => null,
            };
        }
    }

    /// <summary>
    /// Walks out to the outermost invocation of the fluent chain this one belongs to.
    /// </summary>
    /// <remarks>
    /// Parentheses, <c>with</c> and the null-forgiving <c>!</c> are stepped through, because the
    /// downward walks treat them as transparent: stopping here but not there made
    /// <c>(Border(FlexColumn(children)).Padding(16)).Background(...)</c> report a workaround,
    /// never having reached the <c>Background</c> that justifies the Border.
    /// </remarks>
    private static SyntaxNode Outermost(InvocationExpressionSyntax invocation)
    {
        SyntaxNode outermost = invocation;

        while (true)
        {
            var parent = outermost.Parent;

            while (parent is ParenthesizedExpressionSyntax
                   or WithExpressionSyntax
                   or PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
            {
                outermost = parent;
                parent = parent.Parent;
            }

            if (parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax parentInvocation })
            {
                outermost = parentInvocation;
                continue;
            }

            return outermost;
        }
    }

    /// <summary>
    /// Walks a fluent chain down to the factory call that started it.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> for anything that is not a bare factory invocation —
    /// a local, a parameter, <c>this.Something</c>, a qualified call. Those could be any element at
    /// runtime, which is the same reason <see cref="NoOpModifierAnalyzer"/> refuses to report on a
    /// receiver typed as <c>Element</c>.
    /// </remarks>
    private static (string Factory, int TypeArgumentCount, SeparatedSyntaxList<ArgumentSyntax> Arguments)? ResolveHead(ExpressionSyntax expression)
    {
        for (var node = expression; node is not null;)
        {
            switch (node)
            {
                // `Border(...)` and `ListView<Todo>(...)` alike — a generic factory's name lives on
                // GenericNameSyntax, and rejecting that shape was half of the blind spot that kept
                // generic factories out of this walker entirely. The call site's own type-argument
                // count is what tells `ListView(...)` from `ListView<T>(...)`.
                case InvocationExpressionSyntax { Expression: GenericNameSyntax generic } genericInvocation:
                    return (generic.Identifier.Text, generic.TypeArgumentList.Arguments.Count, genericInvocation.ArgumentList.Arguments);

                case InvocationExpressionSyntax { Expression: IdentifierNameSyntax identifier } invocation:
                    return (identifier.Identifier.Text, 0, invocation.ArgumentList.Arguments);

                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }:
                    // A type-changing modifier decides the element outright: everything below it
                    // describes what it wrapped, not what the chain now is. `Semantics` is the only
                    // one today, and walking through it resolved `Border(child).Semantics(...)` as a
                    // Border while the value really lands on a SemanticPanel.
                    if (ReactorSurface.Instance.IsTypeChanging(access.Name.Identifier.Text))
                        return (access.Name.Identifier.Text, 0, default);

                    node = access.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    continue;

                case WithExpressionSyntax with:
                    node = with.Expression;
                    continue;

                case PostfixUnaryExpressionSyntax postfix:
                    node = postfix.Operand;
                    continue;

                default:
                    return null;
            }
        }

        return null;
    }

    private static int LineOf(SyntaxTree tree, AgentKitSnippet snippet, SyntaxNode node) =>
        snippet.StartLine + tree.GetLineSpan(node.Span).StartLinePosition.Line;

    /// <summary>Every property name the gate rules can fire on, materialised once.</summary>
    private static readonly string[] GatedModifierNames = ModifierTable.Properties.Keys.ToArray();

    private static bool MentionsAGatedModifier(string text) =>
        GatedModifierNames.Any(name => text.Contains(name, StringComparison.Ordinal));
}
