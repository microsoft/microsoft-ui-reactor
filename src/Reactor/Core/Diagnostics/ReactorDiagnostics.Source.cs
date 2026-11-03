using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

public static partial class ReactorDiagnostics
{
    private static DependencyProperty? s_sourceProperty;

    /// <summary>
    /// Attached string property, registered as <c>"ReactorSource"</c>, that Reactor sets on
    /// every control it realizes while diagnostics publishing is on. Its value describes
    /// where the control came from — call site, owning component, element kind, key,
    /// declared name, and for component boundaries the component's hooks — in the v1 grammar:
    /// <code>
    /// v=1|at=path:line[:col][|rel=&lt;root|0&gt;]|owner=Comp|element=Kind[|mounts=Comp][|root=Comp][|key=k][|name=n][|hooks=i:name@line;...]
    /// </code>
    /// with <c>%</c> escaped as <c>%25</c> and <c>|</c> as <c>%7C</c> in values. <c>at</c> is never an
    /// absolute developer path: it is relative to the project directory, or to the
    /// solution/repo root (<c>rel=root</c>), or just the file name (<c>rel=0</c>) — the same
    /// posture as XAML's runtime source info, which reports <c>ms-appx:///</c> paths.
    ///
    /// <para><b>Why a string on the control.</b> An out-of-process inspector reads it through
    /// XamlDiagnostics like any other property (it is reported as <c>.ReactorSource</c>), with
    /// no managed code in the app — so it works for Native AOT apps, where no managed
    /// inspector agent can be loaded.</para>
    ///
    /// <para><b>When it is set.</b> Only when the app was built with the
    /// <c>Reactor.DevtoolsSupport</c> switch on AND launched with
    /// <c>REACTOR_DIAGNOSTICS=1</c>; see <see cref="IsSourcePublishingEnabled"/>. Otherwise no
    /// control carries it and the publishing code is trimmed from AOT/trimmed builds. Reading
    /// this property registers the attached property (once) but publishes nothing.</para>
    /// </summary>
    public static DependencyProperty SourceProperty => s_sourceProperty ??= DependencyProperty.RegisterAttached(
        "ReactorSource",
        typeof(string),
        typeof(ReactorDiagnostics),
        new PropertyMetadata(null));

    /// <summary>Reads <see cref="SourceProperty"/> from <paramref name="element"/>; <c>null</c> when unset.</summary>
    public static string? GetSource(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(SourceProperty) as string;
    }

    /// <summary>
    /// True when Reactor is publishing <see cref="SourceProperty"/> in this process: the
    /// <c>Reactor.DevtoolsSupport</c> build switch is on and the process was started with
    /// <c>REACTOR_DIAGNOSTICS=1</c>.
    /// </summary>
    public static bool IsSourcePublishingEnabled
        => Hosting.ReactorFeatures.DevtoolsSupported && ReactorSourcePublisher.IsEnabled;

    private static DependencyProperty? s_itemSiteProperty;

    // A menu or command-bar item control (built from an item record, so it has no element tag)
    // records its exact call site for GetSource. It is held as a string id, not as a managed
    // object: a string value is a native HSTRING, while a managed value on a control that sits
    // outside the live tree (a closed CommandBarFlyout's commands) can be collected while the
    // control still refers to it, after which reading the property throws. The id maps back to
    // the SourceLocation through a table bounded by the app's call sites.
    private static DependencyProperty ItemSiteProperty => s_itemSiteProperty ??= DependencyProperty.RegisterAttached(
        "ReactorItemSite",
        typeof(string),
        typeof(ReactorDiagnostics),
        new PropertyMetadata(null));

    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<SourceLocation, string> s_itemSiteIds = new();
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, SourceLocation> s_itemSites = new(StringComparer.Ordinal);
    private static int s_nextItemSite;

    /// <summary>Records (or, for <c>null</c>, clears) a menu or command-bar item control's exact call site.</summary>
    internal static void SetItemSite(DependencyObject control, SourceLocation? site)
    {
        if (site is not { } location)
        {
            if (control.GetValue(ItemSiteProperty) is not null) control.ClearValue(ItemSiteProperty);
            return;
        }
        var id = s_itemSiteIds.GetOrAdd(location, static loc =>
        {
            var newId = global::System.Threading.Interlocked.Increment(ref s_nextItemSite)
                .ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            s_itemSites[newId] = loc;
            return newId;
        });
        if (!string.Equals(control.GetValue(ItemSiteProperty) as string, id, StringComparison.Ordinal))
            control.SetValue(ItemSiteProperty, id);
    }

    /// <summary>The exact call site <see cref="SetItemSite"/> recorded on <paramref name="control"/>, if any.</summary>
    internal static SourceLocation? GetItemSite(DependencyObject control)
        => s_itemSiteProperty is not null && control.GetValue(ItemSiteProperty) is string id && s_itemSites.TryGetValue(id, out var site)
            ? site
            : null;
}
