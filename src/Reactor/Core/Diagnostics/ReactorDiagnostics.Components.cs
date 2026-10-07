using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// A point-in-time description of one mounted component, for inspectors. Every value is text:
/// the snapshot holds no reference into the component, so it can be kept, serialized or sent
/// across a process boundary.
/// </summary>
/// <param name="Name">The component's name: its class (<c>Counter</c>) or, for a render
/// function, where the function is defined (<c>RenderEachTime in MainPage.Render</c>,
/// <c>Memo in MainPage.Render</c>, <c>render in App.Main</c> for a host root).</param>
/// <param name="Kind"><c>"class"</c>, <c>"function"</c> or <c>"memo"</c>.</param>
/// <param name="IsRoot">True for a host's root component (the one passed to <c>Mount</c>).</param>
/// <param name="Props">The props of a <c>Component&lt;TProps&gt;</c> (or the props carried by its
/// element), one row per public property, or one row named <c>Props</c> for a scalar — or for a
/// props type whose members were trimmed away (NativeAOT), described as
/// <c>&lt;Type&gt; (members unavailable)</c>. Empty for function components and propless classes.</param>
/// <param name="State">The component's hooks in call order. <see cref="DiagnosticValue.Index"/> is
/// the index <see cref="ReactorDiagnostics.TrySetState"/> takes.</param>
/// <param name="Contexts">The contexts the component reads through <c>UseContext</c>.</param>
public sealed record ComponentSnapshot(
    string Name,
    string Kind,
    bool IsRoot,
    IReadOnlyList<DiagnosticValue> Props,
    IReadOnlyList<DiagnosticValue> State,
    IReadOnlyList<DiagnosticValue> Contexts);

/// <summary>One prop, hook or context value of a <see cref="ComponentSnapshot"/>, as text.</summary>
/// <param name="Index">Position in its list. For <see cref="ComponentSnapshot.State"/> and
/// <see cref="ComponentSnapshot.Contexts"/> it is the hook's call-order index.</param>
/// <param name="Name">The prop's name, or the context's declaration name (<c>Context&lt;T&gt;</c>
/// when it has none). Empty for hooks: hook names are not recorded.</param>
/// <param name="Kind">For a prop, <c>"prop"</c>. For a hook, <c>"state"</c>, <c>"reducer"</c>,
/// <c>"ref"</c>, <c>"memo"</c>, <c>"effect"</c>, <c>"context"</c>, <c>"persisted"</c>,
/// <c>"navigationLifecycle"</c> or <c>"unknown"</c>. For a context, <c>"default"</c> when the value
/// the component read equals the context's default value (no provider, or a provider supplying
/// that same value), otherwise <c>"provided"</c>.</param>
/// <param name="Type">The value's type as C# spells it (<c>int</c>, <c>int?</c>,
/// <c>List&lt;TaskItem&gt;</c>). Nullable reference annotations are not visible at runtime, so a
/// <c>string?</c> reads <c>string</c>. Empty when the hook has no value.</param>
/// <param name="Value">The value as text: strings quoted, collections summarised as
/// <c>List&lt;T&gt; (n items)</c> (<c>(count unknown)</c> for a sequence that does not advertise
/// its count — it is never enumerated), long values cut at 200 characters, secrets
/// <c>&lt;redacted&gt;</c>. Empty when the hook has no value (effects).</param>
/// <param name="Editable">True when <see cref="ReactorDiagnostics.TrySetState"/> can set it from
/// text: a state, reducer or persisted hook of a text-typed value that is not a secret.</param>
/// <param name="Redacted">True when <paramref name="Value"/> was withheld because the value is a
/// secret: a <c>SecureString</c>; a type, collection element type or name ending in Password,
/// Secret, Credential, Token, ApiKey, PrivateKey or ConnectionString; or a string or object whose own text names such a member with a value (<c>AccessToken=…</c>, a record with a
/// <c>Password</c> property).</param>
/// <param name="MigratedByHotReload">For hooks: true when the most recent hot-reload pass
/// migrated this value to an edited type. Always false for props and contexts.</param>
public sealed record DiagnosticValue(
    int Index,
    string Name,
    string Kind,
    string Type,
    string Value,
    bool Editable,
    bool Redacted,
    bool MigratedByHotReload);

