// Authoring attributes for Reactor.Wrappers.Generator.
//
// These were previously emitted into every consuming compilation by the source
// generator via RegisterPostInitializationOutput. Because Reactor.dll runs the
// generator too, the (internal) generated copies landed in Reactor.dll and then
// leaked through InternalsVisibleTo into friend assemblies that ALSO run the
// generator (e.g. Reactor.AppTests.Host), where the locally generated copy
// collided with the imported one — CS0436, which fails Release builds under
// TreatWarningsAsErrors. Hosting them here, in a single assembly every consumer
// references, removes the duplicate: the generator binds them by metadata name
// from this assembly instead of synthesizing its own copy.
#nullable enable
namespace Microsoft.UI.Reactor.Wrappers
{
    /// <summary>Marks a partial element record for source-generated Reactor wrapping of <c>controlType</c>.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class GenerateReactorWrapperAttribute : global::System.Attribute
    {
        /// <summary>Marks a partial element record for source-generated wrapping of <paramref name="controlType"/>.</summary>
        public GenerateReactorWrapperAttribute(global::System.Type controlType) { ControlType = controlType; }

        /// <summary>The WinUI / third-party control to wrap.</summary>
        public global::System.Type ControlType { get; }

        /// <summary>When true (default) every settable value property is surfaced; when false only <see cref="Include"/> are.</summary>
        public bool AutoDiscover { get; set; } = true;

        /// <summary>When true (default) the generated registration cctor calls <c>ReactorApp.RegisterControlAssembly</c>
        /// to load the control assembly's XAML metadata (needed for third-party/WCT controls). Set false for built-in
        /// WinUI controls whose metadata the framework already loads — the call throws in a headless host when the
        /// assembly has no <c>IXamlMetadataProvider</c>.</summary>
        public bool RegisterAssembly { get; set; } = true;

        /// <summary>Property names to surface when <see cref="AutoDiscover"/> is false.</summary>
        public string[]? Include { get; set; }

        /// <summary>Property names to drop from auto-discovery.</summary>
        public string[]? Exclude { get; set; }
    }

    /// <summary>Spec 058 §15 (P5) — descriptor-only ("attach") generation. Marks an
    /// <b>existing, author-written</b> partial element record (one that already declares its own
    /// properties and has its own hand-written factory) for source-generated emission of <b>only</b>
    /// the <c>ControlDescriptor</c> + Pattern-A registration for <c>controlType</c> —
    /// no init-properties, no factory. Use to replace a hand-written descriptor/handler with a
    /// generated one while preserving the public element-record API and global factory. The
    /// generated descriptor references the record's existing members by name (value props map to the
    /// control by name, or via <see cref="WrapAliasAttribute"/>; the content slot maps to the
    /// control's content property; <c>Setters</c>/<c>On{Event}</c> must already exist on the record).</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class GenerateReactorDescriptorAttribute : global::System.Attribute
    {
        /// <summary>Marks an existing partial element record for descriptor-only generation against <paramref name="controlType"/>.</summary>
        public GenerateReactorDescriptorAttribute(global::System.Type controlType) { ControlType = controlType; }

        /// <summary>The WinUI / third-party control whose descriptor to generate.</summary>
        public global::System.Type ControlType { get; }

        /// <summary>When true (default) every settable value property is surfaced; when false only <see cref="Include"/> are.</summary>
        public bool AutoDiscover { get; set; } = true;

        /// <summary>When true, each <b>nullable</b> record prop backed by a <c>{ControlProp}Property</c> dependency
        /// property is routed through the <c>Optional&lt;T&gt;</c> + <c>dp.ClearValue</c> channel (Unset releases the
        /// local value back to the theme/style precedence chain) instead of the skip-write <c>OneWayConditional</c>.
        /// Required for styling controls (e.g. TextBlock) that must clear stale values on a recycled control (issue #522).
        /// Default false.</summary>
        public bool ClearValueOnUnset { get; set; }

        /// <summary>Property names to surface when <see cref="AutoDiscover"/> is false.</summary>
        public string[]? Include { get; set; }

        /// <summary>Property names to drop from auto-discovery.</summary>
        public string[]? Exclude { get; set; }
    }

