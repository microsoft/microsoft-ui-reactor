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
        if (lastBoxed is null)
            return current is null;
        return current is not null && EqualityComparer<T>.Default.Equals(current, (T)lastBoxed);
    }
}
