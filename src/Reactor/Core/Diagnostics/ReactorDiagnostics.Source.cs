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
}