/// <summary>
/// A WinUI property Reactor writes on a control because the element that produced it sets a
/// common modifier, or because Reactor derived it (the default automation name).
/// </summary>
/// <param name="Modifier">The modifier that carries the value: <c>"Width"</c>,
/// <c>"IsVisible"</c>, <c>"Accessibility.HelpText"</c>, or <c>"DefaultAutomationName"</c> for
/// the name Reactor derives from a control's caption.</param>
/// <param name="Property">The WinUI property it is written to, as <c>OwnerType.PropertyName</c>:
/// <c>"FrameworkElement.Width"</c>, <c>"UIElement.Visibility"</c>,
/// <c>"AutomationProperties.Name"</c>.</param>
/// <param name="Value">The element-side value as text. It can differ from the property value when
/// Reactor converts it (<c>IsVisible</c> → <c>Visibility</c>) or folds several modifiers into one
/// property (inline margins into <c>Margin</c>).</param>
public sealed record AppliedProperty(
    string Modifier,
    string Property,
    string Value);

public static partial class ReactorDiagnostics
{
    /// <summary>
    /// Describes the component hosted at <paramref name="element"/>: a component's wrapper
    /// <c>Border</c> (every class, function and memo component is mounted into one), or a host's
    /// root content (its rendered root control, the window content or <c>ContentTarget</c> it was
    /// installed into, or a <c>ReactorHostControl</c> itself). Returns <c>null</c> for any other
    /// element. Works with or without source mapping.
    /// </summary>
    /// <remarks>
    /// <para>Must be called on the UI thread.</para>
    /// <para>One element can anchor two components: when a root renders a component directly
    /// (<c>Render() =&gt; Component&lt;Shell&gt;()</c>), the root's rendered control <em>is</em>
    /// <c>Shell</c>'s wrapper. The wrapper wins — the element describes <c>Shell</c>, the innermost
    /// component it hosts — and <see cref="TrySetState"/> / <see cref="Rerender"/> address the same
    /// component. The root stays reachable through its other anchors: a <c>ContentTarget</c>, a
    /// <c>ReactorHostControl</c>, or the dev-overlay wrapper. A host that installs straight into
    /// <c>Window.Content</c> has none of those, so in that one shape its root is not reachable.</para>
    /// </remarks>
    [Microsoft.UI.Reactor.Hosting.UIThreadOnly]
    public static ComponentSnapshot? DescribeComponent(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        EnsureUIThread(element);
        return ComponentHandle.TryResolve(element, out var handle) ? handle.Describe() : null;
    }