    /// <summary>Forces <c>property</c> to be a controlled (two-way) prop, binding it to
    /// <see cref="ChangedEvent"/> (or <c>{property}Changed</c> when unset), or to the multiple events named
    /// in <see cref="Events"/>. Use a single <see cref="ChangedEvent"/> when the change event does not follow
    /// the <c>{Prop}Changed</c> convention (e.g. ToggleSwitch.IsOn ↔ Toggled); use <see cref="Events"/> when
    /// the two-way value is signalled by several events (e.g. CheckBox/RadioButton IsChecked ↔ Checked +
    /// Unchecked). With <see cref="Events"/> the new value is read back from the control property after any of
    /// the events fire.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WrapControlledAttribute : global::System.Attribute
    {
        /// <summary>Forces <paramref name="property"/> to be a controlled (two-way) prop.</summary>
        public WrapControlledAttribute(string property) { Property = property; }

        /// <summary>The control property to treat as controlled (two-way).</summary>
        public string Property { get; }

        /// <summary>The single change event to bind to. Defaults to <c>{Property}Changed</c> when null. Ignored when <see cref="Events"/> is set.</summary>
        public string? ChangedEvent { get; set; }

        /// <summary>Multiple change events that jointly signal the two-way value (e.g. <c>Checked</c> + <c>Unchecked</c>). When set, the value is read back from the control property after any of them fire. Takes precedence over <see cref="ChangedEvent"/>.</summary>
        public string[]? Events { get; set; }

        /// <summary>Use the <b>deferred / suppress-counter</b> echo channel (<c>HandCodedControlled</c>) instead of the
        /// default synchronous value-diff <c>Controlled</c>. Required for controlled values whose change event is NOT a
        /// synchronous, exact-comparable round-trip — deferred / coercing string boxes such as
        /// <c>PasswordBox.Password</c>, <c>AutoSuggestBox.Text</c>, <c>RichEditBox</c>. The generated trampoline gates on
        /// the public <c>ReactorBinding.ShouldSuppressEcho</c> primitive and re-reads the control value. Single <see cref="ChangedEvent"/> only.</summary>
        public bool Deferred { get; set; }
    }

    /// <summary>Surfaces the control property <c>controlProperty</c> under the friendly
    /// element-facing name <c>name</c> (the generated init-property and factory parameter
    /// use <c>name</c>; the descriptor reads/writes <c>controlProperty</c>).
    /// Use to match bespoke element naming (e.g. <c>Min</c> → <c>Minimum</c>, <c>Content</c> → <c>Text</c>).</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WrapAliasAttribute : global::System.Attribute
    {
        /// <summary>Surfaces <paramref name="controlProperty"/> under the element-facing name <paramref name="name"/>.</summary>
        public WrapAliasAttribute(string name, string controlProperty) { Name = name; ControlProperty = controlProperty; }

        /// <summary>The element-facing name (init-property + factory parameter).</summary>
        public string Name { get; }

        /// <summary>The actual control property the descriptor reads/writes.</summary>
        public string ControlProperty { get; }
    }

    /// <summary>Forces <c>property</c> to be one-way even though it has a matching
    /// <c>{property}Changed</c> event (opts out of two-way auto-pairing — e.g. <c>ProgressBar.Value</c>
    /// is display-only despite <c>ValueChanged</c>).</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WrapOneWayAttribute : global::System.Attribute
    {
        /// <summary>Forces <paramref name="property"/> to be one-way (opts out of two-way auto-pairing).</summary>
        public WrapOneWayAttribute(string property) { Property = property; }

        /// <summary>The control property to keep one-way.</summary>
        public string Property { get; }
    }

    /// <summary>Overrides which control property is the single-content slot (the child the Reactor
    /// <c>content:</c> argument maps to). When absent, the generator reads the control's
    /// <c>[ContentProperty]</c> attribute (e.g. <c>Border.Child</c>), falling back to a
    /// property named <c>Content</c>.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class WrapContentAttribute : global::System.Attribute
    {
        /// <summary>Overrides the single-content slot to <paramref name="property"/>.</summary>
        public WrapContentAttribute(string property) { Property = property; }

        /// <summary>The control property to use as the single-content slot.</summary>
        public string Property { get; }
    }

