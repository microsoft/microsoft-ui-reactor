using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: Context scopes, provider/theme change tracking, and context replay for virtualized realization.
public sealed partial class Reconciler
{
    // ════════════════════════════════════════════════════════════════════
    //  Disposable wrappers for V1 MountContext (1.6)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.6) — typed context push that returns an
    /// <see cref="IDisposable"/> the handler disposes when its scope ends.
    /// The underlying scope is the same one used by built-in ContextProvider
    /// elements.
    /// </summary>
    internal IDisposable PushContextDisposable<T>(Context<T> context, T value)
    {
        var dict = new Dictionary<ContextBase, object?>(1) { [context] = value };
        _contextScope.Push(dict);
        return new PopOnDispose(_contextScope, 1);
    }

    // Internal so V1-owned lifecycle classes can read ambient Context<T> values
    // without exposing the traversal stack itself.
    internal T ReadContext<T>(Context<T> context) => _contextScope.Read(context);

    internal bool HasActiveContextValues => _contextScope.HasActiveValues || _providerChangeDepth > 0 || _themeChangeDepth > 0;

    // Depth of non-skip Updates, on the current traversal path, whose element's own
    // ContextValues differ from the previous render's (a provider added, removed or
    // changed). Removing the LAST provider leaves the live stack empty, which used to
    // short-circuit every context skip check (HasActiveContextValues), so a
    // reference-stable consumer below kept the removed value. Host snapshots use the
    // longer-lived ContextScope.ProviderGeneration instead (bumped alongside).
    private int _providerChangeDepth;

    internal void EnterProviderChange()
    {
        _providerChangeDepth++;
        _contextScope.BumpProviderGeneration();
    }

    internal void ExitProviderChange() => _providerChangeDepth--;

    // Same idea for RequestedTheme: depth of non-skip Updates on the current path whose
    // own RequestedTheme changed, plus a generation bumped alongside. A virtualized host
    // records the generation when it is reached; a host skipped below a theme change
    // would keep the previous pass's subtree theme, so the skip check declines it once
    // and its Update re-records the pending theme for rows realized later in the pass.
    private int _themeChangeDepth;
    private long _themeGeneration;

    internal void EnterThemeChange()
    {
        _themeChangeDepth++;
        _themeGeneration++;
    }

    internal void ExitThemeChange() => _themeChangeDepth--;

    // ── Context replay for virtualized realization ─────────────────────
    //
    // ItemsRepeater (IElementFactory.GetElement), ListViewBase
    // (ContainerContentChanging) and the typed TreeView build row content from
    // WinUI layout callbacks. Those run after the traversal that maintains
    // _contextScope has unwound (first realization, scrolling) or re-entrantly in
    // the middle of an unrelated subtree's update (a sibling's synchronous
    // UpdateLayout), so the live stack is either empty or someone else's. Each
    // such host captures the stack whenever it is reconciled, and the realization
    // callbacks replay that capture around the row mount (see EnterRealization).

    // Gates the per-control capture lookups so apps without a virtualized host
    // never pay the attached-state read.
    private bool _hasContextCaptures;

    // The innermost element currently in Mount/Update dispatch. A capture registered
    // by a host handler records this element's type as its owner: a decorator
    // (Flyout, MenuFlyout, …) or a transparent wrapper shares the host's control but
    // sits at a shallower depth, so only the owner's own Update/skip may re-snapshot.
    private Element? _reconcilingElement;
    // The outermost current layer updating this same native control. Custom
    // decorators share modifier priority just like the built-in flyout decorators.
    internal (UIElement? Control, Element? Root, ElementModifiers? Modifiers) ModifierUpdate;
    private bool _hasModifierTargets;
    private Element? _modifierMountParent;
    private List<(UIElement Control, Element Target)>? _modifierMounts;
    // Until the target update returns, its mounted decorator still describes the previous render.
    internal (FrameworkElement? Target, Element? Decorator) UpdatingFlyout;

    private static bool MayHostContextCapture(UIElement control)
        => control is WinUI.ScrollViewer or WinUI.ItemsRepeater or WinUI.ItemsView
            or WinUI.ListViewBase or WinUI.TreeView;

    /// <summary>
    /// Registers <paramref name="host"/> as a virtualized host and snapshots the live
    /// provider stack onto it. Call from the host's Mount, while the traversal is at
    /// the host; the generic Update path refreshes the snapshot from then on.
    /// </summary>
    internal ContextCapture CaptureContextForHost(FrameworkElement host)
    {
        global::System.Diagnostics.Debug.Assert(MayHostContextCapture(host),
            $"{host.GetType().Name} is not covered by MayHostContextCapture; its capture would never refresh.");
        var state = GetOrCreateReactorState(host);
        var capture = state.ContextCapture ??= new ContextCapture { OwnerType = _reconcilingElement?.GetType() };
        capture.Refresh(_contextScope);
        capture.NotePass(_passId, _ambientRequestedTheme, _themeGeneration);
        _hasContextCaptures = true;
        return capture;
    }

