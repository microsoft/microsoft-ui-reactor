using global::Windows.ApplicationModel.DataTransfer;
using global::Windows.Foundation;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

public sealed partial class Reconciler
{
    /// <summary>
    /// Per-element drag-and-drop dispatch state (spec 027 Tier 6 / Phase 6a).
    /// Mirrors <see cref="EventHandlerState"/>'s trampoline pattern so per-render
    /// updates of <see cref="ElementModifiers.DragSource"/> / <see cref="ElementModifiers.DropTarget"/>
    /// touch only a mutable field — the underlying WinUI events stay subscribed
    /// for the element's lifetime.
    /// </summary>
    internal sealed class DragDropState
    {
        public DragSourceConfig? Source;
        public DropTargetConfig? Target;

        // Current in-flight drag (source side).
        public Guid ActiveTransferId;

        // Stable trampolines — attached once.
        public TypedEventHandler<UIElement, DragStartingEventArgs>? DragStartingTrampoline;
        public TypedEventHandler<UIElement, DropCompletedEventArgs>? DropCompletedTrampoline;
        public DragEventHandler? DragEnterTrampoline;
        public DragEventHandler? DragOverTrampoline;
        public DragEventHandler? DragLeaveTrampoline;
        public DragEventHandler? DropTrampoline;
    }

    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, DragDropState> _dndStates = new();

    private static DragDropState GetOrCreateDndState(FrameworkElement fe)
    {
        if (!_dndStates.TryGetValue(fe, out var state))
        {
            state = new DragDropState();
            _dndStates.AddOrUpdate(fe, state);
        }
        return state;
    }

    private static void ApplyDragDropHandlers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        if (m.DragSource is null && m.DropTarget is null
            && oldM?.DragSource is null && oldM?.DropTarget is null)
            return;

        var state = GetOrCreateDndState(fe);
        state.Source = m.DragSource;
        state.Target = m.DropTarget;

        // ── Source side ───────────────────────────────────────────────
        if (m.DragSource is not null)
        {
            fe.CanDrag = true;

            if (state.DragStartingTrampoline is null)
            {
                state.DragStartingTrampoline = (s, e) => OnDragStarting(state, s, e);
                fe.DragStarting += state.DragStartingTrampoline;
            }
            if (state.DropCompletedTrampoline is null)
            {
                state.DropCompletedTrampoline = (s, e) => OnDropCompleted(state, s, e);
                fe.DropCompleted += state.DropCompletedTrampoline;
            }
        }
        else if (oldM?.DragSource is not null)
        {
            fe.CanDrag = false;
        }

