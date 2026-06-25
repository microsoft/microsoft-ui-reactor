using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Non-generic base for type-erased storage in the context scope stack.
/// </summary>
public abstract class ContextBase
{
    internal abstract object? DefaultValueBoxed { get; }

    /// <summary>
    /// Compares this context's current value in <paramref name="scope"/> against a
    /// previously-recorded boxed value, without boxing the current value (perf #25).
    /// Semantically equivalent to <c>object.Equals(scope.Read(this), lastBoxed)</c>.
    /// </summary>
    internal abstract bool CurrentValueEquals(ContextScope scope, object? lastBoxed);
}

/// <summary>
/// A typed, named context that can be provided to a subtree and consumed by any descendant.
/// Define as a static field. Provide via .Provide() modifier. Consume via UseContext() hook.
/// </summary>
public sealed class Context<T> : ContextBase
{
    public T DefaultValue { get; }
    internal string? DebugName { get; }

    public Context(T defaultValue, [CallerMemberName] string? name = null)
    {
        DefaultValue = defaultValue;
        DebugName = name;
    }

    internal override object? DefaultValueBoxed => DefaultValue;

    internal override bool CurrentValueEquals(ContextScope scope, object? lastBoxed)
    {
        var current = scope.Read(this); // strongly-typed read — no boxing of the current value
        if (current is null)
            return lastBoxed is null;
        if (lastBoxed is null)
            return false;
        // For two non-null operands, object.Equals(current, lastBoxed) is exactly
        //   ReferenceEquals(current, lastBoxed) || current.Equals(lastBoxed)
        // Reproduce that without boxing the freshly-read value (perf #25):
        //   * reference types — keep the ReferenceEquals fast-path: it covers the common
        //     "same instance, unchanged" context value AND the only case where a
        //     non-reflexive Equals override would otherwise diverge from object.Equals.
        //     The fall-back virtual Equals(object) preserves reference-equality change
        //     detection for a type implementing IEquatable<T> WITHOUT an object.Equals
        //     override (a value-equality collapse there would skip a required rerender).
        //   * value types — the constrained call binds to the Equals(object) override, so
        //     primitives and Nullable<T> compare without a box; a boxed value of a DIFFERENT
        //     type returns false rather than throwing, exactly as the prior boxed
        //     object.Equals did. ReferenceEquals is skipped — distinct boxes are never
        //     reference-equal, and testing it would needlessly box the current value.
        // Routing value types through EqualityComparer<T>.Default would instead dispatch to
        // IEquatable<T>, diverging from object.Equals for any struct whose IEquatable<T>.Equals
        // disagrees with its object.Equals override.
        if (!typeof(T).IsValueType && ReferenceEquals(current, lastBoxed))
            return true;
        return current.Equals(lastBoxed);
    }
}
