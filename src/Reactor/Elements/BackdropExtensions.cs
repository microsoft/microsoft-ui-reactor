using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Declarative <see cref="SystemBackdrop"/> modifier (spec 033 §6). Applied to a
/// Reactor root element, the modifier instructs the hosting Reactor host to set
/// the corresponding <see cref="Microsoft.UI.Xaml.Window.SystemBackdrop"/> on its
/// owning window.
/// </summary>
/// <remarks>
/// <para>
/// When applied inside a <c>ReactorHostControl</c> that does not own its window
/// (e.g. embedded in an existing XAML page), the modifier is a no-op and emits a
/// debug-log notice. Backdrops are a window-level concept; we do not silently
/// climb the visual tree to the parent <c>Window</c>.
/// </para>
/// </remarks>
public static class BackdropExtensions
{
    /// <summary>
    /// Sets the system backdrop on the hosting window using a built-in
    /// <see cref="BackdropKind"/> selector.
    /// </summary>
    public static T Backdrop<T>(this T el, BackdropKind kind) where T : Element
    {
        if (el is null) throw new global::System.ArgumentNullException(nameof(el));
        return ElementExtensionsBackdropHelper.Modify(el, BackdropChoice.Of(kind));
    }

    /// <summary>
    /// Sets the system backdrop on the hosting window using a custom factory.
    /// Use this overload to apply tinted Mica, custom blur tints, or any other
    /// <see cref="SystemBackdrop"/> subclass.
    /// </summary>
    public static T Backdrop<T>(this T el, global::System.Func<SystemBackdrop?> factory) where T : Element
    {
        if (el is null) throw new global::System.ArgumentNullException(nameof(el));
        if (factory is null) throw new global::System.ArgumentNullException(nameof(factory));
        return ElementExtensionsBackdropHelper.Modify(el, BackdropChoice.Of(factory));
    }
}

internal static class ElementExtensionsBackdropHelper
{
    internal static T Modify<T>(T el, BackdropChoice choice) where T : Element
    {
        var existing = el.Modifiers ?? new ElementModifiers();
        var merged = existing with { Backdrop = choice };
        return (T)(el with { Modifiers = merged });
    }
}
