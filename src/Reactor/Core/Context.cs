using System.Collections.Generic;
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
        // Reproduce the previous object.Equals(current, lastBoxed) semantics EXACTLY,
        // while still avoiding a box of the freshly-read value in the common value-type
        // case (perf #25):
        //   * value types     — guard the unbox with `is T`: a same-slot context-type
        //     swap can leave `lastBoxed` holding a different boxed type, and the prior
        //     object.Equals returned false there (never threw), so a mismatch is false,
        //     not an InvalidCastException. The matching case routes through
        //     EqualityComparer<T>.Default (IEquatable<T>/ValueType.Equals) without boxing
        //     `current`, consistent with object.Equals for a well-behaved struct.
        //   * reference types — invoke the virtual object.Equals directly (no unbox cast)
        //     so a type implementing IEquatable<T> WITHOUT overriding object.Equals keeps
        //     the prior reference-equality change-detection behavior instead of silently
        //     collapsing distinct instances (which would skip a required rerender).
        return typeof(T).IsValueType
            ? (lastBoxed is T typedLast && EqualityComparer<T>.Default.Equals(current, typedLast))
            : current.Equals(lastBoxed);
    }
}