    /// <summary>
    /// Development-time state edit: parses <paramref name="text"/> to the value type of hook
    /// <paramref name="hookIndex"/> of the component hosted at <paramref name="element"/>, writes
    /// it, and — when the value changed — schedules a re-render exactly as the hook's own setter
    /// would.
    /// </summary>
    /// <param name="element">A component wrapper or host root, as for <see cref="DescribeComponent"/>.</param>
    /// <param name="hookIndex">The hook's <see cref="DiagnosticValue.Index"/>.</param>
    /// <param name="text">Invariant-culture text: a number, a single character, <c>true</c>/<c>false</c>, an enum member
    /// name, a date, time span or GUID, or the string itself (unquoted). <c>null</c> sets a
    /// string or nullable to null.</param>
    /// <param name="error">Why the edit was refused, when this returns false.</param>
    /// <returns>True when the value was written (including when it was already equal).</returns>
    /// <remarks>
    /// Only state, reducer and persisted hooks of a text-typed value can be set; secrets never are.
    /// Must be called on the UI thread that renders the component.
    /// </remarks>
    [Microsoft.UI.Reactor.Hosting.UIThreadOnly]
    public static bool TrySetState(UIElement element, int hookIndex, string text, out string? error)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(text);
        EnsureUIThread(element);
        if (!ComponentHandle.TryResolve(element, out var handle))
        {
            error = "no Reactor component is hosted at this element";
            return false;
        }
        return handle.TrySetState(hookIndex, text, out error);
    }

    /// <summary>
    /// Schedules a re-render of the component hosted at <paramref name="element"/>, bypassing its
    /// memoization (the path a state setter takes).
    /// </summary>
    /// <remarks>
    /// A re-render diffs the new element tree against the previous one, so it re-applies a
    /// property only when the element's value for it changed: it does <b>not</b> restore a
    /// property that was edited directly on the control while the element's value stayed the
    /// same. Must be called on the UI thread that renders the component.
    /// </remarks>
    /// <returns>False when no component is hosted at <paramref name="element"/> or it has never rendered.</returns>
    [Microsoft.UI.Reactor.Hosting.UIThreadOnly]
    public static bool Rerender(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        EnsureUIThread(element);
        return ComponentHandle.TryResolve(element, out var handle) && handle.Rerender();
    }

    /// <summary>
    /// The WinUI properties Reactor writes on <paramref name="control"/> from the common modifiers
    /// of the element that produced it, so an inspector can mark a property as set in C# (and
    /// overwritten when the element's value changes). Empty when the control is not tagged with
    /// its element — Reactor tags only controls something reads back (callbacks, a key, or a
    /// stamped source location), so turn on
    /// <see cref="Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled"/> for full coverage.
    /// </summary>
    /// <remarks>
    /// Covers <see cref="ElementModifiers"/> and <see cref="AccessibilityModifiers"/>, plus the
    /// <c>AutomationProperties.Name</c> Reactor derives from a captioned control
    /// (<c>Button("Save")</c>, <c>CheckBox("Agree")</c>, …) when the author sets none. That one is
    /// reported as <c>"DefaultAutomationName"</c>, and only while the live name still equals it; a
    /// different live name was written by the app. Properties a control's own element record sets
    /// (<c>TextBlock("hi")</c> → <c>Text</c>) are not listed. Must be called on the UI thread.
    /// </remarks>
    [Microsoft.UI.Reactor.Hosting.UIThreadOnly]
    public static IReadOnlyList<AppliedProperty> GetAppliedProperties(UIElement control)
    {
        ArgumentNullException.ThrowIfNull(control);
        EnsureUIThread(control);
        var tag = Reconciler.GetElementTag(control);
        if (tag is null) return global::System.Array.Empty<AppliedProperty>();
        var chain = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorChain(tag);
        if (Microsoft.UI.Reactor.Core.V1Protocol.OverlayLifecycle.IsAttributionOnlyTag(control, chain[^1]))
            return global::System.Array.Empty<AppliedProperty>();

        var described = AppliedModifierMap.DescribeChain(
            chain, control.GetType(), Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(control));
        var applied = new AppliedProperty[described.Count];
        for (int i = 0; i < applied.Length; i++) applied[i] = ToText(described[i]);
        return applied;
    }

    internal static AppliedProperty ToText(AppliedModifier m)
        => new(m.Modifier, m.Property, DiagnosticText.FormatPlain(m.Modifier, m.Value).Text);

    /// <summary>
    /// Every public member is UI-thread-affine: component tables are unsynchronized and
    /// hook cells are owned by the render thread. Off-thread calls fail loudly instead of
    /// racing. The supplied element's own dispatcher decides (it is set for every live WinUI
    /// element, including in an embedded <c>ReactorHostControl</c> with no <c>ReactorApp.Run</c>,
    /// and is right per window thread); <c>ThreadAffinity</c>'s process-wide rule covers the rest.
    /// </summary>
    internal static void EnsureUIThread(UIElement element)
    {
        if (element.DispatcherQueue is { } queue)
        {
            if (!queue.HasThreadAccess)
                throw new InvalidOperationException(
                    "ReactorDiagnostics must be called on the UI thread that owns the element. " +
                    "Use element.DispatcherQueue.TryEnqueue(...) to marshal the call.");
            return;
        }
        Microsoft.UI.Reactor.Hosting.ThreadAffinity.ThrowIfNotOnUIThread("ReactorDiagnostics component inspection");
    }
}

/// <summary>
/// Internal resolution of a component for <see cref="ReactorDiagnostics"/>: the render context
/// plus identity. Never exposed — the public surface only returns text snapshots of it.
/// </summary>
internal sealed class ComponentHandle
{
    private readonly RenderContext _context;
    private readonly Func<bool> _isAlive;

    internal ComponentHandle(string kind, bool isRoot, string name, RenderContext context,
        object? props, Type? propsType, Func<bool> isAlive)
    {
        Kind = kind;
        IsRoot = isRoot;
        Name = name;
        _context = context;
        Props = props;
        PropsType = propsType;
        _isAlive = isAlive;
    }

    internal string Kind { get; }
    internal bool IsRoot { get; }
    internal string Name { get; }
    internal object? Props { get; }
    internal Type? PropsType { get; }
    internal bool IsMounted => _isAlive();

