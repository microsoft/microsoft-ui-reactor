using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using WinUITableView = Microsoft.UI.Xaml.Controls.TableView;
using AdvancedXamlMetadataProvider = Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsAdvancedXamlMetaDataProvider;

namespace Reactor.Controls;

/// <summary>
/// Makes a native TableView render in a code-only Reactor host: registers the satellite control's
/// XAML metadata provider (so the WinUI XAML loader can resolve the advanced types), merges the
/// control's default Style + theme-resource closure (shipped as embedded XAML), and assigns the
/// Style explicitly.
/// </summary>
/// <remarks>
/// Three things are needed because the satellite control ships its default Style/ControlTemplate in
/// its own generic.xbf (under the framework resource path, which a loose consumer can't load), and a
/// code-only Reactor app has no XAML files of its own and therefore no compiler-generated metadata
/// provider:
/// <list type="number">
/// <item>Register the advanced XAML metadata provider via <see cref="ReactorApp.RegisterControlAssembly(IXamlMetadataProvider)"/>
/// so <c>XamlReader.Load</c> can resolve <c>controls:TableView</c> and the primitive types in the template.</item>
/// <item>Parse the embedded Style/theme closure and merge it into <see cref="Application"/>.Resources.</item>
/// <item>Assign the control's default Style explicitly (implicit-style lookup misses a loosely-consumed control).</item>
/// </list>
/// Without these the control activates with data but renders a blank body.
/// </remarks>
public static class TableViewStyles
{
    private static bool s_init;
    private static Style? s_tvStyle;
    private static readonly object s_gate = new object();

    /// <summary>Diagnostic status of the last init attempt (surfaced by the demo selftest).</summary>
    internal static string Status { get; private set; } = "(not initialized)";

    /// <summary>
    /// Public, idempotent, process-wide enablement entry point. Registers the satellite XAML metadata
    /// provider and merges the control's Style/theme closure into <see cref="Application"/>.Resources
    /// EXACTLY ONCE. Any second consumer in the same process (e.g. the embedded TableViewSamples gallery)
    /// MUST funnel through this method rather than merging its own copy of the same closure — two copies
    /// of the advanced theme dictionaries in Application.Resources corrupts native resource lookup and
    /// access-violates the next Frame.Navigate.
    /// </summary>
    public static void EnsureInitialized() => EnsureInit();

    /// <summary>The satellite control's default Style (captured from the single merged closure).</summary>
    public static Style? DefaultStyle { get { EnsureInit(); return s_tvStyle; } }

