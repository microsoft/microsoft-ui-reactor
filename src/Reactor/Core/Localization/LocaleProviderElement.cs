using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Localization;

/// <summary>
/// A Reactor element that establishes a locale context for its subtree.
/// Sets FlowDirection on the host and provides IntlAccessor to descendants via UseIntl().
/// </summary>
public record LocaleProviderElement(
    string Locale,
    Element Child,
    IStringResourceProvider? ResourceProvider = null,
    string DefaultLocale = "en-US",
    bool PseudoLocalize = false) : Element;

/// <summary>
/// Component that manages the LocaleProvider lifecycle: creates the LocaleContext,
/// updates it when the locale prop changes, and handles FlowDirection.
/// </summary>
internal sealed class LocaleProviderComponent : Component<LocaleProviderElement>
{
    private static readonly MessageCache SharedMessageCache = new();

    public override Element Render()
    {
        var locale = Props.Locale;
        var defaultLocale = Props.DefaultLocale;
        var pseudoLocalize = Props.PseudoLocalize;
        var resourceProvider = Props.ResourceProvider ?? new ReswResourceProvider(defaultLocale);

        // Create the IntlAccessor for this locale
        var accessor = Context.UseMemo(() =>
            new IntlAccessor(locale, resourceProvider, SharedMessageCache, defaultLocale, pseudoLocalize),
            locale, defaultLocale, pseudoLocalize);

        // Legacy: manage the old LocaleContext.Current lifecycle for backward compat
        Context.UseEffect(() =>
        {
            var ctx = new LocaleContext(accessor);
            var previous = LocaleContext.Current;
            LocaleContext.Current = ctx;

            return () =>
            {
                LocaleContext.Current = previous;
            };
        });

        Context.UseEffect(() =>
        {
            if (LocaleContext.Current is { } ctx)
            {
                ctx.UpdateAccessor(accessor);
                SharedMessageCache.Flush();
            }
        }, locale);

        // Wrap the child in a Border that sets FlowDirection on the visual tree.
        // WinUI inherits FlowDirection down the tree, so all descendants get RTL/LTR
        // layout automatically when the locale changes.
        // Provide the IntlAccessor via Context so descendants can UseContext(IntlContexts.Locale).
        var direction = accessor.Direction;
        return (Border(Props.Child) with
        {
            Setters = [b => b.FlowDirection = direction]
        }).Provide(IntlContexts.Locale, accessor);
    }
}