    /// <summary>Spec 058 §15 (P5) — surfaces a struct-typed control property (e.g. <c>CornerRadius</c>,
    /// <c>BorderThickness</c>) through an ergonomic scalar element property, writing it via the
    /// struct's single-argument constructor. The element-facing value type is inferred from that
    /// constructor's parameter (e.g. <c>Border.CornerRadius</c> of type <c>CornerRadius</c> ⇒ a
    /// <c>double?</c> element prop written as <c>new CornerRadius(v)</c>). General across controls
    /// (CornerRadius/Thickness/GridLength/…) — not a per-control patch.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WrapConvertAttribute : global::System.Attribute
    {
        /// <summary>Surfaces the struct-typed control property <paramref name="property"/> via a scalar element prop.</summary>
        public WrapConvertAttribute(string property) { Property = property; }

        /// <summary>The control property whose struct type is constructed from a scalar element value.</summary>
        public string Property { get; }
    }

    /// <summary>Spec 058 §15 (P5) — marks an element property as <b>manually handled</b>: the generator
    /// excludes it from auto-discovery and instead routes the generated <c>Descriptor</c> through an
    /// author-implemented <c>partial</c> hook
    /// (<c>static partial ControlDescriptor&lt;TElement,TControl&gt; Customize(ControlDescriptor&lt;TElement,TControl&gt; d)</c>)
    /// where the author chains the bespoke entry (e.g. a composite/derived/method-based prop the
    /// generator cannot infer — <c>ScrollViewer.Orientation</c> → multiple scroll props,
    /// <c>RichEditBox.Text</c> → <c>Document.SetText</c>, <c>ToggleButton.CheckedState</c> tri-state).
    /// Declaring any <c>[WrapManual]</c> makes implementing the <c>Customize</c> hook mandatory.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WrapManualAttribute : global::System.Attribute
    {
        /// <summary>Marks <paramref name="property"/> as manually handled in the <c>Customize</c> hook.</summary>
        public WrapManualAttribute(string property) { Property = property; }

        /// <summary>The element property the author handles manually in the <c>Customize</c> hook.</summary>
        public string Property { get; }
    }

    /// <summary>Spec 058 §15 — declares a <b>secondary single-element slot</b>: an <c>Element?</c> property
    /// whose mounted control is assigned to a UIElement-typed (or <c>object</c>) control property, alongside
    /// the control's primary content/children slot. The generator surfaces an <c>Element?</c> init property
    /// (and factory parameter, in full-wrapper mode) named <paramref name="property"/> and emits the
    /// mount/reconcile wiring — the same shape otherwise hand-written as an <c>.ImperativeBridged</c> entry in
    /// <c>Customize</c> (e.g. <c>TabView.TabStripHeader</c>/<c>TabStripFooter</c>, <c>SettingsCard.HeaderIcon</c>).
    /// <para>The element value is reconciled across re-renders (descendant component state preserved) via the
    /// public <c>ctx.ReconcileChild(...)</c> primitive, so the same emit works for both built-in descriptors
    /// and external wrappers. Use <see cref="ControlProperty"/> when the control property name differs from the
    /// element-facing slot name.</para>
    /// <para><b>Not for</b> a slot that shares a control property with a sibling value prop needing precedence
    /// gating (e.g. <c>Expander.HeaderTemplate</c> vs. the string <c>Header</c>) — keep those in <c>Customize</c>,
    /// where write order is explicit.</para></summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WrapElementSlotAttribute : global::System.Attribute
    {
        /// <summary>Declares <paramref name="property"/> as a secondary element slot.</summary>
        public WrapElementSlotAttribute(string property) { Property = property; }

        /// <summary>The element-facing <c>Element?</c> slot property name.</summary>
        public string Property { get; }

        /// <summary>The control property the mounted element is assigned to. Defaults to <see cref="Property"/>.</summary>
        public string? ControlProperty { get; set; }
    }

