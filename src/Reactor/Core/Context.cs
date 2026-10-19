using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Non-generic base for type-erased storage in the context scope stack.
/// </summary>
public abstract class ContextBase
{
    internal abstract object? DefaultValueBoxed { get; }
    // Diagnostics (ReactorDiagnostics.Components): non-generic access to T and the
    // declaration name without reflection.
    internal abstract Type ValueType { get; }
    internal virtual string? DiagnosticName => null;
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
    internal override Type ValueType => typeof(T);
    internal override string? DiagnosticName => DebugName;
}