    /// <summary>
    /// Resolves the component wrapper or host root at <paramref name="element"/>. Must run on the
    /// UI thread (the node tables are not locked).
    /// </summary>
    internal static bool TryResolve(UIElement element, out ComponentHandle handle)
    {
        if (Reconciler.TryFindComponentNode(element, out var owner, out var node)
            && (node.Component is not null || node.Context is not null))
        {
            handle = FromNode(node, () => owner.IsLiveComponentNode(element, node));
            return true;
        }
        if (Reconciler.TryFindRootComponent(element, out var root))
        {
            handle = FromRoot(root);
            return true;
        }
        handle = null!;
        return false;
    }

    internal static ComponentHandle FromNode(Reconciler.ComponentNode node, Func<bool> isAlive)
    {
        var component = node.Component;
        var context = component?.Context ?? node.Context
            ?? throw new InvalidOperationException("Component node has no render context.");
        var element = node.Element;

        if (component is not null)
        {
            var receiver = component as IPropsReceiver;
            var props = receiver is not null ? receiver.PropsBoxed : (element as ComponentElement)?.Props;
            return new ComponentHandle("class", isRoot: false, DiagnosticText.TypeName(component.GetType()),
                context, props, receiver?.PropsType ?? props?.GetType(), isAlive);
        }

        var memo = element is MemoElement;
        Delegate? render = element switch
        {
            FuncElement f => f.RenderFunc,
            MemoElement m => m.RenderFunc,
            _ => null,
        };
        return new ComponentHandle(memo ? "memo" : "function", isRoot: false,
            DiagnosticText.DelegateName(render, memo ? "Memo" : "RenderEachTime"),
            context, props: null, propsType: null, isAlive);
    }

    internal static ComponentHandle FromRoot(in RootComponentSource root)
    {
        var component = root.Component;
        var context = component?.Context ?? root.FuncContext
            ?? throw new InvalidOperationException("Root source has no render context.");
        if (component is not null)
        {
            var receiver = component as IPropsReceiver;
            return new ComponentHandle("class", isRoot: true, DiagnosticText.TypeName(component.GetType()),
                context, receiver?.PropsBoxed, receiver?.PropsType, root.IsAlive);
        }
        return new ComponentHandle("function", isRoot: true, DiagnosticText.DelegateName(root.RenderFunc, "render"),
            context, props: null, propsType: null, root.IsAlive);
    }

    internal ComponentSnapshot Describe()
    {
        var contexts = DescribeContexts();
        return new ComponentSnapshot(Name, Kind, IsRoot, DescribeProps(), DescribeHooks(contexts), contexts);
    }

    internal IReadOnlyList<DiagnosticValue> DescribeProps()
    {
        var rows = DiagnosticText.PropRows(Props, PropsType);
        if (rows.Count == 0) return global::System.Array.Empty<DiagnosticValue>();
        var values = new DiagnosticValue[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var (name, type, value) = rows[i];
            var (text, redacted) = DiagnosticText.Format(name, type, value);
            values[i] = new DiagnosticValue(i, name, "prop", DiagnosticText.FriendlyTypeName(type), text,
                Editable: false, redacted, MigratedByHotReload: false);
        }
        return values;
    }

    internal IReadOnlyList<DiagnosticValue> DescribeHooks(IReadOnlyList<DiagnosticValue>? contexts = null)
    {
        int count = _context.DiagnosticHookCount;
        if (count == 0) return global::System.Array.Empty<DiagnosticValue>();
        var values = new DiagnosticValue[count];
        for (int i = 0; i < count; i++)
        {
            var cell = _context.DiagnosticHookAt(i);
            var valueType = cell.DiagnosticValueType;
            string text = "";
            bool redacted = false;
            if (cell.DiagnosticKind is not (HookKind.Effect or HookKind.NavigationLifecycle or HookKind.Unknown))
            {
                // A context hook's secret-ness can come from the context's name.
                var redactionName = cell.DiagnosticKind == HookKind.Context
                    ? contexts?.FirstOrDefault(c => c.Index == i)?.Name ?? ""
                    : "";
                (text, redacted) = DiagnosticText.Format(redactionName, valueType, cell.DiagnosticValue);
            }
            values[i] = new DiagnosticValue(i, "", HookKindText(cell.DiagnosticKind),
                valueType is null ? "" : DiagnosticText.FriendlyTypeName(valueType), text,
                // The same type-level gate TrySetState applies, so a null secret-typed value is not
                // advertised as editable either.
                Editable: cell.DiagnosticCanSet && !redacted && DiagnosticText.IsEditable(valueType) && !DiagnosticText.IsSecretType(valueType),
                redacted, cell.Migrated);
        }
        return values;
    }

