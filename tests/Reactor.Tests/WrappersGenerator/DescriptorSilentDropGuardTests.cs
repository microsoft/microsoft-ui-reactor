using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.WrappersGenerator;

/// <summary>
/// Regression guard (spec 058 §15) — a descriptor-only migration must not
/// <b>silently drop</b> a record value-prop. The generator only maps control
/// properties whose type it supports (see <see cref="Classify"/>); a record prop
/// whose backing control property is an <b>unsupported type</b> (e.g.
/// <c>ParallaxView.Source : UIElement</c>) is dropped with NO compile error and NO
/// unit/selftest failure unless the author covers it with <c>[WrapManual]</c> or
/// <c>Exclude</c> — build + unit + selftests all passed for the original
/// <c>ParallaxView.Source</c> drop; only this guard caught it.
///
/// <para>This runs <b>always</b> (real CI gate) and FAILS on any uncovered drop
/// among the controls actually annotated <c>[GenerateReactorDescriptor]</c>. The
/// reflection model below mirrors <c>WrapperGenerator.CollectMembers</c>'s rules
/// (member walk; supported scalar/text/enum/struct/data-interface types;
/// <c>{Prop}Changed</c>-paired props are controlled, not dropped). Keep it in sync
/// with the generator's type support.</para>
///
/// <para>(The former env-gated comprehensive parity report that used to live
/// alongside this guard was scaffolding for the built-in migration campaign and
/// has been removed; the remaining capability gap — keyed/templated/virtualized
/// items + multi-select — is tracked in spec 058 §11.)</para>
/// </summary>
public class DescriptorSilentDropGuardTests
{
    private enum Mode { OneWay, Controlled }

    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only architecture/contract guard: enumerates all types in the Reactor assembly (Assembly.GetTypes) and reflects over their members by design — exactly the full-surface scan trimming would prune. This host is never trimmed; the analyzer-on state keeps guarding new reflection elsewhere. Behaviour-neutral.")]
    public void MigratedDescriptors_DoNotSilentlyDropUnsupportedTypeProps()
    {
        var failures = new List<string>();
        var migratedCount = 0;

        foreach (var element in typeof(Element).Assembly.GetTypes()
                     .Where(t => typeof(Element).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var attrs = element.GetCustomAttributesData();
            var desc = attrs.FirstOrDefault(a => a.AttributeType.Name == "GenerateReactorDescriptorAttribute");
            if (desc is null || desc.ConstructorArguments.Count < 1 || desc.ConstructorArguments[0].Value is not Type control)
                continue;
            migratedCount++;

            // Props the author intentionally handles (so a missing auto-mapping is
            // expected, not a silent drop): Exclude on the attribute, plus every
            // [WrapManual] / [WrapConvert] prop.
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var na in desc.NamedArguments)
                if (na.MemberName == "Exclude" && na.TypedValue.Value is IReadOnlyCollection<CustomAttributeTypedArgument> ex)
                    foreach (var e in ex) if (e.Value is string s) covered.Add(s);
            foreach (var a in attrs)
                if ((a.AttributeType.Name == "WrapManualAttribute" || a.AttributeType.Name == "WrapConvertAttribute")
                    && a.ConstructorArguments.Count >= 1 && a.ConstructorArguments[0].Value is string mp)
                    covered.Add(mp);

            // [WrapAlias(Name, Ctrl)] — the record's Name maps to control prop Ctrl.
            var aliasMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var a in attrs)
                if (a.AttributeType.Name == "WrapAliasAttribute" && a.ConstructorArguments.Count >= 2
                    && a.ConstructorArguments[0].Value is string an && a.ConstructorArguments[1].Value is string cn)
                    aliasMap[an] = cn;

            foreach (var (name, mp) in ManualSurface(element).Props)
            {
                if (mp.Mode != Mode.OneWay) continue;   // controlled props are surfaced
                if (covered.Contains(name)) continue;
                var ctrlName = aliasMap.TryGetValue(name, out var c) ? c : name;
                var cp = control.GetProperty(ctrlName, BindingFlags.Public | BindingFlags.Instance);
                // The bug class: the control HAS the property but its type is one
                // the generator cannot map → the prop is dropped. (A record prop
                // with no matching control property at all is a separate, usually
                // intentional bespoke/computed case and is not flagged here.)
                if (cp is { SetMethod: { IsPublic: true } } && Classify(cp.PropertyType) is null)
                    failures.Add(
                        $"{element.Name}.{name} → {control.Name}.{ctrlName} ({Short(cp.PropertyType)}) is an unsupported " +
                        $"value type, so the generated descriptor SILENTLY DROPS it. Add [WrapManual(\"{name}\")] " +
                        $"(map it in the Customize hook) or Exclude it.");
            }
        }

        Assert.True(failures.Count == 0,
            "Generated descriptors silently drop record value-props (spec 058 §15):\n  " +
            string.Join("\n  ", failures));