        // ── Target side ───────────────────────────────────────────────
        if (m.DropTarget is not null)
        {
            fe.AllowDrop = true;

            if (state.DragEnterTrampoline is null)
            {
                state.DragEnterTrampoline = (s, e) => OnDragEnter(fe, state, e);
                fe.DragEnter += state.DragEnterTrampoline;
            }
            if (state.DragOverTrampoline is null)
            {
                state.DragOverTrampoline = (s, e) => OnDragOver(fe, state, e);
                fe.DragOver += state.DragOverTrampoline;
            }
            if (state.DragLeaveTrampoline is null)
            {
                state.DragLeaveTrampoline = (s, e) => OnDragLeave(fe, state, e);
                fe.DragLeave += state.DragLeaveTrampoline;
            }
            if (state.DropTrampoline is null)
            {
                state.DropTrampoline = (s, e) => OnDrop(fe, state, e);
                fe.Drop += state.DropTrampoline;
            }
        }
        else if (oldM?.DropTarget is not null)
        {
            fe.AllowDrop = false;
        }
    }

    // ── Source-side handlers ─────────────────────────────────────────

    private static void OnDragStarting(DragDropState state, UIElement sender, DragStartingEventArgs e)
    {
        if (state.Source is not { } src) return;

        // Gate: DraggableWhen.
        if (src.CanDrag is { } guard && !guard())
        {
            e.Cancel = true;
            return;
        }

        var data = src.GetData();
        if (data is null)
        {
            e.Cancel = true;
            return;
        }

        // Register transfer so same-process target can recover the typed payload.
        var transferId = DragData.Register(data);
        state.ActiveTransferId = transferId;

        // Map allowed operations.
        var allowed = src.AllowedOperations ?? DragOperations.All;
        e.AllowedOperations = ToWinUI(allowed);

        // Mark the DataPackage so same-process targets can look up DragData.
        e.Data.Properties[DragData.TransferIdFormatId] = transferId.ToString("N");
        e.Data.Properties[DragData.ProcIdFormatId] =
            data.OriginProcessId.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

        // Publish format sentinels so HasFormat()/AvailableFormats on a view could be
        // wired in Phase 6b. Typed payloads stay out-of-band in the transfer registry.
        foreach (var fmt in data.AvailableFormats)
        {
            if (fmt == DragData.ProcIdFormatId) continue;
            if (!e.Data.Properties.ContainsKey(fmt))
                e.Data.Properties[fmt] = fmt;
        }
    }

    private static void OnDropCompleted(DragDropState state, UIElement sender, DropCompletedEventArgs e)
    {
        if (state.Source is not { } src) return;
        var transferId = state.ActiveTransferId;
        state.ActiveTransferId = Guid.Empty;

        var completed = FromWinUI(e.DropResult);
        var cancelled = e.DropResult == DataPackageOperation.None;

        try
        {
            src.OnEnd?.Invoke(new DragEndContext(completed, cancelled));
        }
        finally
        {
            if (transferId != Guid.Empty)
                DragData.Unregister(transferId);
        }
    }

    // ── Target-side handlers ─────────────────────────────────────────

    private static DragData? ResolveDragData(DragEventArgs e)
    {
        // Prefer the in-memory transfer registry (same-process path).
        if (e.DataView.Properties.TryGetValue(DragData.TransferIdFormatId, out var idObj)
            && idObj is string idStr
            && Guid.TryParseExact(idStr, "N", out var id))
        {
            return DragData.Resolve(id);
        }
        return null;
    }

    private static void InvokeTargetCallback(
        FrameworkElement fe,
        DropTargetConfig cfg,
        DragEventArgs e,
        Action<DragTargetArgs>? callback)
    {
        if (callback is null) return;

        var data = ResolveDragData(e) ?? new DragData();
        var uiOverride = new DragUIOverrideHandle();
        var pos = e.GetPosition(fe);
        var args = new DragTargetArgs(
            data: data,
            position: pos,
            allowedOperations: FromWinUI(e.AllowedOperations),
            modifiers: e.Modifiers,
            uiOverride: uiOverride);

        callback(args);

        // Propagate accepted operation.
        if (args.AcceptedOperation != DragOperations.None)
            e.AcceptedOperation = ToWinUI(args.AcceptedOperation);
        else
            e.AcceptedOperation = DataPackageOperation.None;

        // Propagate UI override (caption, visibility flags).
        if (uiOverride.Caption is not null)
            e.DragUIOverride.Caption = uiOverride.Caption;
        e.DragUIOverride.IsCaptionVisible = uiOverride.IsCaptionVisible;
        e.DragUIOverride.IsContentVisible = uiOverride.IsContentVisible;
        e.DragUIOverride.IsGlyphVisible = uiOverride.IsGlyphVisible;
    }

    private static void OnDragEnter(FrameworkElement fe, DragDropState state, DragEventArgs e)
    {
        if (state.Target is not { } cfg) return;
        // If no explicit callback, still mark drop acceptance based on cfg.AcceptedOperations.
        if (cfg.OnDragEnter is not null)
        {
            InvokeTargetCallback(fe, cfg, e, cfg.OnDragEnter);
        }
        else
        {
            // Default: accept the configured operations, negotiated with source-allowed.
            var negotiated = DragOperationNegotiation.Negotiate(
                FromWinUI(e.AllowedOperations),
                cfg.AcceptedOperations);
            e.AcceptedOperation = ToWinUI(negotiated);
        }
    }

    private static void OnDragOver(FrameworkElement fe, DragDropState state, DragEventArgs e)
    {
        if (state.Target is not { } cfg) return;
        if (cfg.OnDragOver is not null)
        {
            InvokeTargetCallback(fe, cfg, e, cfg.OnDragOver);
        }
        else
        {
            var negotiated = DragOperationNegotiation.Negotiate(
                FromWinUI(e.AllowedOperations),
                cfg.AcceptedOperations);
            e.AcceptedOperation = ToWinUI(negotiated);
        }
    }

    private static void OnDragLeave(FrameworkElement fe, DragDropState state, DragEventArgs e)
    {
        if (state.Target is not { } cfg) return;
        if (cfg.OnDragLeave is not null)
            InvokeTargetCallback(fe, cfg, e, cfg.OnDragLeave);
    }

    private static void OnDrop(FrameworkElement fe, DragDropState state, DragEventArgs e)
    {
        if (state.Target is not { } cfg) return;
        // TypedDrop runs first if set — it does payload unwrapping internally.
        var callback = cfg.TypedDrop ?? cfg.OnDrop;
        if (callback is not null)
        {
            InvokeTargetCallback(fe, cfg, e, callback);
        }
    }

    // ── Enum mapping ─────────────────────────────────────────────────

    internal static DataPackageOperation ToWinUI(DragOperations ops)
    {
        DataPackageOperation result = DataPackageOperation.None;
        if ((ops & DragOperations.Copy) != 0) result |= DataPackageOperation.Copy;
        if ((ops & DragOperations.Move) != 0) result |= DataPackageOperation.Move;
        if ((ops & DragOperations.Link) != 0) result |= DataPackageOperation.Link;
        return result;
    }

    internal static DragOperations FromWinUI(DataPackageOperation ops)
    {
        DragOperations result = DragOperations.None;
        if ((ops & DataPackageOperation.Copy) != 0) result |= DragOperations.Copy;
        if ((ops & DataPackageOperation.Move) != 0) result |= DragOperations.Move;
        if ((ops & DataPackageOperation.Link) != 0) result |= DragOperations.Link;
        return result;
    }
}
