using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Input;

/// <summary>
/// Opaque ref to a mounted <see cref="FrameworkElement"/>. Populated by the reconciler
/// when the element is mounted; consumed by <see cref="FocusManager.Focus"/> and friends.
/// Null until the target element is mounted. Survives re-renders — the same
/// <see cref="ElementRef"/> instance is reused by the reconciler while the underlying
/// control is recycled from the element pool.
/// </summary>
public sealed class ElementRef
{
    private const int MaxCurrentChangedDispatchDepth = 32;

    [ThreadStatic]
    private static int s_dispatchDepth;

#if !DEBUG
    private static int s_loggedReentrancy;
#endif
    private bool _dispatching;

    internal FrameworkElement? _current;

    /// <summary>
    /// When this ref is the inner of an <see cref="ElementRef{T}"/>, the typed wrapper
    /// records the expected concrete type here so the reconciler can fail loudly under
    /// DEBUG when the developer attaches a ref of one type to an element of another.
    /// Internal so it does not pollute IntelliSense on the public type.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Type? ExpectedType { get; set; }

    /// <summary>
    /// The currently-mounted control, or null if the referenced element has not mounted yet
    /// (or has been unmounted without a replacement).
    /// </summary>
    public FrameworkElement? Current => _current;

    /// <summary>
    /// Raised after <see cref="Current"/> changes. Visibility is intentionally
    /// internal in Phase 1 of spec 057 (Q4) and may be promoted to public once a
    /// concrete imperative consumer needs it.
    /// </summary>
    internal event Action<FrameworkElement?>? CurrentChanged;

    /// <summary>
    /// Number of current subscribers to <see cref="CurrentChanged"/>. Internal
    /// diagnostic/test hook for spec 057 topology leak-count assertions.
    /// </summary>
    internal int CurrentChangedSubscriberCount => CurrentChanged?.GetInvocationList().Length ?? 0;

    /// <summary>
    /// Updates the mounted control and raises <see cref="CurrentChanged"/> only
    /// when the reference actually changes.
    /// </summary>
    internal void SetCurrent(FrameworkElement? value)
    {
        if (ReferenceEquals(_current, value)) return;

        _current = value;
        if (Microsoft.UI.Reactor.Core.V1Protocol.ReferenceDirtySet.TryEnqueue(this)) return;

        RaiseCurrentChanged(value);
    }

    internal void FlushDispatch() => RaiseCurrentChanged(_current);

    private void RaiseCurrentChanged(FrameworkElement? value)
    {
        if (_dispatching || s_dispatchDepth >= MaxCurrentChangedDispatchDepth)
        {
            ReportReentrantDispatchSuppressed(value);
            return;
        }

        var handler = CurrentChanged;
        if (handler is null) return;

        _dispatching = true;
        s_dispatchDepth++;
        try
        {
            handler(value);
        }
        finally
        {
            s_dispatchDepth--;
            _dispatching = false;
        }
    }

    private void ReportReentrantDispatchSuppressed(FrameworkElement? value)
    {
        var expected = ExpectedType?.Name ?? nameof(FrameworkElement);
        var actual = value?.GetType().Name ?? "null";
        var message =
            "ElementRef reference-edge no-echo violation: suppressed re-entrant " +
            $"CurrentChanged dispatch for ElementRef<{expected}> (value={actual}, " +
            $"depth={s_dispatchDepth}, cap={MaxCurrentChangedDispatchDepth}). " +
            "CurrentChanged subscribers must not call SetCurrent on the same cell " +
            "or create synchronous reference-edge cycles.";

#if DEBUG
        Debug.Fail(message);
#else
        if (global::System.Threading.Interlocked.Exchange(ref s_loggedReentrancy, 1) == 0)
        {
            Debug.WriteLine(message);
        }
#endif
    }
}

/// <summary>
/// Strongly-typed wrapper around <see cref="ElementRef"/> that surfaces the mounted
/// element as <typeparamref name="T"/> instead of the base <see cref="FrameworkElement"/>,
/// removing the <c>(Button)ref.Current</c> cast at every consumer.
/// </summary>
/// <typeparam name="T">
/// The concrete WinUI control type the ref will be attached to. Constrained to
/// <see cref="FrameworkElement"/> so the safe-cast in <see cref="Current"/> is
/// well-defined.
/// </typeparam>
/// <remarks>
/// <para>
/// Spec 033 §3. Construct via <c>ctx.UseElementRef&lt;T&gt;()</c> and bind to an
/// element via <c>.Ref(typedRef)</c>. The typed wrapper is reflection-free and
/// AOT-safe — <typeparamref name="T"/> is used only for the cast on read.
/// </para>
/// <para>
/// <b>Threading:</b> the reconciler populates the underlying ref on the UI thread
/// during element mount/update. Reading <see cref="Current"/> from a non-UI thread
/// is permitted but the value may be stale by the time the caller dereferences it.
/// </para>
/// <para>
/// <b>Accessibility:</b> programmatic focus via
/// <c>Current?.Focus(<see cref="FocusState.Programmatic"/>)</c> moves the
/// keyboard focus and triggers WinUI's standard focus-changed UIA event, but
/// it does <i>not</i> by itself produce a screen-reader announcement of
/// surrounding content. When the focus change should be accompanied by an
/// announcement (e.g. "saved", "validation error") use
/// <see cref="Microsoft.UI.Reactor.Hooks.UseAnnounceExtensions.UseAnnounce(RenderContext)"/>
/// in addition to the focus call. Modifiers applied to the element (notably
/// <c>.AutomationName(...)</c>) are preserved across re-renders, so the typed
/// ref does not affect the UIA tree shape.
/// </para>
/// </remarks>
[DebuggerDisplay("ElementRef<{typeof(T).Name,nq}> Current={Current}")]
public sealed class ElementRef<T> where T : FrameworkElement
{
    private readonly ElementRef _inner;
    private event Action<T?>? _typedCurrentChanged;
    private bool _typedCurrentChangedForwarderAttached;

