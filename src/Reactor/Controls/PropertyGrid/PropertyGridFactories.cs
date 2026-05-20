using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// Creates a PropertyGrid element for editing the target object's properties.
    /// Keyed by target type so switching between different target types creates
    /// a fresh component (avoiding broken reconciliation of different structures).
    /// </summary>
    public static ComponentElement<PropertyGridElement> PropertyGrid(
        object target,
        TypeRegistry registry,
        Action<object>? onRootChanged = null)
    {
        var props = new PropertyGridElement
        {
            Target = target,
            Registry = registry,
            OnRootChanged = onRootChanged,
        };

        return Component<PropertyGridComponent, PropertyGridElement>(props)
            .WithKey($"pg-{target.GetType().FullName}");
    }
}