    internal IReadOnlyList<DiagnosticValue> DescribeContexts()
    {
        List<DiagnosticValue>? result = null;
        int count = _context.DiagnosticHookCount;
        for (int i = 0; i < count; i++)
        {
            if (_context.DiagnosticHookAt(i) is not RenderContext.ContextHookState { Context: { } ctx } hook)
                continue;
            var name = ctx.DiagnosticName is { Length: > 0 } declared
                ? declared
                : $"Context<{DiagnosticText.FriendlyTypeName(ctx.ValueType)}>";
            var value = hook.LastValue;
            var fallback = ctx.DefaultValueBoxed;
            bool isDefault;
            // App code: a value's Equals override can throw; one bad value must not fail the description.
            try { isDefault = ReferenceEquals(value, fallback) || (value is not null && value.Equals(fallback)); }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException)) { isDefault = false; }
            var (text, redacted) = DiagnosticText.Format(name, ctx.ValueType, value);
            (result ??= new()).Add(new DiagnosticValue(i, name, isDefault ? "default" : "provided",
                DiagnosticText.FriendlyTypeName(ctx.ValueType), text, Editable: false, redacted, MigratedByHotReload: false));
        }
        return result is null ? global::System.Array.Empty<DiagnosticValue>() : result;
    }

    internal bool TrySetState(int hookIndex, string text, out string? error)
    {
        EnsureRenderThread();
        if (!IsMounted)
        {
            error = $"<{Name}> is no longer mounted";
            return false;
        }
        if ((uint)hookIndex >= (uint)_context.DiagnosticHookCount)
        {
            error = $"<{Name}> has no hook {hookIndex}";
            return false;
        }
        var cell = _context.DiagnosticHookAt(hookIndex);
        if (!cell.DiagnosticCanSet)
        {
            error = $"hook {hookIndex} of <{Name}> has kind '{HookKindText(cell.DiagnosticKind)}'; only state, reducer and persisted hooks can be set";
            return false;
        }
        var valueType = cell.DiagnosticValueType;
        if (valueType is null)
        {
            error = $"hook {hookIndex} of <{Name}> has no value type";
            return false;
        }
        if (DiagnosticText.IsSecretType(valueType) || DiagnosticText.Format("", valueType, cell.DiagnosticValue).Redacted)
        {
            error = $"hook {hookIndex} of <{Name}> holds a secret; it is not written from diagnostics";
            return false;
        }
        if (!DiagnosticText.TryParse(text, valueType, out var parsed, out var parseError))
        {
            error = parseError;
            return false;
        }
        // The same rule that redacts a read refuses a write, so a value the next snapshot would
        // hide (a string such as "AccessToken=…") is never written from diagnostics either.
        if (DiagnosticText.Format("", valueType, parsed).Redacted)
        {
            error = $"the value for hook {hookIndex} of <{Name}> looks like a secret; it is not written from diagnostics";
            return false;
        }
        if (!_context.TrySetHookValueForDiagnostics(hookIndex, parsed))
        {
            error = $"hook {hookIndex} of <{Name}> refused the value";
            return false;
        }
        error = null;
        return true;
    }

    internal bool Rerender()
    {
        EnsureRenderThread();
        return IsMounted && _context.RequestRerenderForDiagnostics();
    }

    private void EnsureRenderThread()
    {
        if (!_context.IsOnRenderThread)
            throw new InvalidOperationException(
                "ReactorDiagnostics writes must run on the UI thread that renders the component.");
    }

    internal static string HookKindText(HookKind kind) => kind switch
    {
        HookKind.State => "state",
        HookKind.Reducer => "reducer",
        HookKind.Ref => "ref",
        HookKind.Memo => "memo",
        HookKind.Effect => "effect",
        HookKind.Context => "context",
        HookKind.Persisted => "persisted",
        HookKind.NavigationLifecycle => "navigationLifecycle",
        _ => "unknown",
    };
}

/// <summary>Which hook produced a hook cell. Internal: the public surface reports it as text.</summary>
internal enum HookKind
{
    Unknown,
    State,
    Reducer,
    Ref,
    Memo,
    Effect,
    Context,
    Persisted,
    NavigationLifecycle,
}