        // Self-validation: the test must actually have inspected the migrated
        // catalog. If reflection ever stops seeing the [GenerateReactorDescriptor]
        // attribute (e.g. it's trimmed from metadata) the loop would no-op and the
        // guard would pass vacuously — so assert a healthy lower bound.
        Assert.True(migratedCount >= 20,
            $"Expected to inspect 20+ [GenerateReactorDescriptor] elements but found {migratedCount} — " +
            "the silent-drop guard may be running vacuously.");
    }

    // ── Manual surface (the hand-written element record) ──────────────────
    private sealed record ManualProp(Mode Mode, Type Type);
    private sealed record ManualModel(Dictionary<string, ManualProp> Props, bool HasContent, string ContentKind);

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070",     Justification = "Test-only architecture/contract guard: reflects public instance properties of an element type enumerated by the surrounding Assembly.GetTypes scan (which trimming would prune). Intentional and JIT-only (this host is never trimmed); behaviour-neutral.")]
    private static ManualModel ManualSurface(Type element)
    {
        var baseProps = typeof(Element).GetProperties().Select(p => p.Name).ToHashSet();
        baseProps.Add("Setters");
        var optionalDef = typeof(Optional<>);

        var props = new Dictionary<string, ManualProp>();
        var hasContent = false;
        var contentKind = "none";

        foreach (var p in element.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (baseProps.Contains(p.Name)) continue;
            var t = p.PropertyType;

            if (t.IsGenericType && t.GetGenericTypeDefinition() == optionalDef)
            {
                props[p.Name] = new ManualProp(Mode.Controlled, t.GetGenericArguments()[0]);
                continue;
            }

            // Reactor child slot(s).
            if (t == typeof(Element)) { hasContent = true; contentKind = "single"; continue; }
            if (IsElementCollection(t)) { hasContent = true; contentKind = "items"; continue; }

            // Items-control items collection: the manual element surfaces the
            // control's Items as a flat collection (typically string[] for
            // ListBox/ComboBox). The generator binds it through the ItemsHost
            // child strategy, so it's a content slot here, not a value prop.
            if (p.Name == "Items" && (t.IsArray || (typeof(IEnumerable).IsAssignableFrom(t) && t != typeof(string))))
            {
                hasContent = true; contentKind = "items"; continue;
            }

            // Callback delegates are folded into their controlled prop or are events.
            if (typeof(Delegate).IsAssignableFrom(t)) continue;

            // Everything else the record exposes is a one-way value/text/brush/… prop.
            props[p.Name] = new ManualProp(Mode.OneWay, t);
        }

        return new ManualModel(props, hasContent, contentKind);
    }

    private static bool IsElementCollection(Type t)
    {
        if (t == typeof(string)) return false;
        if (t.IsArray) return typeof(Element).IsAssignableFrom(t.GetElementType()) || t.GetElementType()!.Name.EndsWith("Data");
        if (typeof(IEnumerable).IsAssignableFrom(t) && t.IsGenericType)
        {
            var arg = t.GetGenericArguments()[0];
            return typeof(Element).IsAssignableFrom(arg) || arg.Name.EndsWith("Data");
        }
        return false;
    }

    // ── Generator type support (mirrors WrapperGenerator.Classify) ────────
    private readonly record struct Cls(bool IsObject);

    private static Cls? Classify(Type t)
    {
        if (t == typeof(string)) return new Cls(false);
        if (t == typeof(object)) return new Cls(true);
        if (t.IsEnum) return new Cls(false);
        if (t == typeof(bool) || t == typeof(int) || t == typeof(double)) return new Cls(false);

        // Value-type struct (Thickness, …) and Nullable<U> tri-state (bool?, …)
        // — both surfaced (Nullable is Optional<U?>-backed, spec 050).
        if (t.IsValueType)
            return new Cls(false);

        // Reference type (Brush, FontFamily, INumberFormatter2, ICommand, …), excluding
        // delegates/arrays/collections/templates/styles and UIElement-derived (content).
        // Plain data interfaces are surfaced as raw nullable one-way value props.
        if (!t.IsValueType && IsSupportedReference(t))
            return new Cls(false);

        return null;
    }

    private static bool IsSupportedReference(Type t)
    {
        if (typeof(Delegate).IsAssignableFrom(t) || t.IsArray) return false;
        var name = t.FullName ?? t.Name;
        switch (name)
        {
            case "Microsoft.UI.Xaml.DataTemplate":
            case "Microsoft.UI.Xaml.Controls.ControlTemplate":
            case "Microsoft.UI.Xaml.ResourceDictionary":
            case "Microsoft.UI.Xaml.Controls.DataTemplateSelector":
                return false;
        }
        if (typeof(IEnumerable).IsAssignableFrom(t)) return false;
        if (typeof(Microsoft.UI.Xaml.UIElement).IsAssignableFrom(t)) return false;
        return true;
    }

    private static string Short(Type t) => t.IsGenericType
        ? t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(a => a.Name)) + ">"
        : t.Name;
}