    /// <summary>Registers the satellite XAML metadata provider. Idempotent; safe to call early.</summary>
    internal static void RegisterMetadata()
    {
        try { ReactorApp.RegisterControlAssembly(new AdvancedXamlMetadataProvider()); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[TableViewStyles] register failed: " + ex); }
    }

    /// <summary>Ensures metadata + style closure are loaded and applies the default Style to <paramref name="tv"/>.</summary>
    public static void EnsureLoadedAndApply(WinUITableView tv)
    {
        EnsureInit();
        if (s_tvStyle != null)
        {
            tv.Style = null;
            tv.Style = s_tvStyle;
            try { tv.ApplyTemplate(); } catch { /* inflates on first layout if this no-ops */ }
        }
        if (double.IsNaN(tv.Height) && tv.MinHeight < 1)
            tv.MinHeight = 420;

        // Nudge a layout pass once the control is in the live tree. The native TableView realizes its
        // header host (PinnedRegionPresenter) + body rows (ItemsRepeater) during measure/arrange; in a
        // nested code-only host the first natural pass can leave the body unrealized, so re-run layout
        // on Loaded (and once more low-priority) to force realization.
        tv.Loaded -= s_loadedNudge;
        tv.Loaded += s_loadedNudge;
    }

    private static readonly Microsoft.UI.Xaml.RoutedEventHandler s_loadedNudge = static (s, _) =>
    {
        var tv = (WinUITableView)s;
        try { tv.UpdateLayout(); } catch { }
        try
        {
            var dq = tv.DispatcherQueue;
            dq?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                try { tv.UpdateLayout(); } catch { }
            });
        }
        catch { }
    };

    private static void EnsureInit()
    {
        if (s_init)
            return;
        lock (s_gate)
        {
            if (s_init)
                return;
            s_init = true;
            try
            {
                // The native TableView realizes its header host + body cells inside async (Task.Yield)
                // continuations that must resume on the UI thread via the DispatcherQueue
                // SynchronizationContext. A normal compiled WinUI app installs one automatically; a
                // code-only Reactor host running on its own STA thread may not, so those continuations
                // resume off-context and silently fault (RPC_E_WRONG_THREAD, swallowed in async void) —
                // the control activates and its outer template inflates but headers/cells never populate.
                // Install one if missing so the realization continuations complete on the UI thread.
                var hadCtx = System.Threading.SynchronizationContext.Current != null;
                if (!hadCtx)
                {
                    try
                    {
                        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                        if (dq != null)
                            System.Threading.SynchronizationContext.SetSynchronizationContext(
                                new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(dq));
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[TableViewStyles] sync-ctx install failed: " + ex); }
                }

                RegisterMetadata();
                var appRes = Application.Current?.Resources;

                // Merge the curated embedded Styles/*.xaml closure (the satellite control's default Style +
                // theme/primitive styles) into app resources, and capture the outer TableView Style for
                // explicit assignment (implicit lookup misses a loosely-consumed control). This is the
                // single, process-wide closure merge -- the embedded gallery defers to this method so the
                // same advanced theme dictionaries are never merged twice.
                var asm = typeof(TableViewStyles).Assembly;
                var names = asm.GetManifestResourceNames()
                    .Where(n => n.Contains(".Styles.") && n.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
                if (names.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.Append("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><ResourceDictionary.MergedDictionaries>");
                    foreach (var n in names)
                    {
                        using var stream = asm.GetManifestResourceStream(n);
                        if (stream == null)
                            continue;
                        using var reader = new StreamReader(stream);
                        sb.Append(reader.ReadToEnd());
                    }
                    sb.Append("</ResourceDictionary.MergedDictionaries></ResourceDictionary>");
                    var closure = (ResourceDictionary)XamlReader.Load(sb.ToString());
                    if (appRes != null && !appRes.MergedDictionaries.Contains(closure))
                        appRes.MergedDictionaries.Add(closure);
                    s_tvStyle = FindControlStyle(closure);
                }

                // Best-effort: also merge the satellite's COMPLETE resource dictionary (its XamlControlsResources
                // equivalent) for any inner-type implicit styles not in the curated subset. Loads only when the
                // dll's own resource map is reachable; harmless (additive) otherwise.
                bool advMerged = false;
                try
                {
                    var advRes = new Microsoft.UI.Xaml.Controls.AdvancedControlsResources();
                    if (appRes != null && !appRes.MergedDictionaries.Contains(advRes))
                    {
                        appRes.MergedDictionaries.Add(advRes);
                        advMerged = true;
                    }
                    s_tvStyle ??= FindControlStyle(advRes);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[TableViewStyles] AdvancedControlsResources merge failed: " + ex);
                }

                Status = "ok: syncCtxWasPresent=" + hadCtx + ", advResources=" + advMerged + ", styleFound=" + (s_tvStyle != null);
            }
            catch (Exception ex)
            {
                Status = "init error: " + ex.GetType().Name + ": " + ex.Message;
                System.Diagnostics.Debug.WriteLine("[TableViewStyles] init failed: " + ex);
            }
        }
    }

    private static Style? FindControlStyle(ResourceDictionary d)
    {
        foreach (var kv in d)
        {
            if (kv.Value is Style s
                && s.TargetType == typeof(WinUITableView)
                && s.Setters.Any(st => st is Setter se && se.Property == Control.TemplateProperty))
                return s;
        }
        foreach (var md in d.MergedDictionaries)
        {
            var r = FindControlStyle(md);
            if (r != null)
                return r;
        }
        foreach (var td in d.ThemeDictionaries.Values)
        {
            if (td is ResourceDictionary themeDict)
            {
                var r = FindControlStyle(themeDict);
                if (r != null)
                    return r;
            }
        }
        return null;
    }
}
