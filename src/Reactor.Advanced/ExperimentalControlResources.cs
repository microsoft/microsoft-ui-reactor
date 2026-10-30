using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Advanced;

/// <summary>
/// Merges the theme dictionaries that the experimental WinUI control families
/// (<c>Microsoft.UI.Xaml.Controls.Tabular</c> and <c>Microsoft.UI.Xaml.Controls.Charts</c>)
/// need into <see cref="Application.Resources"/>.
/// </summary>
/// <remarks>
/// <para>
/// A XAML app declares <c>&lt;TabularControlsResources/&gt;</c> /
/// <c>&lt;XamlChartsResources/&gt;</c> in <c>App.xaml</c> after
/// <c>XamlControlsResources</c>; without them the controls have no default style
/// and render empty. Reactor apps have no <c>App.xaml</c> to edit, so the
/// TableView and Chart handlers call <see cref="EnsureMerged{T}"/> before their
/// first control is created. An app that already merged the dictionary (or
/// merged it itself, earlier) is detected and left untouched.
/// </para>
/// <para>
/// The dictionaries load their themes from <c>ms-appx:///</c>, which an unpackaged app
/// resolves only when it is self-contained (<c>WindowsAppSDKSelfContained=true</c>) or
/// MSIX-packaged; a framework-dependent unpackaged app gets an explanatory
/// <see cref="InvalidOperationException"/> instead of an opaque COM error.
/// </para>
/// </remarks>
internal static class ExperimentalControlResources
{
    private static readonly HashSet<Type> s_merged = new();

    internal static void EnsureMerged<T>() where T : ResourceDictionary, new()
    {
        if (s_merged.Contains(typeof(T))) return;

        var resources = Application.Current?.Resources;
        if (resources is null) return;

        foreach (var merged in resources.MergedDictionaries)
        {
            if (merged is T)
            {
                s_merged.Add(typeof(T));
                return;
            }
        }

        T dictionary;
        try
        {
            dictionary = new T();
        }
        catch (global::System.Runtime.InteropServices.COMException ex)
        {
            // An unpackaged, framework-dependent app cannot resolve the controls' ms-appx:///
            // theme resources: their .pri files are only merged into the app's resources.pri
            // when the app is self-contained (or packaged).
            throw new InvalidOperationException(
                $"Could not load {typeof(T).Name}, the theme resources for the experimental WinUI " +
                "TableView/Chart controls. An unpackaged app must set " +
                "<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained> (or be MSIX-packaged) so " +
                "the controls' resource (.pri) files are merged into the app's resources.", ex);
        }

        resources.MergedDictionaries.Add(dictionary);
        s_merged.Add(typeof(T));
    }
}