    private ContextCapture? TryGetContextCapture(UIElement control)
        => _hasContextCaptures
            && MayHostContextCapture(control)
            && ((FrameworkElement)control).GetValue(ReactorAttached.StateProperty) is ReactorState { ContextCapture: { } capture }
                ? capture
                : null;

    /// <summary>
    /// Re-snapshots a host's provider stack when <paramref name="newEl"/> is the element that
    /// owns the capture; a no-op for any other control or element.
    /// </summary>
    internal void RefreshContextCapture(UIElement control, Element newEl, ElementTheme effectiveTheme)
    {
        if (TryGetContextCapture(control) is { } capture && capture.IsOwnedBy(newEl))
        {
            capture.Refresh(_contextScope);
            capture.NotePass(_passId, effectiveTheme, _themeGeneration);
        }
    }

    /// <summary>
    /// Skip-arm counterpart of <see cref="RefreshContextCapture"/>. The skip arm runs
    /// before the element's own <c>ContextValues</c> are pushed, so they are pushed here
    /// for the snapshot. A decorator sharing the host's control is not the owner, so a
    /// skipped decorator leaves the host's snapshot (taken at the host's own depth)
    /// untouched; if a provider above it changed, the stale check declines that skip and
    /// the decorator's Update reaches the host element.
    /// </summary>
    internal void RefreshContextCaptureOnSkip(UIElement control, Element newEl, ElementTheme effectiveTheme)
    {
        if (TryGetContextCapture(control) is not { } capture || !capture.IsOwnedBy(newEl)) return;
        capture.NotePass(_passId, effectiveTheme, _themeGeneration);
        var own = newEl.ContextValues;
        if (own is not { Count: > 0 })
        {
            capture.Refresh(_contextScope);
            return;
        }
        _contextScope.Push(own);
        try { capture.Refresh(_contextScope); }
        finally { _contextScope.Pop(own.Count); }
    }

    /// <summary>
    /// A standalone snapshot of the live provider stack, for an <see cref="ElementFactory{T}"/>
    /// whose host never registered one (an out-of-tree handler installing the factory).
    /// </summary>
    internal ContextCapture CaptureLiveContext()
    {
        var capture = new ContextCapture();
        capture.Refresh(_contextScope);
        capture.NotePass(_passId, _ambientRequestedTheme, _themeGeneration);
        return capture;
    }

    internal void RefreshLiveContext(ContextCapture capture)
    {
        capture.Refresh(_contextScope);
        capture.NotePass(_passId, _ambientRequestedTheme, _themeGeneration);
    }

    /// <summary>
    /// Opens a realization frame for building a row outside (or re-entrantly inside) the
    /// traversal. Until the returned scope is disposed:
    /// <list type="bullet">
    ///   <item><description>the live provider stack is replaced with <paramref name="capture"/>,
    ///     and the ambient theme is read live off <paramref name="host"/> (see
    ///     <see cref="LiveAmbientTheme"/>);</description></item>
    ///   <item><description>the in-flight pass's per-subtree bookkeeping is set aside, so the
    ///     row is not recorded in an ErrorBoundary's rollback lists (a failing boundary child
    ///     would otherwise tear down a live row in an unrelated list), a row that throws gets
    ///     its own in-tree fallback instead of escaping into WinUI layout, and stagger,
    ///     provider/theme-change state and the diagnostic owner of the surrounding subtree
    ///     do not leak in.</description></item>
    /// </list>
    /// Window hooks need nothing here: they read <see cref="Owner"/>, which every component
    /// render receives regardless of when it runs.
    /// A null capture still isolates; it just leaves the provider stack as it is.
    /// </summary>
    internal RealizationFrame EnterRealization(ContextCapture? capture, UIElement? host)
        => new(this, capture, RealizationTheme(capture, host));

    /// <inheritdoc cref="EnterRealization(ContextCapture?, UIElement?)"/>
    internal RealizationFrame EnterRealization(UIElement host) => EnterRealization(TryGetContextCapture(host), host);

    // Inside the pass that last reached the host, the theme that pass is applying to the
    // host's subtree (its native RequestedTheme modifier lands only after children are
    // dispatched); otherwise the host's live inherited RequestedTheme.
    private ElementTheme RealizationTheme(ContextCapture? capture, UIElement? host)
        => capture is not null && _entryDepth > 0 && capture.PassId == _passId
            ? capture.PassTheme
            : LiveAmbientTheme(host);