    /// <summary>Spec 058 §15 — projects an event's args into a <b>typed</b> <c>On{Event}</c> callback.
    /// The generated trampoline invokes <c>On{Event}(args.{Arg})</c> — projecting the named property off
    /// the event-args object (e.g. <c>Image.ImageFailed</c> → <c>OnImageFailed(args.ErrorMessage)</c>,
    /// <c>Action&lt;string&gt;</c>); <see cref="Args"/> projects several into a multi-parameter callback.
    /// <para><b>Note (P5.30):</b> in full-wrapper mode, typed events (<c>TypedEventHandler&lt;S,A&gt;</c> /
    /// <c>EventHandler&lt;A&gt;</c>) with a meaningful <c>A</c> now <i>auto-surface</i> the whole args as
    /// <c>Action&lt;A&gt;</c> with NO attribute — so <c>[WrapEvent]</c> is only needed for: (a) the
    /// <c>Arg</c>/<c>Args</c> property <b>projection</b> above; (b) making a non-standard delegate
    /// surfaceable; or (c) opting a descriptor-only (built-in) record into typed args (auto-surfacing is
    /// scoped to full-wrapper mode). A bare <c>[WrapEvent("Foo")]</c> with no <see cref="Arg"/> still
    /// passes the whole args object (<c>Action&lt;TArgs&gt;</c>).</para></summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WrapEventAttribute : global::System.Attribute
    {
        /// <summary>Projects the args of <paramref name="eventName"/> into a typed <c>On{Event}</c> callback.</summary>
        public WrapEventAttribute(string eventName) { EventName = eventName; }

        /// <summary>The control event whose args are projected to the typed <c>On{Event}</c> callback.</summary>
        public string EventName { get; }

        /// <summary>The property on the event-args object to pass to <c>On{Event}</c> (e.g. <c>ErrorMessage</c>).
        /// When null (and <see cref="Args"/> is null), the whole args object is passed.</summary>
        public string? Arg { get; set; }

        /// <summary>Multiple event-args properties to pass to a multi-parameter <c>On{Event}</c> callback
        /// (e.g. <c>new[]{ "SourcePageType", "Exception" }</c> → <c>Action&lt;Type,Exception&gt;</c>).
        /// Takes precedence over <see cref="Arg"/>.</summary>
        public string[]? Args { get; set; }
    }

    /// <summary>Spec 058 §15 (P5.19) — declares an <b>attached-property panel</b>. The generator
    /// already emits a <c>Panel</c> children strategy for a panel control; this attribute wires the
    /// per-child (or two-pass after-all) attached-property hook onto it, so authors no longer
    /// hand-write the strategy + <c>[WrapManual("Children")]</c> + <c>Customize</c> boilerplate.
    /// <see cref="PerChild"/> names a <c>static void M(TControl panel, UIElement child, Element childElement)</c>
    /// method (Grid.SetRow/SetColumn, Canvas.SetLeft/Top, …); <see cref="AfterAll"/> names a
    /// <c>static void M(TControl panel, IReadOnlyList&lt;(UIElement Mounted, Element ChildElement)&gt; pairs)</c>
    /// method for two-pass sibling-name resolution (RelativePanel). At least one must be set.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = false)]
    public sealed class WrapPanelChildrenAttribute : global::System.Attribute
    {
        /// <summary>Static per-child hook: <c>void M(TControl panel, UIElement child, Element childElement)</c>.</summary>
        public string? PerChild { get; set; }

        /// <summary>Static two-pass hook: <c>void M(TControl panel, IReadOnlyList&lt;(UIElement, Element)&gt; pairs)</c>.</summary>
        public string? AfterAll { get; set; }
    }

    /// <summary>Spec 058 §15 (P5.27) — declares a <b>polymorphic</b> control whose concrete WinUI
    /// type is chosen at runtime from the element's data. Instead of a <c>ControlDescriptor</c> (which
    /// assumes a single fixed control built with <c>new TControl()</c>), the generator emits an
    /// <c>IDecoratorElementHandler&lt;TElement&gt;</c> that resolves the control via the author's
    /// <see cref="Resolve"/> method. The <c>controlType</c> passed to
    /// <see cref="GenerateReactorDescriptorAttribute"/> is the common <b>base</b> every resolved
    /// control derives from (e.g. <c>IconElement</c> for <c>SymbolIcon</c>/<c>FontIcon</c>/…).
    ///
    /// <para>Mount calls <see cref="Resolve"/>; Update re-resolves and either patches in place (when the
    /// runtime control type is unchanged and <see cref="Reconcile"/> returns true) or rebuilds. When the
    /// record declares a <c>Setters</c> member it is applied after every Mount/Update.</para></summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class WrapPolymorphicAttribute : global::System.Attribute
    {
        /// <summary>Declares a polymorphic control resolved at runtime via the <paramref name="resolve"/> method.</summary>
        public WrapPolymorphicAttribute(string resolve) { Resolve = resolve; }

        /// <summary>Name of a <c>static TControlBase? M(TElement)</c> method that produces the concrete
        /// control (used by Mount and by Update's type-change rebuild).</summary>
        public string Resolve { get; }

        /// <summary>Optional name of a <c>static bool M(TElement oldEl, TElement newEl, TControlBase control)</c>
        /// same-subtype patch. Returns <c>false</c> to force a rebuild via <see cref="Resolve"/>. When unset,
        /// every update with an unchanged runtime control type is a no-op patch (setters still re-applied).</summary>
        public string? Reconcile { get; set; }

        /// <summary>Optional name of a <c>static UIElement M()</c> placeholder returned when
        /// <see cref="Resolve"/> yields null. Defaults to an empty <c>TextBlock</c>.</summary>
        public string? EmptySentinel { get; set; }
    }

