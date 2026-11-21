using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Microsoft.UI.Reactor.Animation;

/// <summary>The features that share a visual's implicit animation keys. A higher value wins a key.</summary>
internal enum ImplicitFeature : byte
{
    /// <summary>A reorder move under an ambient <c>WithAnimation</c> scope (spec 042 §6).</summary>
    Move = 0,
    /// <summary><c>.OpacityTransition()</c>, <c>.ScaleTransition()</c>, <c>.RotationTransition()</c>, <c>.TranslationTransition()</c>.</summary>
    Transition = 1,
    /// <summary><c>.Animate()</c>.</summary>
    Animate = 2,
    /// <summary><c>.LayoutAnimation()</c>.</summary>
    Layout = 3,
}

/// <summary>Where the effective claim for a key is written.</summary>
internal interface IImplicitAnimationSink
{
    void Insert(string key, object value);
    void Remove(string key);
    /// <summary>Drops every key this owner published, without restoring anything.</summary>
    void Clear();
}

/// <summary>
/// Who claims each implicit animation key on one control (spec 066 §6.1). Pure managed state:
/// the collection writes go through an <see cref="IImplicitAnimationSink"/>.
/// </summary>
/// <remarks>
/// A claim is keyed by (key, feature, slot). The slot is the declaring layer's depth in the
/// shared-target chain, 0 being the outermost layer (a keyed <c>Memo</c> or a flyout over its
/// target). Element records are replaced on every reconcile, so they can't identify a claim:
/// re-claiming under the same slot replaces the previous claim in place. Between features the
/// higher <see cref="ImplicitFeature"/> wins, and within one feature the outermost slot wins.
/// Releasing the winner publishes the next claim, or removes the key when none is left.
/// </remarks>
internal sealed class ImplicitAnimationClaims
{
    private readonly record struct Claim(string Key, ImplicitFeature Feature, int Slot, object Value);

    private readonly List<Claim> _claims = new(2);
    private List<string>? _deferred;

    /// <summary>Set by the sink once a claim was written to the handed-out visual.</summary>
    internal bool PublishedToVisual;

    /// <summary>The reconciler Mount frame that last claimed on this control (see Reconciler.Mount).</summary>
    internal int MountFrame;

    /// <summary>The outermost element whose Mount handled this control's claims, held weakly.</summary>
    internal WeakReference<object>? MountOwner;

    internal int Count => _claims.Count;

    internal void Add<TSink>(TSink sink, string key, ImplicitFeature feature, int slot, object value)
        where TSink : IImplicitAnimationSink
    {
        var before = Effective(key);
        int i = IndexOf(key, feature, slot);
        if (i >= 0) _claims[i] = new(key, feature, slot, value);
        else _claims.Add(new(key, feature, slot, value));
        // A re-claimed winner republishes even when it's the same instance: a transition object
        // can be reused with a changed Duration, and its compositor key is built from it.
        if (ReferenceEquals(Effective(key), value)) before = null;
        Publish(sink, key, before);
    }

    internal void Release<TSink>(TSink sink, string key, ImplicitFeature feature, int slot, bool deferRestore = false)
        where TSink : IImplicitAnimationSink
    {
        int i = IndexOf(key, feature, slot);
        if (i < 0) return;
        var before = Effective(key);
        _claims.RemoveAt(i);
        if (deferRestore && Effective(key) is { } next && !ReferenceEquals(before, next))
        {
            // The key leaves now and its next claim returns at FlushDeferred, so writes made in
            // between (the setters and modifiers of every layer sharing the control) don't
            // animate with an animation that just took over. Claims and releases on the key
            // until then wait for the flush too.
            sink.Remove(key);
            (_deferred ??= []).Add(key);
            return;
        }
        Publish(sink, key, before);
    }

    /// <summary>Publishes the claims a deferred release held back.</summary>
    internal void FlushDeferred<TSink>(TSink sink) where TSink : IImplicitAnimationSink
    {
        if (_deferred is not { Count: > 0 } deferred) return;
        foreach (var key in deferred)
            if (Effective(key) is { } value) sink.Insert(key, value);
        deferred.Clear();
    }

    internal void ReleaseAll<TSink>(TSink sink) where TSink : IImplicitAnimationSink
    {
        if (_claims.Count == 0) return;
        _claims.Clear();
        _deferred?.Clear();
        sink.Clear();
    }

    /// <summary>A layer mounted over this control: the existing claims move one slot inward.</summary>
    internal void ShiftInward()
    {
        for (int i = 0; i < _claims.Count; i++)
            _claims[i] = _claims[i] with { Slot = _claims[i].Slot + 1 };
    }

    internal object? Effective(string key)
    {
        Claim? best = null;
        foreach (var claim in _claims)
        {
            if (claim.Key != key) continue;
            if (best is not { } b || claim.Feature > b.Feature || (claim.Feature == b.Feature && claim.Slot < b.Slot))
                best = claim;
        }
        return best?.Value;
    }

    private int IndexOf(string key, ImplicitFeature feature, int slot)
    {
        for (int i = 0; i < _claims.Count; i++)
        {
            var claim = _claims[i];
            if (claim.Key == key && claim.Feature == feature && claim.Slot == slot) return i;
        }
        return -1;
    }

