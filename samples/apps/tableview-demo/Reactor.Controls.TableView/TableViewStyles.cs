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
internal static class TableViewStyles
{
    private static bool s_init;
    private static Style? s_tvStyle;
    private static readonly object s_gate = new object();

    /// <summary>Diagnostic status of the last init attempt (surfaced by the demo selftest).</summary>
    internal static string Status { get; private set; } = "(not initialized)";

    /// <summary>Registers the satellite XAML metadata provider. Idempotent; safe to call early.</summary>
    internal static void RegisterMetadata()
    {
        try { ReactorApp.RegisterControlAssembly(new AdvancedXamlMetadataProvider()); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[TableViewStyles] register failed: " + ex); }
    }

    /// <summary>Ensures metadata + style closure are loaded and applies the default Style to <paramref name="tv"/>.</summary>
    internal static void EnsureLoadedAndApply(WinUITableView tv)
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
    }

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
                RegisterMetadata();

                var asm = typeof(TableViewStyles).Assembly;
                var names = asm.GetManifestResourceNames()
                    .Where(n => n.Contains(".Styles.") && n.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
                if (names.Count == 0)
                {
                    Status = "no embedded Styles/*.xaml";
                    return;
                }

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
                var appRes = Application.Current?.Resources;
                if (appRes != null && !appRes.MergedDictionaries.Contains(closure))
                    appRes.MergedDictionaries.Add(closure);
                s_tvStyle = FindControlStyle(closure);
                Status = "ok: closure merged, styleFound=" + (s_tvStyle != null);
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
        return null;
    }
}
