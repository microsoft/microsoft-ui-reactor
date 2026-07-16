using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.UI.Reactor.Wrappers.Generator;

/// <summary>
/// Experimental Roslyn source generator (spec 058) that turns any WinUI /
/// third-party <c>FrameworkElement</c> control into a first-class Reactor
/// element. The author writes a partial element record annotated with the
/// control to wrap; the generator fills in the rest of that same partial:
///
/// <code>
/// [GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsCard))]
/// public partial record SettingsCardElement;
/// </code>
///
/// Into the same partial the generator emits one init-property per surfaced
/// control property, a <c>Content</c> child slot, <c>On{Event}</c> callbacks, a
/// <c>ControlDescriptor</c>, the spec-048 Pattern-A registration static
/// constructor, and a <b>parameterized factory</b> static method named after
/// the control.
///
/// <para><b>Two-way / controlled props (spec 058 Phase 2, spec 050).</b> A
/// value property <c>P</c> paired with a sibling <c>PChanged</c> event is
/// emitted as a <i>controlled</i> prop: the element field is
/// <c>Optional&lt;T&gt;</c> (default <c>Unset</c> ⇒ the control owns the value
/// and user interaction survives re-renders; an explicit value ⇒ force-assert),
/// plus an <c>OnPChanged</c> callback. It wires through the public
/// <c>ControlDescriptor.Controlled&lt;TValue,TArgs&gt;</c> entry, which
/// encapsulates echo suppression internally — the generated code never touches
/// the internal <c>ChangeEchoSuppressor</c>.</para>
///
/// <para>Phase-1 (one-way) scope: <c>string</c>/<c>object</c> (text),
/// <c>bool</c>/<c>int</c>/<c>double</c>/enum value props; a single
/// <c>Content</c> slot; <c>RoutedEventHandler</c> fire-and-forget events.</para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class WrapperGenerator : IIncrementalGenerator
{
    private const string AttributeFqn = "Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapperAttribute";
    private const string DescriptorAttributeFqn = "Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptorAttribute";

    private static readonly DiagnosticDescriptor UnsupportedTarget = new(
        id: "REACTORGEN001",
        title: "Unsupported wrapper target",
        messageFormat: "GenerateReactorWrapper control '{0}' must be a non-static class deriving from Microsoft.UI.Xaml.FrameworkElement with a public parameterless constructor",
        category: "Reactor.Wrappers",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // REACTORGEN012 — a control has a single controlled-event state slot (the
    // descriptor controlled-prop payload is keyed per-control), so AT MOST ONE
    // controlled/two-way prop per control works. A second controlled prop (whether
    // auto-paired via a {Prop}Changed convention or an explicit [WrapControlled])
    // silently never fires its callback. The fix is to make all but one of them
    // one-way and observe changes through a single typed event instead
    // (e.g. RangeSelector: one-way RangeStart/RangeEnd + OnValueChanged).
    private static readonly DiagnosticDescriptor MultipleControlledProps = new(
        id: "REACTORGEN012",
        title: "Multiple controlled props on one control",
        messageFormat: "'{0}' surfaces {1} controlled/two-way props ({2}) but a control supports only one — the extras never fire. Make all but one one-way and observe changes through a single typed event.",
        category: "Reactor.Wrappers",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // Fully-qualified display that also preserves nullable-reference annotations
    // (e.g. a projected event-arg property typed `string?` surfaces as a
    // `string?` callback parameter, not `string` — so the generated trampoline's
    // `Invoke(args.Prop)` doesn't warn CS8604).
    private static readonly SymbolDisplayFormat NullableFqnFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeFqn,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, ct) => BuildModel(ctx, ct, descriptorOnly: false))
            .Where(static m => m is not null);

        // Spec 058 §15 (P5) — descriptor-only ("attach") generation against an
        // existing record. Emits ONLY the descriptor + registration (no init
        // props, no factory).
        var descriptorModels = context.SyntaxProvider.ForAttributeWithMetadataName(
            DescriptorAttributeFqn,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, ct) => BuildModel(ctx, ct, descriptorOnly: true))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            if (model!.Diagnostic is { } d) spc.ReportDiagnostic(d);
            if (model.Source is { } src) spc.AddSource(model.HintName!, SourceText.From(src, Encoding.UTF8));
        });

        context.RegisterSourceOutput(descriptorModels, static (spc, model) =>
        {
            if (model!.Diagnostic is { } d) spc.ReportDiagnostic(d);
            if (model.Source is { } src) spc.AddSource(model.HintName!, SourceText.From(src, Encoding.UTF8));
        });
    }

    private static WrapperModel? BuildModel(GeneratorAttributeSyntaxContext ctx, CancellationToken _, bool descriptorOnly)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol element) return null;
        var attr = ctx.Attributes.FirstOrDefault();
        if (attr is null || attr.ConstructorArguments.Length != 1) return null;
        if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol control) return null;

        var compilation = ctx.SemanticModel.Compilation;
        var elementName = element.Name;

        // [WrapPolymorphic(resolve, Reconcile=, EmptySentinel=)] — decorator-style
        // emission (spec 058 §15 / P5.27): the concrete control type is resolved at
        // runtime, so emit an IDecoratorElementHandler that calls the author's
        // resolver instead of `new TControl()`. Branches BEFORE IsValidTarget /
        // CollectMembers — the base control type need not be concrete/instantiable
        // and no value props are surfaced (the resolver + reconcile do everything).
        var polySymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapPolymorphicAttribute");
        if (polySymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, polySymbol)) continue;
                if (a.ConstructorArguments.Length < 1 || a.ConstructorArguments[0].Value is not string resolve) continue;
                string? reconcile = null, emptySentinel = null;
                foreach (var na in a.NamedArguments)
                {
                    if (na.Key == "Reconcile" && na.Value.Value is string rc) reconcile = rc;
                    else if (na.Key == "EmptySentinel" && na.Value.Value is string es) emptySentinel = es;
                }
                var polyNs = element.ContainingNamespace.IsGlobalNamespace
                    ? null
                    : element.ContainingNamespace.ToDisplayString();
                var hasSetters = element.GetMembers().Any(m => m is IPropertySymbol { IsStatic: false, Name: "Setters" });
                var polySource = EmitPolymorphic(
                    polyNs,
                    elementName,
                    control.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    resolve, reconcile, emptySentinel, hasSetters);
                return WrapperModel.Ok($"{elementName}.Polymorphic.g.cs", polySource);
            }
        }

        // [WrapDecorator(create, OnUpdate=, OnUnmount=)] — monomorphic custom-lifecycle
        // decorator emission (spec 058 §15 / P5.28): the control is created once and
        // mutated in place (Frame.Navigate, interop Factory/Updater). Like the
        // polymorphic path, branches BEFORE IsValidTarget — the control type the
        // handler casts to need not be instantiable by the generator (the Create
        // method owns construction) and no value props are surfaced.
        var decoSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapDecoratorAttribute");
        if (decoSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, decoSymbol)) continue;
                if (a.ConstructorArguments.Length < 1 || a.ConstructorArguments[0].Value is not string create) continue;
                string? onUpdate = null, onUnmount = null;
                foreach (var na in a.NamedArguments)
                {
                    if (na.Key == "OnUpdate" && na.Value.Value is string ou) onUpdate = ou;
                    else if (na.Key == "OnUnmount" && na.Value.Value is string od) onUnmount = od;
                }
                var decoNs = element.ContainingNamespace.IsGlobalNamespace
                    ? null
                    : element.ContainingNamespace.ToDisplayString();
                var decoSource = EmitDecorator(
                    decoNs,
                    elementName,
                    control.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    create, onUpdate, onUnmount);
                return WrapperModel.Ok($"{elementName}.Decorator.g.cs", decoSource);
            }
        }

        if (!IsValidTarget(control, compilation))
        {
            return WrapperModel.Error(Diagnostic.Create(UnsupportedTarget,
                element.Locations.FirstOrDefault(),
                control.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        var autoDiscover = true;
        var registerAssembly = true;
        var clearValueOnUnset = false;
        ImmutableHashSet<string> include = ImmutableHashSet<string>.Empty;
        ImmutableHashSet<string> exclude = ImmutableHashSet<string>.Empty;
        foreach (var na in attr.NamedArguments)
        {
            switch (na.Key)
            {
                case "AutoDiscover" when na.Value.Value is bool b: autoDiscover = b; break;
                case "RegisterAssembly" when na.Value.Value is bool ra: registerAssembly = ra; break;
                case "ClearValueOnUnset" when na.Value.Value is bool cv: clearValueOnUnset = cv; break;
                case "Include": include = ToStringSet(na.Value); break;
                case "Exclude": exclude = ToStringSet(na.Value); break;
            }
        }

        var authorDeclared = element.GetMembers().Select(m => m.Name).ToImmutableHashSet();

        // [WrapControlled("Prop", ChangedEvent = "Event")] / [WrapControlled("Prop",
        // Events = new[]{"A","B"})] overrides — force a prop to be controlled and
        // bind it to one or more non-conventional change events. The dictionary
        // value is the explicit event list (null ⇒ fall back to "{Prop}Changed").
        var overrides = new Dictionary<string, string[]?>();
        var deferredControlled = new HashSet<string>();  // props using HandCodedControlled (suppress-counter echo)
        var wcSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapControlledAttribute");
        if (wcSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, wcSymbol)) continue;
                if (a.ConstructorArguments.Length < 1 || a.ConstructorArguments[0].Value is not string prop) continue;
                string? changedEvent = null;
                string[]? eventList = null;
                foreach (var na in a.NamedArguments)
                {
                    if (na.Key == "ChangedEvent" && na.Value.Value is string ce) changedEvent = ce;
                    else if (na.Key == "Events" && !na.Value.Values.IsDefaultOrEmpty)
                        eventList = na.Value.Values.Select(v => v.Value as string).Where(s => s is not null).Select(s => s!).ToArray();
                    else if (na.Key == "Deferred" && na.Value.Value is bool df && df) deferredControlled.Add(prop);
                }
                // Events[] takes precedence; else single ChangedEvent; else convention (null).
                overrides[prop] = eventList is { Length: > 0 } ? eventList
                    : changedEvent is not null ? new[] { changedEvent }
                    : null;
            }
        }

        // [WrapAlias("Name", "ControlProperty")] — surface a control property
        // under a friendly element-facing name (controlProperty → name).
        var aliases = new Dictionary<string, string>();
        var aliasSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapAliasAttribute");
        if (aliasSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, aliasSymbol)) continue;
                if (a.ConstructorArguments.Length < 2) continue;
                if (a.ConstructorArguments[0].Value is not string name) continue;
                if (a.ConstructorArguments[1].Value is not string controlProp) continue;
                aliases[controlProp] = name;
            }
        }

        // [WrapOneWay("Prop")] — force a prop one-way even if it has a {Prop}Changed event.
        var forceOneWay = new HashSet<string>();
        var oneWaySymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapOneWayAttribute");
        if (oneWaySymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, oneWaySymbol)) continue;
                if (a.ConstructorArguments.Length >= 1 && a.ConstructorArguments[0].Value is string p)
                    forceOneWay.Add(p);
            }
        }

        // [WrapContent("Prop")] — override the single-content slot property.
        string? wrapContent = null;
        var wcontentSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapContentAttribute");
        if (wcontentSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, wcontentSymbol)) continue;
                if (a.ConstructorArguments.Length >= 1 && a.ConstructorArguments[0].Value is string cp)
                    wrapContent = cp;
            }
        }

        // [WrapConvert("Prop")] — surface a struct-typed control property through
        // an ergonomic scalar element prop, written via the struct's single-arg
        // constructor (spec 058 §15 / P5.2).
        var convert = new HashSet<string>();
        var convertSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapConvertAttribute");
        if (convertSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, convertSymbol)) continue;
                if (a.ConstructorArguments.Length >= 1 && a.ConstructorArguments[0].Value is string cvp)
                    convert.Add(cvp);
            }
        }

        // [WrapEvent("EventName", Arg = "Property")] or [WrapEvent("E", Args = new[]{...})]
        // — the control event is typed and the record's On{Event} callback is
        // Action<T> (or Action<T1,T2,…>): project args.{Arg}(s) (or the whole args
        // when neither is set) into the callback (spec 058 §15 / P5.6).
        var eventArgs = new Dictionary<string, string[]?>();
        var weSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapEventAttribute");
        if (weSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, weSymbol)) continue;
                if (a.ConstructorArguments.Length < 1 || a.ConstructorArguments[0].Value is not string en) continue;
                string? single = null;
                string[]? multi = null;
                foreach (var na in a.NamedArguments)
                {
                    if (na.Key == "Arg" && na.Value.Value is string s) single = s;
                    else if (na.Key == "Args" && !na.Value.Values.IsDefaultOrEmpty)
                        multi = na.Value.Values.Select(v => v.Value as string).Where(x => x is not null).Select(x => x!).ToArray();
                }
                // Args[] takes precedence; else single Arg; else null (whole args).
                eventArgs[en] = multi is { Length: > 0 } ? multi
                    : single is not null ? new[] { single }
                    : null;
            }
        }

        // [WrapManual("Prop")] — the author handles this prop manually in the
        // Customize hook; exclude it from auto-discovery and emit the hook
        // (spec 058 §15). Presence of any [WrapManual] makes Customize mandatory.
        var manual = new HashSet<string>();
        var manualSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapManualAttribute");
        if (manualSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, manualSymbol)) continue;
                if (a.ConstructorArguments.Length >= 1 && a.ConstructorArguments[0].Value is string mp)
                    manual.Add(mp);
            }
        }
        var hasManual = manual.Count > 0;
        if (hasManual) exclude = exclude.Union(manual);

        // [WrapElementSlot("Prop", ControlProperty="...")] — a SECONDARY single-element
        // slot: an Element? property mounted/reconciled into a UIElement-typed (or object)
        // control property, alongside the primary content/children slot. The generator
        // surfaces the Element? init prop (+ factory param, full-wrapper) and emits the
        // mount/reconcile wiring otherwise hand-written as an .ImperativeBridged entry in
        // Customize (spec 058 §15 — e.g. TabView.TabStripHeader/Footer, SettingsCard.HeaderIcon).
        var elementSlots = new List<ElementSlotInfo>();
        var slotSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapElementSlotAttribute");
        if (slotSymbol is not null)
        {
            foreach (var a in element.GetAttributes().Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, slotSymbol)
                && a.ConstructorArguments.Length >= 1 && a.ConstructorArguments[0].Value is string))
            {
                var sp = (string)a.ConstructorArguments[0].Value!;
                var controlProp = sp;
                foreach (var na in a.NamedArguments.Where(na => na.Key == "ControlProperty" && na.Value.Value is string))
                    controlProp = (string)na.Value.Value!;
                var slotTypeFqn = FindContentProperty(control, controlProp) is { } sps
                    ? sps.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : null;
                elementSlots.Add(new ElementSlotInfo(sp, controlProp, slotTypeFqn));
            }
        }
        // Keep both the element-facing name and the target control property out of value-prop
        // discovery — the slot owns the write, and an object-typed control prop (TabStripHeader)
        // would otherwise also surface as an `object?` value prop in full-wrapper mode.
        if (elementSlots.Count > 0)
            exclude = exclude.Union(elementSlots.SelectMany(s => new[] { s.Name, s.ControlProp }));

        // [WrapPanelChildren(PerChild=..., AfterAll=...)] — attached-property panel:
        // wire the generated Panel children strategy's per-child / two-pass attached hook.
        string? panelPerChild = null, panelAfterAll = null;
        var panelChildrenSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapPanelChildrenAttribute");
        if (panelChildrenSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, panelChildrenSymbol)) continue;
                foreach (var na in a.NamedArguments)
                {
                    if (na.Key == "PerChild" && na.Value.Value is string pc) panelPerChild = pc;
                    else if (na.Key == "AfterAll" && na.Value.Value is string aa) panelAfterAll = aa;
                }
            }
        }

        // [WrapLifecycle(onMounted, OnUnmounted=...)] — imperative mount/unmount
        // lifecycle: the generated factory wires the named static methods through
        // the element's .OnMount / .OnUnmount modifiers (spec 058 §15 / P5.30).
        string? lifecycleMount = null, lifecycleUnmount = null;
        var lifecycleSymbol = compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Wrappers.WrapLifecycleAttribute");
        if (lifecycleSymbol is not null)
        {
            foreach (var a in element.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, lifecycleSymbol)) continue;
                if (a.ConstructorArguments.Length >= 1 && a.ConstructorArguments[0].Value is string lm) lifecycleMount = lm;
                foreach (var na in a.NamedArguments)
                    if (na.Key == "OnUnmounted" && na.Value.Value is string lu) lifecycleUnmount = lu;
            }
        }

        // In descriptor-only mode the existing record's members are exactly what
        // we want to map — so we must NOT let CollectMembers' partial-fill skip
        // (which drops author-declared members so they can be overridden) hide
        // them. Discover the full control surface (empty skip-set), then filter
        // down to the record's real members below.
        var collectAuthorDeclared = descriptorOnly ? ImmutableHashSet<string>.Empty : authorDeclared;
        var (props, contentProp, panelChildren, items, events) = CollectMembers(control, autoDiscover, include, exclude, collectAuthorDeclared, overrides, aliases, forceOneWay, wrapContent, convert, eventArgs, deferredControlled, suppressDp: descriptorOnly);

        // Full-wrapper ergonomics: drop any discovered prop whose name is already a
        // generic element modifier on the Reactor `Element` base (Padding, Width,
        // Height, Margin, …). Some controls redeclare these above the Control/
        // FrameworkElement cutoff (e.g. WCT panels and Grid define their own
        // Padding), and surfacing them would shadow the fluent modifier (CS0108)
        // and give two competing ways to set the same thing. Authors use the
        // modifier (`.Padding(…)`) instead. Descriptor-only built-ins are
        // record-driven and unaffected.
        if (!descriptorOnly &&
            compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Core.Element") is { } elementBase)
        {
            var modifierNames = new HashSet<string>(System.StringComparer.Ordinal);
            for (INamedTypeSymbol? t = elementBase; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                foreach (var ep in t.GetMembers().OfType<IPropertySymbol>())
                    if (!ep.IsStatic && ep.DeclaredAccessibility == Accessibility.Public)
                        modifierNames.Add(ep.Name);
            if (modifierNames.Count > 0)
                props = props.Where(p => !modifierNames.Contains(p.Name)).ToImmutableArray();
        }

        // Spec 058 §15 (P5) — descriptor-only mode is RECORD-DRIVEN: the existing
        // hand-written record defines the surface. Filter the control-discovered
        // members down to those the record actually declares, so the generated
        // descriptor only references members that exist (e.g. it must not surface
        // FrameworkElement.Loaded as `e.OnLoaded` when the record never declared
        // it). The content/children/items slot is kept only when the record
        // declares the matching member (content under the control's content-prop
        // name; `Children`/`Items` for the collection strategies).
        string? contentElementName = contentProp; // record-facing GetChild name
        if (descriptorOnly)
        {
            // A prop the control auto-paired to controlled but whose record does
            // NOT declare an `On{Prop}Changed` callback is the author's signal that
            // they want it ONE-WAY (e.g. ProgressBar.Value: RangeBase exposes
            // ValueChanged, but Reactor pushes Progress one-way). DEMOTE it to
            // one-way rather than dropping it — dropping would silently stop
            // writing the prop. The record-type pass below then picks the channel.
            props = props
                .Where(p => authorDeclared.Contains(p.Name))
                .Select(p => p.Controlled && !authorDeclared.Contains("On" + p.Name + "Changed")
                    ? p with { Controlled = false, ChangedEvents = ImmutableArray<string>.Empty, ArgsType = null }
                    : p)
                .ToImmutableArray();
            events = events.Where(e => authorDeclared.Contains("On" + e.Name)).ToImmutableArray();
            // Content-name-from-record: the record's content member may be named
            // differently from the control's content property (e.g. ScrollView
            // declares `Child` but writes the control's `Content`). Resolve the
            // element-facing GetChild name: the record's own member matching the
            // control's content prop, else its single Element-typed member. If we
            // can't resolve a single one (zero, or multi-content like SplitView's
            // Pane+Content), drop the auto content slot (the author handles it).
            if (contentProp is not null)
            {
                if (authorDeclared.Contains(contentProp))
                    contentElementName = contentProp;
                else
                {
                    var elementTyped = element.GetMembers().OfType<IPropertySymbol>()
                        .Where(p => !p.IsStatic && !p.IsIndexer && IsElementType(p.Type))
                        .Select(p => p.Name).Distinct().ToList();
                    contentElementName = elementTyped.Count == 1 ? elementTyped[0] : null;
                }
                if (contentElementName is null) contentProp = null;
            }
            if (panelChildren && !authorDeclared.Contains("Children")) panelChildren = false;
            if (items && !authorDeclared.Contains("Items")) items = false;

            // Record-type-driven channel selection: the hand-written records the
            // generated descriptor must reproduce declare props as either nullable
            // `T?` (written conditionally) or non-nullable `T` with a default
            // (written unconditionally via `.OneWay`). Auto-discovery classifies
            // every value prop as nullable/conditional; here we promote a one-way
            // prop to UNCONDITIONAL when the record declares it as a non-nullable
            // value type (bool/double/enum/struct — not Nullable<> or Optional<>),
            // matching the hand-written `.OneWay(get, set)` shape exactly.
            var recordPropTypes = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
            for (INamedTypeSymbol? t = element; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                foreach (var rp in t.GetMembers().OfType<IPropertySymbol>())
                    if (!rp.IsStatic && !rp.IsIndexer && !recordPropTypes.ContainsKey(rp.Name))
                        recordPropTypes[rp.Name] = rp.Type;
            props = props.Select(p =>
                !p.Controlled && recordPropTypes.TryGetValue(p.Name, out var rt) && IsNonNullableValueType(rt)
                    ? p with { Unconditional = true, Dp = null }
                    : p).ToImmutableArray();

            // Opt-in ClearValue-on-Unset (spec 050 / 058 §15 P5.7): when the author
            // sets ClearValueOnUnset=true (e.g. TextBlock, where transitioning a
            // styling prop set→unset on a recycled control must release the local
            // value back to the theme/style — issue #522), route each NULLABLE
            // record prop backed by a `{ControlProp}Property` dependency property
            // through the Optional<T> + dp ClearValue channel instead of the
            // skip-write OneWayConditional. Non-nullable props (Unconditional) and
            // converted props are unaffected.
            if (clearValueOnUnset)
                props = props.Select(p =>
                    !p.Controlled && !p.Unconditional && p.Convert is null &&
                    recordPropTypes.TryGetValue(p.Name, out var rt2) && IsNullableType(rt2) &&
                    FindDependencyPropertyMember(control, p.ControlProp) is { } dpRef
                        ? p with { Dp = dpRef }
                        : p).ToImmutableArray();
        }

        var ns = element.ContainingNamespace.IsGlobalNamespace
            ? null
            : element.ContainingNamespace.ToDisplayString();

        // Content-slot target type: the SingleContent strategy hands SetChild a
        // `UIElement?`, but the control's content property may be narrower
        // (LayoutTransformControl.Child is a FrameworkElement). Capture the
        // declared type FQN so Emit can down-cast when it isn't object/UIElement.
        string? contentPropTypeFqn = null;
        if (contentProp is not null && FindContentProperty(control, contentProp) is { } cps)
            contentPropTypeFqn = cps.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var source = Emit(
            ns,
            elementName,
            control.Name,
            control.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            props, contentProp, panelChildren, items, events,
            descriptorOnly, hasManual, contentElementName, registerAssembly,
            panelPerChild, panelAfterAll, lifecycleMount, lifecycleUnmount,
            contentPropTypeFqn, elementSlots);

        var hint = descriptorOnly ? $"{elementName}.Descriptor.g.cs" : $"{elementName}.Wrapper.g.cs";

        // REACTORGEN012 — a control supports only one controlled/two-way prop (single
        // per-control event-state slot). Warn if more than one was surfaced; the
        // extras silently never fire (see RangeSelector: RangeStart + RangeEnd).
        // Full-wrapper only — descriptor-only built-ins are hand-reviewed records.
        var controlled = props.Where(p => p.Controlled).Select(p => p.Name).ToImmutableArray();
        if (!descriptorOnly && controlled.Length > 1)
        {
            var d = Diagnostic.Create(
                MultipleControlledProps,
                element.Locations.FirstOrDefault() ?? Location.None,
                elementName, controlled.Length, string.Join(", ", controlled));
            return WrapperModel.OkWithDiagnostic(hint, source, d);
        }

        return WrapperModel.Ok(hint, source);
    }

    private static ImmutableHashSet<string> ToStringSet(TypedConstant tc)
    {
        if (tc.Kind != TypedConstantKind.Array || tc.Values.IsDefault) return ImmutableHashSet<string>.Empty;
        return tc.Values
            .Select(v => v.Value as string)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToImmutableHashSet();
    }

    private static bool IsValidTarget(INamedTypeSymbol control, Compilation compilation)
    {
        if (control.TypeKind != TypeKind.Class || control.IsStatic || control.IsAbstract) return false;
        var fe = compilation.GetTypeByMetadataName("Microsoft.UI.Xaml.FrameworkElement");
        if (fe is null || !InheritsFrom(control, fe)) return false;
        return control.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
        return false;
    }

    // Walk the control's chain from the most-derived type up to (but excluding)
    // Microsoft.UI.Xaml.Controls.Control — captures control-specific members
    // (ButtonBase.Click, ContentControl.Content, SettingsCard.Header) while
    // skipping the Control/FrameworkElement layout plumbing Reactor models via
    // generic modifiers (spec 058 §7 Q7, default cutoff).
    private static (ImmutableArray<PropInfo> Props, string? ContentProp, bool PanelChildren, bool Items, ImmutableArray<EventInfo> Events) CollectMembers(
        INamedTypeSymbol control,
        bool autoDiscover,
        ImmutableHashSet<string> include,
        ImmutableHashSet<string> exclude,
        ImmutableHashSet<string> authorDeclared,
        IReadOnlyDictionary<string, string[]?> overrides,
        IReadOnlyDictionary<string, string> aliases,
        ISet<string> forceOneWay,
        string? wrapContent,
        ISet<string> convert,
        IReadOnlyDictionary<string, string[]?> eventArgs,
        ISet<string> deferredControlled,
        bool suppressDp)
    {
        // Pass 1 — index every public instance event by name (first-wins) so a
        // value prop can look up its sibling "{Prop}Changed" for auto-pairing.
        var eventsByName = new Dictionary<string, IEventSymbol>();
        foreach (var t in ControlChain(control, trimFrameworkBase: !suppressDp))
            foreach (var member in t.GetMembers())
                if (member is IEventSymbol evt && !evt.IsStatic &&
                    evt.DeclaredAccessibility == Accessibility.Public &&
                    !eventsByName.ContainsKey(evt.Name))
                    eventsByName[evt.Name] = evt;

        var props = new List<PropInfo>();
        var consumedEvents = new HashSet<string>();
        var seen = new HashSet<string>();

        // Panel children: a control exposing a public `Children` property of
        // type UIElementCollection (StackPanel, Canvas, Grid, …) gets a Panel
        // children strategy (append/diff into the live collection). Mutually
        // exclusive with a single-content slot. NOTE: per-child attached layout
        // props (Grid.Row, Canvas.Left) are a separate capability and are NOT
        // generated — only the children collection itself.
        var panelChildren = HasPanelChildren(control);

        // Items host: an ItemsControl-derived control exposing a public instance
        // `Items` collection of type ItemCollection (ListBox, ComboBox, ListView,
        // GridView, …) gets an ItemsHost strategy (the engine populates the live
        // items collection — strings pass through, Element items mount through
        // the reconciler). Mutually exclusive with panel children and a single-
        // content slot. `Exclude = ["Items"]` opts out (e.g. TokenizingTextBox
        // manages its Items internally and rejects direct Items.Clear()/Add()).
        // NOTE: keyed/templated virtualization (ListView<T>) and selection are
        // separate capabilities — selection is surfaced via the ordinary
        // controlled-prop path ([WrapControlled] on SelectedIndex).
        var items = !panelChildren && !exclude.Contains("Items") && HasItems(control);

        // Single-content slot: [WrapContent] override → control [ContentProperty]
        // → a property named "Content". Author declaring "Content" opts out.
        var contentProp = (panelChildren || items) ? null : DiscoverContentProperty(control, wrapContent);
        if (contentProp is not null && authorDeclared.Contains("Content")) contentProp = null;
        // Aliasing the content property opts it out of being a child slot and
        // surfaces it as a named value prop instead (e.g. ToggleButton.Content
        // → a string `Label`). UIElement-typed content can't be a value prop,
        // so this only takes effect for object/text content.
        if (contentProp is not null && aliases.ContainsKey(contentProp)) contentProp = null;

        // Pass 2 — properties.
        foreach (var t in ControlChain(control, trimFrameworkBase: !suppressDp))
        {
            foreach (var member in t.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.IsStatic || prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.SetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
                if (prop.GetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
                if (prop.IsIndexer) continue;
                if (!seen.Add(prop.Name)) continue;

                if (contentProp is not null && prop.Name == contentProp) continue; // the content slot
                // The items-host / panel collection is handled by its strategy, not as
                // a value prop — skip it even if (unusually) it has a public setter,
                // now that settable collections can otherwise surface (full-wrapper).
                if (items && prop.Name == "Items") continue;
                if (panelChildren && prop.Name == "Children") continue;
                if (exclude.Contains(prop.Name)) continue;
                if (!autoDiscover && !include.Contains(prop.Name)) continue;

                // Name-mapping: surface the control property under a friendly
                // element-facing name when [WrapAlias] is present.
                var surfacedName = aliases.TryGetValue(prop.Name, out var alias) ? alias : prop.Name;
                if (authorDeclared.Contains(surfacedName)) continue;

                var classified = Classify(prop.Type, allowCollections: !suppressDp);
                if (classified is not { } c) continue;

                // Full-wrapper: a raw `object` value prop surfaces as `object?` (not
                // the content-text `string?` default) so data/collection props
                // (ItemsSource, SuggestedItemsSource, CommandParameter, …) flow
                // DECLARATIVELY and accept a list/any value — no imperative .OnMount
                // escape hatch needed. IsObject stays true so the two-way/controlled
                // path is still skipped. Descriptor-only built-ins keep the string
                // mapping (record-driven; unchanged).
                if (!suppressDp && c.IsObject)
                    c = c with { ValueType = "object", ElementType = "object?" };

                // [WrapConvert] — a struct-typed control prop (CornerRadius,
                // Thickness, GridLength, …) surfaced through an ergonomic scalar
                // element prop, written via the struct's single-argument
                // constructor. The element value type is the ctor parameter
                // (e.g. CornerRadius ⇒ double). Always one-way (skip-write
                // OneWayConditional, matching the hand-written descriptors) — so
                // we suppress both the controlled and dp-ClearValue channels.
                string? convertType = null;
                if (convert.Contains(prop.Name) &&
                    TryGetSingleArgCtorParamType(prop.Type) is { } paramType &&
                    Classify(paramType) is { } pc)
                {
                    convertType = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    c = pc;
                }

                // Controlled (two-way) when the prop is paired with one or more
                // change events: an explicit [WrapControlled] override names the
                // event(s) (a single ChangedEvent, an Events[] list, or — when
                // declared with neither — defaults to "{Prop}Changed"); otherwise
                // auto-pair keys on the "{Prop}Changed" convention. For a
                // multi-event prop the new value is read back from the control
                // property after any event fires; TArgs is taken from the first
                // event. Keyed on the control property.
                var hasOverride = overrides.TryGetValue(prop.Name, out var ov);
                var changeEventNames = ov ?? new[] { prop.Name + "Changed" };

                if (!c.IsObject &&
                    convertType is null &&
                    !forceOneWay.Contains(prop.Name) &&
                    TryResolveControlledEvents(eventsByName, changeEventNames, out var resolved, out var argsType))
                {
                    foreach (var ev in resolved) consumedEvents.Add(ev);
                    // Deferred (suppress-counter) channel: capture the change event's
                    // delegate type for the generated HandCodedControlled trampoline.
                    var deferred = deferredControlled.Contains(prop.Name);
                    string? ctrlDelegate = null;
                    if (deferred && resolved.Length >= 1 &&
                        eventsByName.TryGetValue(resolved[0], out var devt) && devt.Type is INamedTypeSymbol ddel)
                        ctrlDelegate = ddel.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    props.Add(new PropInfo(
                        surfacedName, prop.Name, c.ValueType,
                        ElementType: $"global::Microsoft.UI.Reactor.Optional<{c.ValueType}>",
                        IsReference: false, Controlled: true, ChangedEvents: resolved, ArgsType: argsType, Dp: null,
                        Deferred: deferred, ControlledDelegate: ctrlDelegate));
                    continue;
                }

                // One-way. When the prop is backed by a `{Prop}Property`
                // dependency property, use the spec-050 Optional<T> + dp
                // ClearValue channel (Unset ⇒ ClearValue releases the local
                // value to the WinUI style/precedence chain). Otherwise fall
                // back to the nullable-`T?` skip-write OneWayConditional.
                // Converted props always take the skip-write OneWayConditional
                // path (no dp channel) to match the hand-written descriptors.
                // Descriptor-only mode (suppressDp) likewise forces
                // OneWayConditional: the existing hand-written records declare
                // `T?` (Nullable), not `Optional<T>`, and the built-in
                // descriptors they replace use OneWayConditional — so the dp
                // channel would both mismatch the record's type and diverge
                // from the behaviour being reproduced.
                var dp = (suppressDp || convertType is not null) ? null : FindDependencyProperty(control, prop.Name);
                var elementType = dp is not null
                    ? $"global::Microsoft.UI.Reactor.Optional<{c.ValueType}>"
                    : c.ElementType;
                props.Add(new PropInfo(surfacedName, prop.Name, c.ValueType, elementType, c.IsReference,
                    Controlled: false, ChangedEvents: ImmutableArray<string>.Empty, ArgsType: null, Dp: dp, Convert: convertType));
            }
        }

        // Fire-and-forget events (Q4: RoutedEventHandler + TypedEventHandler<,>)
        // not consumed by a controlled prop.
        var events = new List<EventInfo>();
        foreach (var evt in eventsByName.Values)
        {
            if (consumedEvents.Contains(evt.Name)) continue;
            if (authorDeclared.Contains("On" + evt.Name)) continue;
            if (exclude.Contains(evt.Name)) continue;
            // AutoDiscover=false ⇒ only surface explicitly Included events (symmetric
            // with value props). Without this, full wrapper generation of any real
            // FrameworkElement would surface the entire UIElement event surface
            // (GotFocus/Loaded/Unloaded/…) as On{Event} callbacks the author never asked for.
            if (!autoDiscover && !include.Contains(evt.Name)) continue;
            if (evt.Type is not INamedTypeSymbol del) continue;

            var delFqn = del.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isRouted = delFqn == "global::Microsoft.UI.Xaml.RoutedEventHandler";
            var delOpenFqn = del.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isTyped = delOpenFqn.StartsWith("global::Windows.Foundation.TypedEventHandler<");
            var isEventHandlerOfT = delOpenFqn.StartsWith("global::System.EventHandler<");
            // [WrapEvent] makes an otherwise-unsupported delegate (e.g.
            // ExceptionRoutedEventHandler / NavigationFailedEventHandler)
            // surfaceable AND turns the trampoline into a named-property projection.
            var hasWrapEvent = eventArgs.TryGetValue(evt.Name, out var argProps);

            // Auto whole-args (full-wrapper mode only): a typed event
            // (TypedEventHandler<S,A> / EventHandler<A>) defaults to Action<A> with
            // no [WrapEvent] needed — A is the delegate's 2nd Invoke parameter.
            // RoutedEventHandler stays a parameterless Action (RoutedEventArgs is
            // rarely useful; Click-style ergonomics want Action), and uninteresting
            // args (object) are dropped to parameterless too. Descriptor-only built-ins
            // are NOT auto-surfaced — they own their On{Event} signatures and opt in
            // explicitly via [WrapEvent] — so this never disturbs migrated controls.
            var argsType = del.DelegateInvokeMethod?.Parameters is { Length: 2 } aps ? aps[1].Type : null;
            var autoTyped = !suppressDp && (isTyped || isEventHandlerOfT)
                && argsType is not null && !IsUninterestingArgs(argsType);

            if (isRouted || isTyped || hasWrapEvent || autoTyped)
            {
                var argPropArr = ImmutableArray<string>.Empty;
                var argTypeArr = ImmutableArray<string>.Empty;
                if (hasWrapEvent && argProps is not null)
                {
                    // [WrapEvent(Arg/Args)] — project named properties off the args object.
                    argPropArr = argProps.ToImmutableArray();
                    argTypeArr = argProps.Select(p =>
                        argsType?.GetMembers(p).OfType<IPropertySymbol>().FirstOrDefault()
                                ?.Type.ToDisplayString(NullableFqnFormat) ?? "object").ToImmutableArray();
                }
                else if (((hasWrapEvent && argProps is null) || autoTyped) && argsType is not null)
                {
                    // Whole-args: bare [WrapEvent] OR an auto-surfaced typed event ⇒ Action<TArgs>.
                    argTypeArr = ImmutableArray.Create(argsType.ToDisplayString(NullableFqnFormat));
                }
                // else: routed / unresolved ⇒ parameterless Action.
                events.Add(new EventInfo(evt.Name, delFqn, !argTypeArr.IsDefaultOrEmpty, argPropArr, argTypeArr));
            }
        }

        props.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        events.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return (props.ToImmutableArray(), contentProp, panelChildren, items, events.ToImmutableArray());
    }

    // True when the control is an items host — exposes a public instance
    // property named "Items" whose type is (or implements) IList<object>
    // (ItemsControl-derived ListBox/ComboBox/ListView/GridView/Pivot expose an
    // ItemCollection; RadioButtons/SelectorBar expose a bare IList<object>).
    // Only a public getter is required (the items collection has no setter).
    // True when the type is the Reactor Element base (Element or Element?).
    private static bool IsElementType(ITypeSymbol type)
    {
        var fq = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                     .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fq == "global::Microsoft.UI.Reactor.Core.Element";
    }

    // True for a non-nullable value type (bool/int/double/enum/struct) that is
    // NOT Nullable<T> and NOT the spec-050 Optional<T> — i.e. a prop the record
    // declares with a default and the descriptor writes unconditionally.
    private static bool IsNonNullableValueType(ITypeSymbol type)
    {
        if (!type.IsValueType) return false;
        if (type is INamedTypeSymbol n)
        {
            if (n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return false;
            if (n.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .StartsWith("global::Microsoft.UI.Reactor.Optional<")) return false;
        }
        return true;
    }

    // True when the record declares the prop as nullable — either Nullable<T>
    // (double?, bool?, FontWeight?, …) or a nullable-annotated reference type
    // (FontFamily?, Brush?, …). Used by the opt-in ClearValue channel to target
    // only props that have an Unset state to release.
    private static bool IsNullableType(ITypeSymbol type)
    {
        if (type.IsValueType)
            return type is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        return type.NullableAnnotation == NullableAnnotation.Annotated;
    }

    // Event-args types not worth surfacing to an Action<T> (so the event defaults to
    // a parameterless Action): `object` (e.g. EventHandler<object> like DropDownOpened)
    // and RoutedEventArgs (Click-style — the args carry nothing useful).
    private static bool IsUninterestingArgs(ITypeSymbol argsType)
    {
        var fqn = argsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fqn is "global::System.Object"
            or "object"
            or "global::Microsoft.UI.Xaml.RoutedEventArgs";
    }

    private static bool HasItems(INamedTypeSymbol control)
    {
        for (INamedTypeSymbol? t = control; t is not null; t = t.BaseType)
            foreach (var p in t.GetMembers("Items").OfType<IPropertySymbol>())
                if (!p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public &&
                    p.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
                    ImplementsIListOfObject(p.Type))
                    return true;
        return false;
    }

    // The type is, or implements, System.Collections.Generic.IList<object>.
    private static bool ImplementsIListOfObject(ITypeSymbol type)
    {
        static bool IsIListOfObject(ITypeSymbol t) =>
            t is INamedTypeSymbol { IsGenericType: true } n &&
            n.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IList_T &&
            n.TypeArguments.Length == 1 &&
            n.TypeArguments[0].SpecialType == SpecialType.System_Object;

        if (IsIListOfObject(type)) return true;
        foreach (var i in type.AllInterfaces)
            if (IsIListOfObject(i)) return true;
        return false;
    }

    // True when the control is a panel — exposes a public instance property
    // named "Children" of type Microsoft.UI.Xaml.Controls.UIElementCollection
    // (StackPanel, Canvas, Grid, RelativePanel, VariableSizedWrapGrid, …).
    private static bool HasPanelChildren(INamedTypeSymbol control)
    {
        for (INamedTypeSymbol? t = control; t is not null; t = t.BaseType)
            foreach (var p in t.GetMembers("Children").OfType<IPropertySymbol>())
                if (!p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public &&
                    p.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
                    p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        "global::Microsoft.UI.Xaml.Controls.UIElementCollection")
                    return true;
        return false;
    }

    // Resolve a controlled prop's change event(s). Every candidate name must
    // bind to a public two-parameter delegate event; on success returns the
    // resolved event names (in declaration order) and the TArgs type read from
    // the *first* event's delegate (the value itself is read back from the
    // control property, so all events may share a value-less args type such as
    // RoutedEventArgs). A single-event controlled prop is just the length-1 case.
    private static bool TryResolveControlledEvents(
        IReadOnlyDictionary<string, IEventSymbol> eventsByName,
        IReadOnlyList<string> candidateNames,
        out ImmutableArray<string> resolved,
        out string? argsType)
    {
        resolved = ImmutableArray<string>.Empty;
        argsType = null;
        if (candidateNames.Count == 0) return false;

        var names = ImmutableArray.CreateBuilder<string>(candidateNames.Count);
        foreach (var name in candidateNames)
        {
            if (!eventsByName.TryGetValue(name, out var evt) ||
                evt.Type is not INamedTypeSymbol del ||
                del.DelegateInvokeMethod is not { Parameters.Length: 2 } invoke)
                return false;
            if (argsType is null)
                argsType = invoke.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            names.Add(evt.Name);
        }

        resolved = names.ToImmutable();
        return true;
    }

    // Discover the single-content slot property: an explicit [WrapContent]
    // override, else the control's [ContentProperty] attribute (Border.Child,
    // ContentControl.Content, …), else a property literally named "Content".
    // Returns null when the resolved property is missing or is a collection
    // (Panel.Children / ItemsControl.Items) — those are panel/items strategies
    // (P3), not a single-content slot.
    private static string? DiscoverContentProperty(INamedTypeSymbol control, string? wrapContent)
    {
        // Explicit override wins — accept any single-content-typed (object or
        // UIElement-derived) property the author named.
        if (wrapContent is not null)
        {
            var op = FindContentProperty(control, wrapContent);
            return op is not null && IsSingleContentType(op.Type) ? wrapContent : null;
        }

        // [ContentProperty] — but only auto-accept when it's unambiguously a
        // child: a UIElement-derived property (Border.Child) or an `object`
        // named exactly "Content" (ContentControl.Content). An `object`
        // content with another name (ToggleSwitch's [ContentProperty("Header")])
        // is a value prop, not a child slot — skip it.
        for (INamedTypeSymbol? t = control; t is not null; t = t.BaseType)
            foreach (var a in t.GetAttributes())
                if (a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        == "global::Microsoft.UI.Xaml.Markup.ContentPropertyAttribute")
                {
                    var name = a.NamedArguments.FirstOrDefault(na => na.Key == "Name").Value.Value as string
                        ?? (a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null);
                    if (name is null) continue;
                    var ap = FindContentProperty(control, name);
                    if (ap is not null && AcceptAutoContent(ap.Type, name)) return name;
                    goto fallback; // found the attribute but it isn't a child slot
                }

        fallback:
        var cp = FindContentProperty(control, "Content");
        return cp is not null && IsSingleContentType(cp.Type) ? "Content" : null;
    }

    private static IPropertySymbol? FindContentProperty(INamedTypeSymbol control, string name)
    {
        for (INamedTypeSymbol? t = control; t is not null; t = t.BaseType)
            foreach (var p in t.GetMembers(name).OfType<IPropertySymbol>())
                if (!p.IsStatic && p.DeclaredAccessibility == Accessibility.Public &&
                    p.SetMethod is { DeclaredAccessibility: Accessibility.Public } && !p.IsIndexer)
                    return p;
        return null;
    }

    // Single content = object or a UIElement-derived type (a collection like
    // UIElementCollection is a panel/items slot, not this).
    private static bool IsSingleContentType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Object) return true;
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.UI.Xaml.UIElement")
                return true;
        return false;
    }

    // Auto-accept (no override): UIElement-derived (unambiguous child), or an
    // `object` named exactly "Content". Other `object` content (e.g. a Header)
    // is a value prop.
    private static bool AcceptAutoContent(ITypeSymbol type, string name)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.UI.Xaml.UIElement")
                return true;
        return type.SpecialType == SpecialType.System_Object && name == "Content";
    }

    private static IEnumerable<INamedTypeSymbol> ControlChain(INamedTypeSymbol control, bool trimFrameworkBase = false)
    {
        for (INamedTypeSymbol? t = control; t is not null; t = t.BaseType)
        {
            var fq = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (fq == "global::Microsoft.UI.Xaml.Controls.Control")
                yield break;
            // Full-wrapper mode: also stop at the FrameworkElement-rooted bases that
            // are NOT Control — ContentPresenter (ConstrainedBox, SwitchPresenter),
            // Panel (DockPanel, WrapPanel, …) and FrameworkElement itself. This keeps
            // ContentPresenter/Panel-derived wrappers surfacing only their OWN
            // members; the UIElement/FrameworkElement layout & input plumbing (Width,
            // Margin, Background, AllowDrop, Loaded, PointerPressed, …) is modeled by
            // Reactor's generic element modifiers, not per-control props/events — the
            // same boundary Control-derived controls already get via the Control
            // cutoff. (Descriptor-only mode keeps the full walk and filters by
            // record-declared members, so built-ins are unaffected.)
            if (trimFrameworkBase && fq is
                    "global::Microsoft.UI.Xaml.FrameworkElement" or
                    "global::Microsoft.UI.Xaml.Controls.ContentPresenter" or
                    "global::Microsoft.UI.Xaml.Controls.Panel")
                yield break;
            yield return t;
        }
    }

    // Locate the `{prop}Property` public static DependencyProperty field on the
    // control or a base type, and return a fully-qualified reference to it (so
    // the one-way descriptor entry can ClearValue on Unset). Null if none.
    // Locate the `{prop}Property` public static DependencyProperty member on the
    // control or a base type, and return a fully-qualified reference to it (so
    // the one-way descriptor entry can ClearValue on Unset). Handles both a
    // FIELD (`public static readonly DependencyProperty XProperty` — hand-written
    // controls) and a static PROPERTY (the CsWinRT projection shape for built-in
    // WinUI controls). Null if none.
    //
    // <para>NOTE: the field-only <see cref="FindDependencyProperty"/> is kept for
    // the full-wrapper path so its behaviour (WinUI projected DPs are properties,
    // so the dp channel is NOT auto-selected there) is unchanged; this
    // property-aware variant is used only by the opt-in descriptor-only
    // ClearValueOnUnset pass.</para>
    private static string? FindDependencyPropertyMember(INamedTypeSymbol control, string prop)
    {
        for (INamedTypeSymbol? t = control; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers(prop + "Property"))
            {
                ITypeSymbol? mt = m switch
                {
                    IFieldSymbol f when f.IsStatic && f.DeclaredAccessibility == Accessibility.Public => f.Type,
                    IPropertySymbol p when p.IsStatic && p.DeclaredAccessibility == Accessibility.Public && p.GetMethod is not null => p.Type,
                    _ => null,
                };
                if (mt is not null &&
                    mt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.UI.Xaml.DependencyProperty")
                    return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + prop + "Property";
            }
        }
        return null;
    }

    private static string? FindDependencyProperty(INamedTypeSymbol control, string prop)
    {
        for (INamedTypeSymbol? t = control; t is not null; t = t.BaseType)
        {
            foreach (var f in t.GetMembers(prop + "Property").OfType<IFieldSymbol>())
                if (f.IsStatic && f.DeclaredAccessibility == Accessibility.Public &&
                    f.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.UI.Xaml.DependencyProperty")
                    return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + prop + "Property";
        }
        return null;
    }

    private readonly record struct Classified(string ValueType, string ElementType, bool IsReference, bool IsObject);

    // The single public one-parameter instance constructor's parameter type
    // (e.g. CornerRadius(double) ⇒ double, Thickness(double) ⇒ double,
    // GridLength(double) ⇒ double). Null when there is no unambiguous one-arg
    // ctor. Used by [WrapConvert] to infer the ergonomic scalar element type.
    private static ITypeSymbol? TryGetSingleArgCtorParamType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return null;
        ITypeSymbol? found = null;
        foreach (var ctor in named.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility != Accessibility.Public) continue;
            if (ctor.Parameters.Length != 1) continue;
            if (found is not null) return null; // ambiguous — more than one 1-arg ctor
            found = ctor.Parameters[0].Type;
        }
        return found;
    }

    private static Classified? Classify(ITypeSymbol type, bool allowCollections = false)
    {
        if (type.SpecialType == SpecialType.System_String)
            return new Classified("string", "string?", IsReference: true, IsObject: false);
        if (type.SpecialType == SpecialType.System_Object)
            return new Classified("string", "string?", IsReference: true, IsObject: true);

        if (type.TypeKind == TypeKind.Enum)
        {
            var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new Classified(fqn, fqn + "?", IsReference: false, IsObject: false);
        }

        var scalar = type.SpecialType switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Double => "double",
            _ => null,
        };
        if (scalar is not null)
            return new Classified(scalar, scalar + "?", false, false);

        var fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Nullable<U> (bool?, int?, DateTimeOffset?, …) — tri-state value where
        // `null` is a meaningful value distinct from "unset". Backed by the
        // spec-050 Optional<U?>: Unset ⇒ don't touch; Of(null) ⇒ write null;
        // Of(v) ⇒ write v. Optional<T> mirrors Nullable<T>'s .Value/.HasValue
        // surface, so the existing one-way/controlled emit handles it as-is.
        if (type is INamedTypeSymbol nullable &&
            nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var vt = nullable.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "?";
            return new Classified(vt, $"global::Microsoft.UI.Reactor.Optional<{vt}>", IsReference: false, IsObject: false);
        }

        // Value-type struct (Thickness, CornerRadius, Color, GridLength, …):
        // surfaced as a nullable one-way prop, written via .Value when set.
        if (type.IsValueType && type is INamedTypeSymbol)
        {
            return new Classified(fq, fq + "?", IsReference: false, IsObject: false);
        }

        // Reference type (Brush, FontFamily, Style, INumberFormatter2, ICommand, …):
        // nullable one-way, written when non-null. Excludes things that must not be
        // written as a raw object: delegates, arrays, data/control templates, and any
        // UIElement-derived type (content/children). Collections are excluded UNLESS
        // allowCollections (full-wrapper): a SETTABLE typed collection prop (e.g.
        // MetadataControl.Items : IEnumerable<MetadataItem>) is a legitimate
        // declarative value assigned wholesale, far nicer than the Setters escape
        // hatch. (Read-only collections — ItemsControl.Items, Panel.Children — have
        // no setter and never reach here; they're items-host / panel strategies.)
        if (type.IsReferenceType && IsSupportedReference(type, fq, allowCollections))
            return new Classified(fq, fq + "?", IsReference: true, IsObject: false);

        return null;
    }

    private static bool IsSupportedReference(ITypeSymbol type, string fq, bool allowCollections = false)
    {
        if (type.TypeKind is TypeKind.Delegate or TypeKind.Array)
            return false;

        switch (fq)
        {
            case "global::Microsoft.UI.Xaml.DataTemplate":
            case "global::Microsoft.UI.Xaml.Controls.ControlTemplate":
            case "global::Microsoft.UI.Xaml.ResourceDictionary":
            case "global::Microsoft.UI.Xaml.Controls.DataTemplateSelector":
                return false;
        }

        // Collections — a raw write of a collection isn't a declarative value prop,
        // UNLESS allowCollections (full-wrapper): a settable typed collection prop is
        // assigned wholesale and IS declarative. Check the type itself (e.g. the
        // IEnumerable interface) as well as its implemented interfaces — AllInterfaces
        // does not include the type itself, so an IEnumerable-typed prop would
        // otherwise slip through now that plain interfaces are allowed.
        if (!allowCollections &&
            (type.Name == "IEnumerable" || type.AllInterfaces.Any(i => i.Name == "IEnumerable")))
            return false;

        // UIElement-derived (Border.Child, IconElement, …) are content slots,
        // not one-way value props.
        for (var b = type; b is not null; b = b.BaseType)
            if (b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.UI.Xaml.UIElement")
                return false;

        return true;
    }

    // Spec 058 §15 (P5.28) — emit a monomorphic custom-lifecycle decorator for a
    // [WrapDecorator] element: the control is produced once by the author's `Create`
    // method, mutated in place by the optional `OnUpdate`, and torn down by the
    // optional `OnUnmount` before DetachReactorState + SkipPool. Registers via the
    // Pattern-A static cctor → ControlRegistry.RegisterDecorator<TElement>.
    private static string EmitDecorator(
        string? ns,
        string elementName,
        string controlFqn,
        string create,
        string? onUpdate,
        string? onUnmount)
    {
        const string UIElement = "global::Microsoft.UI.Xaml.UIElement";
        const string Reconciler = "global::Microsoft.UI.Reactor.Core.Reconciler";
        const string V1 = "global::Microsoft.UI.Reactor.Core.V1Protocol";
        var decorator = $"{V1}.IDecoratorElementHandler<{elementName}>";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (ns is not null) { sb.AppendLine($"namespace {ns};"); sb.AppendLine(); }

        sb.AppendLine($"/// <summary>Generated monomorphic Reactor decorator (spec 058 §15, P5.28) for <see cref=\"{elementName}\"/>: the <see cref=\"{controlFqn}\"/> is created once and mutated in place.</summary>");
        sb.AppendLine($"partial record {elementName}");
        sb.AppendLine("{");

        sb.AppendLine($"    private sealed class __DecoratorHandler : {decorator}");
        sb.AppendLine("    {");

        // Mount
        sb.AppendLine($"        public {UIElement} Mount({V1}.MountContext ctx, {elementName} element)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var __c = {elementName}.{create}(element);");
        sb.AppendLine($"            {Reconciler}.SetElementTag(__c, element);");
        sb.AppendLine("            return __c;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Update — in place; the returned control is always the same instance.
        sb.AppendLine($"        public {UIElement} Update({V1}.UpdateContext ctx, {elementName} oldEl, {elementName} newEl, {UIElement} control)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var __c = ({controlFqn})control;");
        if (onUpdate is not null) sb.AppendLine($"            {elementName}.{onUpdate}(oldEl, newEl, __c);");
        sb.AppendLine($"            {Reconciler}.SetElementTag(__c, newEl);");
        sb.AppendLine("            return __c;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Unmount — author-owned interop control: optional teardown, detach, SkipPool.
        sb.AppendLine($"        public {V1}.V1UnmountDisposition Unmount({V1}.UnmountContext ctx, {elementName}? element, {UIElement} control)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var __c = ({controlFqn})control;");
        if (onUnmount is not null) sb.AppendLine($"            {elementName}.{onUnmount}(__c);");
        sb.AppendLine($"            {Reconciler}.DetachReactorState(__c);");
        sb.AppendLine($"            return {V1}.V1UnmountDisposition.SkipPool;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Pattern-A registration rooted on the element type.
        sb.AppendLine($"    static {elementName}()");
        sb.AppendLine("    {");
        sb.AppendLine($"        {V1}.ControlRegistry.RegisterDecorator<{elementName}>(static () => new __DecoratorHandler());");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Spec 058 §15 (P5.27) — emit a decorator-style handler for a [WrapPolymorphic]
    // element: the concrete control is produced by the author's `Resolve` method
    // (not `new TControl()`), patched in place by the optional `Reconcile` method
    // when the runtime control type is unchanged, and rebuilt otherwise. Registers
    // via the Pattern-A static cctor → ControlRegistry.RegisterDecorator<TElement>.
    private static string EmitPolymorphic(
        string? ns,
        string elementName,
        string controlBaseFqn,
        string resolve,
        string? reconcile,
        string? emptySentinel,
        bool hasSetters)
    {
        const string UIElement = "global::Microsoft.UI.Xaml.UIElement";
        const string Reconciler = "global::Microsoft.UI.Reactor.Core.Reconciler";
        const string V1 = "global::Microsoft.UI.Reactor.Core.V1Protocol";
        const string TextBlock = "global::Microsoft.UI.Xaml.Controls.TextBlock";
        var decorator = $"{V1}.IDecoratorElementHandler<{elementName}>";
        var empty = emptySentinel is not null
            ? $"{elementName}.{emptySentinel}()"
            : $"new {TextBlock} {{ Text = string.Empty }}";
        var reconcileCheck = reconcile is not null
            ? $"\n                || !{elementName}.{reconcile}(oldEl, newEl, __typed)"
            : string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (ns is not null) { sb.AppendLine($"namespace {ns};"); sb.AppendLine(); }

        sb.AppendLine($"/// <summary>Generated polymorphic Reactor decorator (spec 058 §15, P5.27) for <see cref=\"{elementName}\"/>: the concrete <see cref=\"{controlBaseFqn}\"/> subtype is resolved at runtime.</summary>");
        sb.AppendLine($"partial record {elementName}");
        sb.AppendLine("{");

        sb.AppendLine($"    private sealed class __PolymorphicHandler : {decorator}");
        sb.AppendLine("    {");

        // Mount
        sb.AppendLine($"        public {UIElement} Mount({V1}.MountContext ctx, {elementName} element)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var __c = {elementName}.{resolve}(element);");
        sb.AppendLine($"            if (__c is null) return {empty};");
        sb.AppendLine($"            {Reconciler}.SetElementTag(__c, element);");
        if (hasSetters) sb.AppendLine($"            {Reconciler}.ApplySetters(element.Setters, __c);");
        sb.AppendLine("            return __c;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Update
        sb.AppendLine($"        public {UIElement} Update({V1}.UpdateContext ctx, {elementName} oldEl, {elementName} newEl, {UIElement} control)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var __fresh = {elementName}.{resolve}(newEl);");
        sb.AppendLine($"            if (__fresh is null) return control is {controlBaseFqn} ? {empty} : control;");
        sb.AppendLine($"            if (control is not {controlBaseFqn} __typed");
        sb.AppendLine($"                || __fresh.GetType() != __typed.GetType(){reconcileCheck})");
        sb.AppendLine("            {");
        sb.AppendLine($"                {Reconciler}.SetElementTag(__fresh, newEl);");
        if (hasSetters) sb.AppendLine($"                {Reconciler}.ApplySetters(newEl.Setters, __fresh);");
        sb.AppendLine("                return __fresh;");
        sb.AppendLine("            }");
        sb.AppendLine($"            {Reconciler}.SetElementTag(__typed, newEl);");
        if (hasSetters) sb.AppendLine($"            {Reconciler}.ApplySetters(newEl.Setters, __typed);");
        sb.AppendLine("            return __typed;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Unmount — polymorphic mounts own a single control with no Reactor children.
        sb.AppendLine($"        public {V1}.V1UnmountDisposition Unmount({V1}.UnmountContext ctx, {elementName}? element, {UIElement} control)");
        sb.AppendLine($"            => {V1}.V1UnmountDisposition.CollectSelf;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Pattern-A registration rooted on the element type.
        sb.AppendLine($"    static {elementName}()");
        sb.AppendLine("    {");
        sb.AppendLine($"        {V1}.ControlRegistry.RegisterDecorator<{elementName}>(static () => new __PolymorphicHandler());");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Emit(
        string? ns,
        string elementName,
        string controlName,
        string controlFqn,
        ImmutableArray<PropInfo> props,
        string? contentProp,
        bool panelChildren,
        bool items,
        ImmutableArray<EventInfo> events,
        bool descriptorOnly,
        bool hasManual,
        string? contentElementName = null,
        bool registerAssembly = true,
        string? panelPerChild = null,
        string? panelAfterAll = null,
        string? lifecycleMount = null,
        string? lifecycleUnmount = null,
        string? contentPropTypeFqn = null,
        IReadOnlyList<ElementSlotInfo>? elementSlots = null)
    {
        const string Element = "global::Microsoft.UI.Reactor.Core.Element";
        const string Action = "global::System.Action";
        const string FrameworkElement = "global::Microsoft.UI.Xaml.FrameworkElement";
        const string UIElement = "global::Microsoft.UI.Xaml.UIElement";
        const string Reconciler = "global::Microsoft.UI.Reactor.Core.Reconciler";
        const string ControlRegistry = "global::Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry";
        const string ChildList = "global::System.Collections.Generic.IReadOnlyList<global::Microsoft.UI.Reactor.Core.Element>";
        const string ItemList = "global::System.Collections.Generic.IReadOnlyList<object>";
        var descType = $"global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<{elementName}, {controlFqn}>";
        var handlerType = $"global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.DescriptorHandler<{elementName}, {controlFqn}>";
        var singleContent = $"global::Microsoft.UI.Reactor.Core.V1Protocol.SingleContent<{elementName}, {controlFqn}>";
        var panel = $"global::Microsoft.UI.Reactor.Core.V1Protocol.Panel<{elementName}, {controlFqn}>";
        var itemsHost = $"global::Microsoft.UI.Reactor.Core.V1Protocol.ItemsHost<{elementName}, {controlFqn}>";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (ns is not null) { sb.AppendLine($"namespace {ns};"); sb.AppendLine(); }

        sb.AppendLine(descriptorOnly
            ? $"/// <summary>Generated Reactor <b>descriptor</b> (spec 058 §15, descriptor-only) for <see cref=\"{controlFqn}\"/> against the existing <see cref=\"{elementName}\"/> record.</summary>"
            : $"/// <summary>Generated Reactor wrapper for <see cref=\"{controlFqn}\"/>.</summary>");
        sb.AppendLine(descriptorOnly
            ? $"partial record {elementName}"
            : $"partial record {elementName} : {Element}");
        sb.AppendLine("{");

        // ── Init properties + controlled callbacks ────────────────────────
        // Suppressed in descriptor-only mode: the existing hand-written record
        // already declares these members; the generator emits only the
        // descriptor + registration that reference them.
        if (!descriptorOnly)
        {
        foreach (var p in props)
        {
            if (p.Controlled)
            {
                sb.AppendLine($"    /// <summary>Controlled <c>{controlName}.{p.Name}</c> (Unset ⇒ control owns it; a value force-asserts).</summary>");
                sb.AppendLine($"    public {p.ElementType} {p.Name} {{ get; init; }}");
                sb.AppendLine($"    /// <summary>Invoked when the user changes <c>{controlName}.{p.Name}</c>.</summary>");
                sb.AppendLine($"    public {Action}<{p.ValueType}>? On{p.Name}Changed {{ get; init; }}");
            }
            else
            {
                sb.AppendLine($"    /// <summary>Maps <c>{controlName}.{p.Name}</c>.</summary>");
                sb.AppendLine($"    public {p.ElementType} {p.Name} {{ get; init; }}");
            }
        }
        if (contentProp is not null)
        {
            sb.AppendLine($"    /// <summary>Single content child (maps <c>{controlName}.{contentProp}</c>).</summary>");
            sb.AppendLine($"    public {Element}? Content {{ get; init; }}");
        }
        if (elementSlots is not null)
            foreach (var slot in elementSlots)
            {
                sb.AppendLine($"    /// <summary>Secondary element slot (mounts to <c>{controlName}.{slot.ControlProp}</c>).</summary>");
                sb.AppendLine($"    public {Element}? {slot.Name} {{ get; init; }}");
            }
        if (panelChildren)
        {
            sb.AppendLine($"    /// <summary>Child elements appended to <c>{controlName}.Children</c>.</summary>");
            sb.AppendLine($"    public {ChildList} Children {{ get; init; }}");
            sb.AppendLine($"        = global::System.Array.Empty<{Element}>();");
        }
        if (items)
        {
            sb.AppendLine($"    /// <summary>Items populated into <c>{controlName}.Items</c> (strings pass through; <c>Element</c> items mount through the reconciler).</summary>");
            sb.AppendLine($"    public {ItemList} Items {{ get; init; }}");
            sb.AppendLine("        = global::System.Array.Empty<object>();");
        }
        foreach (var e in events)
        {
            sb.AppendLine($"    /// <summary>Invoked on <c>{controlName}.{e.Name}</c>.</summary>");
            sb.AppendLine(e.Typed && !e.ArgTypes.IsDefaultOrEmpty
                ? $"    public {Action}<{string.Join(", ", e.ArgTypes)}>? On{e.Name} {{ get; init; }}"
                : $"    public {Action}? On{e.Name} {{ get; init; }}");
        }
        sb.AppendLine("    /// <summary>Imperative escape hatch run after every Mount/Update.</summary>");
        sb.AppendLine($"    public {Action}<{controlFqn}>[] Setters {{ get; init; }}");
        sb.AppendLine($"        = global::System.Array.Empty<{Action}<{controlFqn}>>();");
        sb.AppendLine();
        }

        // ── Event / deferred-controlled payload + trampolines ─────────────
        var deferredProps = props.Where(p => p.Deferred).ToImmutableArray();
        if (events.Length > 0 || deferredProps.Length > 0)
        {
            sb.AppendLine("    private sealed class __EventPayload");
            sb.AppendLine("    {");
            foreach (var e in events)
                sb.AppendLine($"        public {e.DelegateType}? {e.Name}Slot;");
            foreach (var p in deferredProps)
                sb.AppendLine($"        public {p.ControlledDelegate}? {p.Name}ControlledTrampoline;");
            sb.AppendLine("    }");
            sb.AppendLine();
            foreach (var e in events)
            {
                // Typed events ([WrapEvent]) project args.{Arg}(s) — or the whole
                // args when no projection — into the Action<…> callback; untyped
                // events fire a parameterless Action.
                var argParam = e.Typed ? "args" : "_";
                string invokeArgs;
                if (!e.Typed) invokeArgs = "";
                else if (e.ArgProperties.IsDefaultOrEmpty) invokeArgs = "args";
                else invokeArgs = string.Join(", ", e.ArgProperties.Select(p => $"args.{p}"));
                sb.AppendLine($"    private static readonly {e.DelegateType} __{e.Name}Trampoline = static (s, {argParam}) =>");
                sb.AppendLine("    {");
                sb.AppendLine($"        if ({Reconciler}.GetElementTag(({FrameworkElement})(object)s!) is {elementName} live)");
                sb.AppendLine($"            live.On{e.Name}?.Invoke({invokeArgs});");
                sb.AppendLine("    };");
                sb.AppendLine();
            }
            // Deferred (suppress-counter) controlled trampolines: gate on the
            // public ReactorBinding.ShouldSuppressEcho primitive (a programmatic
            // Update write begins a suppress token via WriteSuppressed, so its
            // echoed change event is dropped), then re-read the live control value
            // and fire the user callback. Using the public primitive (not the
            // internal ChangeEchoSuppressor) keeps the emitted code compilable
            // against Reactor's public surface alone — spec 062 §14.
            foreach (var p in deferredProps)
            {
                sb.AppendLine($"    private static readonly {p.ControlledDelegate} __{p.Name}ControlledTrampoline = static (s, _) =>");
                sb.AppendLine("    {");
                sb.AppendLine($"        var __c = ({controlFqn})s!;");
                sb.AppendLine("        if (global::Microsoft.UI.Reactor.Core.ReactorBinding.ShouldSuppressEcho(__c)) return;");
                sb.AppendLine($"        ({Reconciler}.GetElementTag(__c) as {elementName})?.On{p.Name}Changed?.Invoke(__c.{p.ControlProp});");
                sb.AppendLine("    };");
                sb.AppendLine();
            }
        }

        // ── Descriptor ────────────────────────────────────────────────────
        sb.AppendLine($"    /// <summary>The descriptor the engine interprets for every <see cref=\"{elementName}\"/>.</summary>");
        sb.AppendLine($"    public static readonly {descType} Descriptor =");
        // When the author declares [WrapManual], route the auto-built descriptor
        // through their Customize hook so they can chain bespoke entries the
        // generator cannot infer.
        if (hasManual) sb.AppendLine("        Customize(");
        sb.AppendLine($"        new {descType}");
        sb.AppendLine("        {");
        if (contentProp is not null)
        {
            sb.AppendLine($"            Children = new {singleContent}(");
            sb.AppendLine(descriptorOnly
                ? $"                GetChild: static e => e.{contentElementName},"
                : "                GetChild: static e => e.Content,");
            // Down-cast only when the content property is narrower than UIElement
            // (e.g. FrameworkElement) — object/UIElement assign directly.
            var contentCast = contentPropTypeFqn is null or "object"
                    or "global::System.Object" or UIElement
                ? "ui"
                : $"ui as {contentPropTypeFqn}";
            sb.AppendLine($"                SetChild: static (c, ui) => c.{contentProp} = {contentCast})");
            sb.AppendLine("            {");
            sb.AppendLine($"                GetCurrentChild = static c => c.{contentProp} as {UIElement},");
            sb.AppendLine("            },");
        }
        else if (panelChildren)
        {
            sb.AppendLine($"            Children = new {panel}(");
            sb.AppendLine("                GetChildren: static e => e.Children,");
            if (panelPerChild is not null || panelAfterAll is not null)
            {
                // Attached-property panel ([WrapPanelChildren]): wire the per-child
                // and/or two-pass attached-prop hook (a static method on the record).
                sb.AppendLine("                GetCollection: static c => c.Children)");
                sb.AppendLine("            {");
                if (panelPerChild is not null)
                    sb.AppendLine($"                PerChildAttached = {panelPerChild},");
                if (panelAfterAll is not null)
                    sb.AppendLine($"                PerChildAttachedAfterAll = {panelAfterAll},");
                sb.AppendLine("            },");
            }
            else
            {
                sb.AppendLine("                GetCollection: static c => c.Children),");
            }
        }
        else if (items)
        {
            sb.AppendLine($"            Children = new {itemsHost}(");
            sb.AppendLine("                GetItems:      static e => e.Items,");
            sb.AppendLine("                GetCollection: static c => c.Items),");
        }
        sb.AppendLine("            GetSetters = static e => e.Setters,");
        sb.Append("        }");
        // Close Customize around just the descriptor config so the author's
        // entries come FIRST (before the auto entries) — some controls (Slider
        // Min/Max coercion) require their manual writes to precede the auto ones.
        if (hasManual) sb.Append(")");
        foreach (var p in props)
        {
            sb.AppendLine();
            if (p.Controlled && p.Deferred)
            {
                // Deferred (suppress-counter) two-way via HandCodedControlled: the
                // generated trampoline (above) gates on the public
                // ReactorBinding.ShouldSuppressEcho primitive and re-reads the
                // control value. The set is wrapped in WriteSuppressed
                // by the entry, so a programmatic Update write doesn't echo back.
                sb.AppendLine($"        .HandCodedControlled<__EventPayload, {p.ValueType}, {p.ControlledDelegate}>(");
                sb.AppendLine($"            get:         static e => e.{p.Name},");
                sb.AppendLine($"            set:         static (c, v) => c.{p.ControlProp} = v,");
                sb.AppendLine($"            readBack:    static c => c.{p.ControlProp},");
                sb.AppendLine($"            subscribe:   static (c, h) => (({controlFqn})c).{p.ChangedEvents[0]} += h,");
                sb.AppendLine($"            callback:    static e => e.On{p.Name}Changed,");
                sb.AppendLine($"            trampoline:  __{p.Name}ControlledTrampoline,");
                sb.AppendLine($"            slotIsNull:  static p => p.{p.Name}ControlledTrampoline is null,");
                sb.Append($"            setSlot:     static (p, h) => p.{p.Name}ControlledTrampoline = h)");
            }
            else if (p.Controlled)
            {
                // Public .Controlled entry: echo suppression is encapsulated in
                // the entry (the generated code never touches the internal
                // ChangeEchoSuppressor). The (s, e) => h(s, e) closure bridges
                // the native event delegate to EventHandler<TArgs>; unsubscribe
                // is a no-op because the engine's per-control payload gate
                // subscribes exactly once per control lifetime.
                sb.AppendLine($"        .Controlled<{p.ValueType}, {p.ArgsType}>(");
                sb.AppendLine($"            get:         static e => e.{p.Name},");
                sb.AppendLine($"            set:         static (c, v) => c.{p.ControlProp} = v,");
                if (p.ChangedEvents.Length == 1)
                {
                    sb.AppendLine($"            subscribe:   static (fe, h) => (({controlFqn})fe).{p.ChangedEvents[0]} += (s, e) => h(s, e),");
                }
                else
                {
                    // Multi-event two-way (e.g. Checked + Unchecked): wire every
                    // signalling event to the shared handler; the value is read
                    // back from the control property (readBack) after any fires.
                    sb.AppendLine("            subscribe:   static (fe, h) =>");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                var __c = ({controlFqn})fe;");
                    foreach (var ev in p.ChangedEvents)
                        sb.AppendLine($"                __c.{ev} += (s, e) => h(s, e);");
                    sb.AppendLine("            },");
                }
                sb.AppendLine($"            unsubscribe: static (fe, h) => {{ }},");
                sb.AppendLine($"            callback:    static e => e.On{p.Name}Changed,");
                sb.Append($"            readBack:    static c => c.{p.ControlProp})");
            }
            else if (p.Unconditional)
            {
                // Record declares a non-nullable value prop (with a default) →
                // unconditional one-way write, matching the hand-written `.OneWay`.
                sb.Append($"        .OneWay<{p.ValueType}>(static e => e.{p.Name}, static (c, v) => c.{p.ControlProp} = {ConvertRhs(p)})");
            }
            else if (p.Dp is not null)
            {
                // spec-050 Optional<T> + dp: HasValue writes; Unset ⇒ ClearValue(dp).
                // In descriptor-only mode the record prop is `T?` (Nullable / nullable
                // reference), not `Optional<T>`, so adapt the get to project Unset on
                // null (matching the hand-written `.OneWay(get, set, dp)` shape).
                var dpGet = !descriptorOnly
                    ? $"static e => e.{p.Name}"
                    : p.IsReference
                        ? $"static e => e.{p.Name} is null ? global::Microsoft.UI.Reactor.Optional<{p.ValueType}>.Unset : e.{p.Name}"
                        : $"static e => e.{p.Name}.HasValue ? e.{p.Name}.Value : global::Microsoft.UI.Reactor.Optional<{p.ValueType}>.Unset";
                sb.Append($"        .OneWay<{p.ValueType}>({dpGet}, static (c, v) => c.{p.ControlProp} = {ConvertRhs(p)}, {p.Dp})");
            }
            else if (p.IsReference)
            {
                sb.Append($"        .OneWayConditional<{p.ValueType}>(static e => e.{p.Name}!, static (c, v) => c.{p.ControlProp} = {ConvertRhs(p)}, static e => e.{p.Name} is not null)");
            }
            else
            {
                sb.Append($"        .OneWayConditional<{p.ValueType}>(static e => e.{p.Name}!.Value, static (c, v) => c.{p.ControlProp} = {ConvertRhs(p)}, static e => e.{p.Name}.HasValue)");
            }
        }
        foreach (var e in events)
        {
            sb.AppendLine();
            sb.AppendLine($"        .HandCodedEvent<__EventPayload, {e.DelegateType}>(");
            sb.AppendLine($"            subscribe: static (c, h) => c.{e.Name} += h,");
            sb.AppendLine($"            callbackPresent: static el => el.On{e.Name},");
            sb.AppendLine($"            trampoline: __{e.Name}Trampoline,");
            sb.AppendLine($"            slotIsNull: static p => p.{e.Name}Slot is null,");
            sb.Append($"            setSlot: static (p, h) => p.{e.Name}Slot = h)");
        }
        // [WrapElementSlot] — secondary single-element slots. Each emits an
        // .ImperativeBridged entry: mount via the public ctx.MountChild, update via the
        // public ctx.ReconcileChild (state-preserving reconcile). The mounted UIElement is
        // down-cast to the control property's type only when it is narrower than UIElement
        // (object/UIElement assign directly). These write DEDICATED control properties, so
        // their position in the post-Customize auto chain is order-independent.
        if (elementSlots is not null)
            foreach (var slot in elementSlots)
            {
                var narrow = slot.ControlPropTypeFqn is not (null or "object"
                    or "global::System.Object" or UIElement);
                // Narrow control prop (e.g. IconElement): hard-cast the mounted UIElement so a
                // genuine type mismatch throws InvalidCastException instead of silently nulling
                // the child. The `!` only suppresses the UIElement? nullability of MountChild /
                // ReconcileChild — casting an actual null reference still yields null (no throw).
                var mountExpr = narrow
                    ? $"({slot.ControlPropTypeFqn})ctx.MountChild(e.{slot.Name})!"
                    : $"ctx.MountChild(e.{slot.Name})";
                var nextExpr = narrow ? $"({slot.ControlPropTypeFqn})__next!" : "__next";
                sb.AppendLine();
                sb.AppendLine("        .ImperativeBridged(");
                sb.AppendLine($"            mount: static (ctx, c, e) => {{ if (e.{slot.Name} is not null) c.{slot.ControlProp} = {mountExpr}; }},");
                sb.AppendLine("            update: static (ctx, c, __o, __n) =>");
                sb.AppendLine("            {");
                sb.AppendLine($"                if (__o.{slot.Name} is null && __n.{slot.Name} is null) return;");
                sb.AppendLine($"                if (__n.{slot.Name} is null)");
                sb.AppendLine("                {");
                sb.AppendLine($"                    if (__o.{slot.Name} is not null) ctx.ReconcileChild(__o.{slot.Name}, null, c.{slot.ControlProp} as {UIElement});");
                sb.AppendLine($"                    c.{slot.ControlProp} = null!;");
                sb.AppendLine("                    return;");
                sb.AppendLine("                }");
                sb.AppendLine($"                var __existing = c.{slot.ControlProp} as {UIElement};");
                sb.AppendLine($"                var __next = ctx.ReconcileChild(__o.{slot.Name}, __n.{slot.Name}, __existing);");
                sb.Append($"                if (!ReferenceEquals(__existing, __next)) c.{slot.ControlProp} = {nextExpr};\n            }})");
            }
        sb.AppendLine(";");
        sb.AppendLine();

        // Author hook (mandatory when [WrapManual] is present): chain the bespoke
        // descriptor entries the generator can't infer. ControlDescriptor fluent
        // methods mutate-and-return-self, so `=> d.HandCodedControlled(...)` works.
        if (hasManual)
        {
            sb.AppendLine($"    /// <summary>Author hook — add the bespoke descriptor entries for the <c>[WrapManual]</c> props.</summary>");
            sb.AppendLine($"    private static partial {descType} Customize({descType} d);");
            sb.AppendLine();
        }

        // ── Pattern-A registration (rooted on the element type) ───────────
        // Also register the control library's XAML metadata provider so the
        // WinUI loader can resolve the control's Themes/Generic.xaml when a
        // Reactor app has no XAML of its own (issue #142). Runs during the
        // first Render (when the factory is called) — before the control is
        // realized and its default style loads.
        sb.AppendLine($"    static {elementName}()");
        sb.AppendLine("    {");
        // RegisterControlAssembly (issue #142) is for third-party control
        // libraries whose Themes/Generic.xaml must be resolvable. Built-in WinUI
        // controls (the descriptor-only migration targets) already have their
        // XAML metadata loaded by the framework, and the call is unsafe in a
        // headless host — so it is emitted only for full wrapper generation.
        // Use the TOLERANT TryRegister: a pure-code control library (code-only
        // panels with no XAML, e.g. WCT Primitives) has no metadata provider and
        // needs none — registering must not throw.
        if (!descriptorOnly && registerAssembly)
            sb.AppendLine($"        global::Microsoft.UI.Reactor.ReactorApp.TryRegisterControlAssembly(typeof({controlFqn}).Assembly);");
        sb.AppendLine($"        {ControlRegistry}.Register<{elementName}, {controlFqn}>(");
        sb.AppendLine($"            static () => new {handlerType}(Descriptor));");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ── Parameterized factory (static method on the element type) ──────
        // Suppressed in descriptor-only mode: the existing record keeps its own
        // hand-written global factory (which flips its V1.Reg<> to this handler).
        if (!descriptorOnly)
        {
        var factoryParams = new List<string>();
        var assigns = new List<string>();
        foreach (var p in props)
        {
            var arg = CamelEscaped(p.Name);
            factoryParams.Add($"{p.ElementType} {arg} = default");
            assigns.Add($"{p.Name} = {arg}");
            if (p.Controlled)
            {
                var cb = "on" + p.Name + "Changed";
                factoryParams.Add($"{Action}<{p.ValueType}>? {cb} = null");
                assigns.Add($"On{p.Name}Changed = {cb}");
            }
        }
        if (contentProp is not null)
        {
            factoryParams.Add($"{Element}? content = null");
            assigns.Add("Content = content");
        }
        if (elementSlots is not null)
            foreach (var slot in elementSlots)
            {
                var arg = CamelEscaped(slot.Name);
                factoryParams.Add($"{Element}? {arg} = null");
                assigns.Add($"{slot.Name} = {arg}");
            }
        foreach (var e in events)
        {
            var arg = "on" + e.Name;
            factoryParams.Add(e.Typed && !e.ArgTypes.IsDefaultOrEmpty
                ? $"{Action}<{string.Join(", ", e.ArgTypes)}>? {arg} = null"
                : $"{Action}? {arg} = null");
            assigns.Add($"On{e.Name} = {arg}");
        }
        if (panelChildren)
        {
            // `params` must be last — added after every optional parameter.
            factoryParams.Add($"params {Element}[] children");
            assigns.Add("Children = children");
        }
        if (items)
        {
            // `params` must be last — added after every optional parameter.
            factoryParams.Add("params object[] items");
            assigns.Add("Items = items");
        }

        sb.AppendLine($"    /// <summary>Creates a <see cref=\"{elementName}\"/> (and guarantees the handler is registered).</summary>");
        // [WrapLifecycle] — wrap the freshly-built element with the imperative
        // mount/unmount wiring via the .OnMount/.OnUnmount modifiers, emitted as
        // fully-qualified static calls (the generated file has no usings).
        // (spec 058 §15 / P5.30)
        const string EE = "global::Microsoft.UI.Reactor.ElementExtensions";
        string WithLifecycle(string ctorExpr)
        {
            var expr = ctorExpr;
            if (lifecycleMount is not null)
                expr = $"{EE}.OnMountAdd({expr}, static __fe => {lifecycleMount}(({controlFqn})__fe))";
            if (lifecycleUnmount is not null)
                expr = $"{EE}.OnUnmountAdd({expr}, static __fe => {lifecycleUnmount}(({controlFqn})__fe))";
            return expr;
        }
        if (factoryParams.Count == 0)
        {
            // Explicitly-typed ctor when wrapped, so OnMountAdd<T> can infer T.
            var ctor = lifecycleMount is not null ? $"new {elementName}()" : "new()";
            sb.AppendLine($"    public static {elementName} {controlName}() => {WithLifecycle(ctor)};");
        }
        else
        {
            var ctor = lifecycleMount is not null
                ? $"new {elementName}() {{ {string.Join(", ", assigns)} }}"
                : $"new() {{ {string.Join(", ", assigns)} }}";
            sb.AppendLine($"    public static {elementName} {controlName}(");
            sb.AppendLine("        " + string.Join(",\n        ", factoryParams));
            sb.AppendLine($"    ) => {WithLifecycle(ctor)};");
        }
        }

        if (!descriptorOnly)
        {
            // Strongly-typed imperative escape hatch — for behavior the declarative
            // surface can't model (building a control subtree like TabbedCommandBar's
            // tabs, or two-way wiring a binding-only control). Reads `.Set(c => …)`
            // instead of `with { Setters = new Action<TControl>[] { … } }`. Chainable.
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>Appends an action run against the live <see cref=\"{controlName}\"/> after every Mount/Update (imperative escape hatch). Chainable.</summary>");
            sb.AppendLine($"    public {elementName} Set({Action}<{controlFqn}> configure) => this with {{ Setters = [.. Setters, configure] }};");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // The set-RHS for a one-way prop: the raw value `v`, or — when [WrapConvert]
    // applies — the struct constructed from the scalar value, `new Struct(v)`.
    private static string ConvertRhs(PropInfo p) => p.Convert is null ? "v" : $"new {p.Convert}(v)";

    private static string CamelEscaped(string name)
    {
        var camel = name.Length > 0 ? char.ToLowerInvariant(name[0]) + name.Substring(1) : name;
        return SyntaxFacts.GetKeywordKind(camel) != SyntaxKind.None ||
               SyntaxFacts.GetContextualKeywordKind(camel) != SyntaxKind.None
            ? "@" + camel
            : camel;
    }

    private sealed record PropInfo(
        string Name,
        string ControlProp,
        string ValueType,
        string ElementType,
        bool IsReference,
        bool Controlled,
        ImmutableArray<string> ChangedEvents,
        string? ArgsType,
        string? Dp,
        string? Convert = null,
        bool Unconditional = false,
        bool Deferred = false,
        string? ControlledDelegate = null);

    private sealed record EventInfo(string Name, string DelegateType, bool Typed = false, ImmutableArray<string> ArgProperties = default, ImmutableArray<string> ArgTypes = default);

    // A [WrapElementSlot] secondary element slot: the element-facing Element? property
    // name, the control property the mounted element is assigned to, and that control
    // property's type FQN (used to decide whether a down-cast from UIElement is needed).
    private sealed record ElementSlotInfo(string Name, string ControlProp, string? ControlPropTypeFqn);

    private sealed record WrapperModel(string? HintName, string? Source, Diagnostic? Diagnostic)
    {
        public static WrapperModel Ok(string hint, string source) => new(hint, source, null);
        public static WrapperModel OkWithDiagnostic(string hint, string source, Diagnostic d) => new(hint, source, d);
        public static WrapperModel Error(Diagnostic d) => new(null, null, d);
    }
}