    /// <summary>Spec 058 §15 (P5.28) — declares a <b>monomorphic custom-lifecycle decorator</b>: a
    /// single-control element whose control is created once and mutated in place (never re-created or
    /// type-swapped), with imperative side-effects the descriptor prop/children model can't express
    /// (e.g. <c>Frame.Navigate</c>, an interop <c>Factory()</c> + <c>Updater</c>). The generator emits
    /// an <c>IDecoratorElementHandler&lt;TElement&gt;</c> (NOT a <c>ControlDescriptor</c>) + a Pattern-A
    /// <c>RegisterDecorator&lt;TElement&gt;</c> cctor. The <c>controlType</c> on
    /// <see cref="GenerateReactorDescriptorAttribute"/> is the control type the handler casts to.
    ///
    /// <para>Mount calls <see cref="Create"/> then tags the control. Update casts the existing control,
    /// runs <see cref="OnUpdate"/> (in-place mutation), re-tags, and returns the <b>same</b> control.
    /// Unmount runs <see cref="OnUnmount"/> (optional teardown), then <c>DetachReactorState</c> and
    /// returns <c>SkipPool</c> (the interop-host disposition — the control is author-owned, not pooled).
    /// Contrast <c>[WrapPolymorphic]</c>, which re-resolves and may rebuild on every update.</para></summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class WrapDecoratorAttribute : global::System.Attribute
    {
        /// <summary>Declares a custom-lifecycle decorator whose control is created via the <paramref name="create"/> method.</summary>
        public WrapDecoratorAttribute(string create) { Create = create; }

        /// <summary>Name of a <c>static TControl M(TElement)</c> method that creates the control and
        /// performs mount-time setup (the generator tags it afterwards).</summary>
        public string Create { get; }

        /// <summary>Optional name of a <c>static void M(TElement oldEl, TElement newEl, TControl control)</c>
        /// in-place update. When unset, Update just re-tags and returns the existing control.</summary>
        public string? OnUpdate { get; set; }

        /// <summary>Optional name of a <c>static void M(TControl control)</c> teardown run before
        /// <c>DetachReactorState</c> at unmount (e.g. <c>frame.Content = null</c>).</summary>
        public string? OnUnmount { get; set; }
    }

    /// <summary>Spec 058 §15 (P5.30) — declares an imperative <b>mount/unmount lifecycle</b> for a
    /// standard wrapped control (one that is otherwise a normal prop/event wrapper, NOT a decorator).
    /// The generated factory wires <see cref="OnMounted"/> (and the optional <see cref="OnUnmounted"/>)
    /// through the element's <c>.OnMount</c> / <c>.OnUnmount</c> modifiers, so a control that must be
    /// started after it mounts and stopped when it unmounts (e.g. <c>CameraPreview.StartAsync</c>) works
    /// with <b>no call-site boilerplate</b> — the consumer just calls the generated factory. Each names a
    /// <c>static void M(TControl)</c> method on the element record (run once on mount / once on unmount).
    /// Generic across any imperative control — not a per-control patch.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class WrapLifecycleAttribute : global::System.Attribute
    {
        /// <summary>Declares a mount/unmount lifecycle whose mount hook is the <paramref name="onMounted"/> method.</summary>
        public WrapLifecycleAttribute(string onMounted) { OnMounted = onMounted; }

        /// <summary>Name of a <c>static void M(TControl)</c> method run once when the control mounts.</summary>
        public string OnMounted { get; }

        /// <summary>Optional name of a <c>static void M(TControl)</c> teardown run once when the control unmounts.</summary>
        public string? OnUnmounted { get; set; }
    }
}