    /// <summary>
    /// The theme a row realized into <paramref name="host"/> should resolve ThemeRef
    /// resource overrides against. Read from the host's visual tree rather than captured at
    /// reconcile time, so an ancestor <c>RequestedTheme</c> that changed while the host was
    /// skipped is still seen. Uses the same rule as <see cref="EnterRealizationTheme"/> (the
    /// nearest explicit <c>RequestedTheme</c>, or Default), so a row's recorded realization
    /// theme compares equal whichever path built it.
    /// </summary>
    private static ElementTheme LiveAmbientTheme(UIElement? host) => InheritedRequestedTheme(host);

    /// <summary>
    /// Registers <paramref name="host"/> (see <see cref="CaptureContextForHost"/>) and
    /// hands its capture to <paramref name="factory"/> so <c>GetElement</c> replays it.
    /// </summary>
    internal void AttachContextCapture(IElementFactory factory, FrameworkElement host)
    {
        if (factory is IContextCapturingFactory capturing)
            capturing.ContextCapture = CaptureContextForHost(host);
    }

    internal readonly struct RealizationFrame : IDisposable
    {
        private readonly Reconciler _owner;
        private readonly List<(ContextBase Context, object? Value)>? _previousStack;
        private readonly ElementTheme _previousTheme;
        private readonly List<UIElement>? _previousJournal;
        private readonly List<UIElement>? _previousRoots;
        private readonly int _previousBoundaryDepth;
        private readonly int _previousProviderChangeDepth;
        private readonly int _previousThemeChangeDepth;
        private readonly Element? _previousReconcilingElement;
        private readonly StaggerScope? _previousStagger;
        private readonly string? _previousDiagOwner;
        private readonly string? _previousDiagRootOwner;

        internal RealizationFrame(Reconciler owner, ContextCapture? capture, ElementTheme ambientTheme)
        {
            _owner = owner;
            _previousTheme = owner._ambientRequestedTheme;
            _previousJournal = owner._boundaryMountJournal;
            _previousRoots = owner._boundaryMountRoots;
            _previousBoundaryDepth = owner._errorBoundaryDepth;
            _previousProviderChangeDepth = owner._providerChangeDepth;
            _previousThemeChangeDepth = owner._themeChangeDepth;
            _previousReconcilingElement = owner._reconcilingElement;
            _previousStagger = _staggerScope;
            _previousDiagOwner = owner._diagOwner;
            _previousDiagRootOwner = owner.DiagnosticRootOwner;

            owner._boundaryMountJournal = null;
            owner._boundaryMountRoots = null;
            owner._errorBoundaryDepth = 0;
            owner._providerChangeDepth = 0;
            owner._themeChangeDepth = 0;
            owner._reconcilingElement = null;
            owner._ambientRequestedTheme = ambientTheme;
            // A row built re-entrantly is not part of the component being rendered: report
            // its owner as unknown, as for any row realized during layout.
            owner._diagOwner = null;
            owner.DiagnosticRootOwner = null;
            _staggerScope = null;

            _previousStack = capture is not null ? owner._contextScope.Replace(capture.Entries, capture.Generation) : null;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null) return;
            if (_previousStack is not null)
                owner._contextScope.Restore(_previousStack);
            owner._ambientRequestedTheme = _previousTheme;
            owner._boundaryMountJournal = _previousJournal;
            owner._boundaryMountRoots = _previousRoots;
            owner._errorBoundaryDepth = _previousBoundaryDepth;
            owner._providerChangeDepth = _previousProviderChangeDepth;
            owner._themeChangeDepth = _previousThemeChangeDepth;
            owner._reconcilingElement = _previousReconcilingElement;
            owner._diagOwner = _previousDiagOwner;
            owner.DiagnosticRootOwner = _previousDiagRootOwner;
            _staggerScope = _previousStagger;
        }
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.6) — push a stagger scope for child enter
    /// transitions. Returns an <see cref="IDisposable"/> that pops the
    /// scope on dispose.
    /// </summary>
    internal IDisposable PushStaggerScopeDisposable(TimeSpan delay)
    {
        PushStaggerScope(delay);
        return new PopStaggerOnDispose();
    }

    private sealed class PopOnDispose : IDisposable
    {
        private ContextScope? _scope;
        private readonly int _count;
        public PopOnDispose(ContextScope scope, int count) { _scope = scope; _count = count; }
        public void Dispose()
        {
            var s = _scope;
            if (s is null) return;
            _scope = null;
            s.Pop(_count);
        }
    }

    private sealed class PopStaggerOnDispose : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PopStaggerScope();
        }
    }
}