    private void Publish<TSink>(TSink sink, string key, object? before) where TSink : IImplicitAnimationSink
    {
        if (_deferred?.Contains(key) == true) return;
        var after = Effective(key);
        if (ReferenceEquals(before, after)) return;
        if (after is null) sink.Remove(key);
        else sink.Insert(key, after);
    }
}

/// <summary>
/// The only writer of <c>Visual.ImplicitAnimations</c> (spec 066 §6). Layout animation,
/// <c>.Animate()</c>, the implicit transitions and ambient moves claim keys here, so adding or
/// removing one never drops another's key (#1625, #1626).
/// </summary>
/// <remarks>
/// A transition claim holds the XAML transition object. It is written to the matching
/// <c>UIElement.*Transition</c> property while the visual hasn't been handed out, and as a
/// compositor animation on the visual afterwards, when WinUI refuses the XAML property. Spec 066
/// layer 2 moves every transition to the visual. Every other claim holds a
/// <see cref="CompositionAnimation"/>, and its caller has already taken the visual.
/// </remarks>
internal static class ImplicitAnimationSlots
{
    internal static void Claim(UIElement element, string key, ImplicitFeature feature, int slot, object value)
    {
        var state = Reconciler.GetOrCreateReactorState(element);
        var claims = state.ImplicitClaims ??= new();
        claims.Add(new Sink(element, claims), key, feature, slot, value);
    }

    internal static void Release(UIElement element, string key, ImplicitFeature feature, int slot, bool deferRestore = false)
    {
        if (Claims(element) is { } claims) claims.Release(new Sink(element, claims), key, feature, slot, deferRestore);
    }

    /// <summary>Publishes what a <c>deferRestore</c> release held back.</summary>
    internal static void FlushDeferred(UIElement element)
    {
        if (Claims(element) is { } claims) claims.FlushDeferred(new Sink(element, claims));
    }

    /// <summary>Drops every claim and the keys they published on the visual. XAML transitions are left alone.</summary>
    internal static void ReleaseAll(UIElement element)
    {
        if (element.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState { ImplicitClaims: { } claims } state)
            return;
        state.ImplicitClaims = null;
        claims.ReleaseAll(new Sink(element, claims));
    }

    /// <summary>A transparent layer (a keyed <c>Memo</c>, a flyout) was mounted over the control's existing layers.</summary>
    internal static void ShiftInward(UIElement element) => Claims(element)?.ShiftInward();

    /// <summary>True once a claim on this control was published to its handed-out visual.</summary>
    internal static bool PublishedToVisual(UIElement element) => Claims(element)?.PublishedToVisual == true;

    internal static ImplicitAnimationClaims? Claims(UIElement element)
        => (element.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState)?.ImplicitClaims;

    private readonly struct Sink(UIElement element, ImplicitAnimationClaims claims) : IImplicitAnimationSink
    {
        public void Insert(string key, object value)
        {
            if (value is not CompositionAnimation animation)
            {
                if (!claims.PublishedToVisual && TrySetXamlTransition(element, key, value)) return;
                animation = TransitionAnimation(key, value);
            }
            var visual = TakeVisual();
            if (visual.ImplicitAnimations is { } collection)
            {
                collection[key] = animation;
                return;
            }
            collection = visual.Compositor.CreateImplicitAnimationCollection();
            collection[key] = animation;
            visual.ImplicitAnimations = collection;
        }

        public void Remove(string key)
        {
            if (!claims.PublishedToVisual)
            {
                TrySetXamlTransition(element, key, null);
                return;
            }
            var visual = ElementCompositionPreview.GetElementVisual(element);
            if (visual.ImplicitAnimations is not { } collection) return;
            collection.Remove(key);
            if (collection.Count == 0) visual.ImplicitAnimations = null;
        }

        public void Clear()
        {
            if (claims.PublishedToVisual)
                ElementCompositionPreview.GetElementVisual(element).ImplicitAnimations = null;
        }

        private Visual TakeVisual()
        {
            if (claims.PublishedToVisual) return ElementCompositionPreview.GetElementVisual(element);
            var visual = AnimationHelper.GetElementVisual(element);
            claims.PublishedToVisual = true;
            return visual;
        }

        // The fallback keeps the transition's duration with a linear curve, as it did before
        // spec 066. Layer 2 gives it the XAML curve.
        private CompositionAnimation TransitionAnimation(string key, object transition)
        {
            var compositor = TakeVisual().Compositor;
            ElementPool.MarkCompositorTainted(element);
            if (transition is ScalarTransition scalar)
                return AnimationHelper.CreateScalarImplicitAnimation(compositor, key, new LinearCurve(scalar.Duration));
            return AnimationHelper.CreateVector3ImplicitAnimation(compositor, key, new LinearCurve(((Vector3Transition)transition).Duration));
        }
    }

    /// <summary>
    /// Writes (or clears, for null) the XAML transition behind <paramref name="key"/>. Returns false
    /// when WinUI refuses it because the visual was handed out.
    /// </summary>
    private static bool TrySetXamlTransition(UIElement element, string key, object? transition)
    {
        try
        {
            switch (key)
            {
                case "Opacity": element.OpacityTransition = (ScalarTransition?)transition; break;
                case "RotationAngle": element.RotationTransition = (ScalarTransition?)transition; break;
                case "Scale": element.ScaleTransition = (Vector3Transition?)transition; break;
                case "Offset": element.TranslationTransition = (Vector3Transition?)transition; break;
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