    internal ElementRef(ElementRef inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        // Record the expected concrete type so the reconciler can assert under DEBUG
        // when a typed ref is attached to an element of the wrong type.
        _inner.ExpectedType = typeof(T);
    }

    /// <summary>
    /// The mounted control as <typeparamref name="T"/>, or <c>null</c> when the
    /// referenced element has not mounted yet, has been unmounted, or the
    /// concrete control is not assignable to <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// In RELEASE builds a type mismatch silently yields <c>null</c>; in DEBUG
    /// builds the reconciler raises <see cref="Debug.Fail(string)"/> at attach time
    /// so the mismatch is loud during development.
    /// </remarks>
    public T? Current => _inner.Current as T;

    /// <summary>
    /// Raised after the inner untyped cell changes, projecting the value as
    /// <typeparamref name="T"/>. Visibility is intentionally internal in Phase 1
    /// of spec 057 (Q4); the inner <see cref="ElementRef"/> remains the actual
    /// subscription target and stable identity.
    /// </summary>
    internal event Action<T?>? CurrentChanged
    {
        add
        {
            if (value is null) return;

            _typedCurrentChanged += value;
            if (!_typedCurrentChangedForwarderAttached)
            {
                _inner.CurrentChanged += OnInnerCurrentChanged;
                _typedCurrentChangedForwarderAttached = true;
            }
        }
        remove
        {
            if (value is null) return;

            _typedCurrentChanged -= value;
            if (_typedCurrentChanged is null && _typedCurrentChangedForwarderAttached)
            {
                _inner.CurrentChanged -= OnInnerCurrentChanged;
                _typedCurrentChangedForwarderAttached = false;
            }
        }
    }

    /// <summary>
    /// The underlying untyped ref. Internal — callers should rely on the implicit
    /// conversion below or on the typed <c>.Ref(...)</c> overload, which keep the
    /// wrapping invisible.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal ElementRef Inner => _inner;

    /// <summary>
    /// Implicit conversion to the untyped <see cref="ElementRef"/> so the typed
    /// wrapper composes with the existing <c>.Ref(...)</c> modifier and
    /// <see cref="FocusManager"/> overloads without overload bloat.
    /// </summary>
    public static implicit operator ElementRef(ElementRef<T> typed) =>
        typed?._inner ?? throw new ArgumentNullException(nameof(typed));

    private void OnInnerCurrentChanged(FrameworkElement? value) =>
        _typedCurrentChanged?.Invoke(value as T);

    /// <inheritdoc />
    public override string ToString() => $"ElementRef<{typeof(T).Name}>";
}

/// <summary>
/// Factory helpers for <see cref="ElementRef{T}"/>. Prefer the
/// <c>ctx.UseElementRef&lt;T&gt;()</c> hook from a render path; this helper is for
/// the rare case of constructing a typed ref outside a render context (e.g. for
/// tests).
/// </summary>
public static class TypedElementRef
{
    /// <summary>Creates a fresh, unbound typed ref.</summary>
    public static ElementRef<T> Create<T>() where T : FrameworkElement =>
        new(new ElementRef());
}

/// <summary>
/// Imperative focus helpers (spec 027 Tier 5). These operate on <see cref="ElementRef"/>
/// refs obtained via the <c>UseElementFocus</c> hook or an explicit <c>.Ref(...)</c>
/// modifier; use them when declarative focus (<c>.Focus(...)</c> form helpers,
/// <see cref="Microsoft.UI.Xaml.UIElement.IsTabStop"/>) is not enough.
/// </summary>
public static class FocusManager
{
    /// <summary>
    /// Synchronously focus the referenced element. Returns <c>false</c> when the ref
    /// has no mounted target, the target cannot receive focus, or WinUI rejects the
    /// focus request.
    /// </summary>
    public static bool Focus(ElementRef target, FocusState state = FocusState.Programmatic)
    {
        if (target?._current is not { } fe) return false;
        if (fe is Control ctrl) return ctrl.Focus(state);
        return fe.Focus(state);
    }

    /// <summary>
    /// Asynchronously focus the referenced element using WinUI's FocusManager.TryFocusAsync.
    /// Prefer this when focus is requested from a non-UI thread or when the caller needs
    /// confirmation that the focus change succeeded.
    /// </summary>
    public static async global::System.Threading.Tasks.Task<bool> FocusAsync(ElementRef target, FocusState state = FocusState.Programmatic)
    {
        if (target?._current is not { } fe) return false;
        var result = await Microsoft.UI.Xaml.Input.FocusManager.TryFocusAsync(fe, state);
        return result.Succeeded;
    }
}
