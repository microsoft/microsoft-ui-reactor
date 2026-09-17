using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Keeps <see cref="ModifierTable"/> honest.
/// <para>
/// <see cref="PoolResetSetConsistencyTests"/> already guards the pool-reset half of the
/// table against <c>ElementPool.CleanElement</c> drift. These tests cover the other half —
/// the "a modifier exists, prefer it over <c>.Set</c>" entries — where the failure mode is
/// different: the table silently falls behind the DSL as modifiers are added, which is
/// exactly how the original 12-entry list went stale while ~144 <c>.Set</c> sites with
/// modifiers went undiagnosed.
/// </para>
/// <para>
/// Two classes of guarantee:
/// </para>
/// <list type="number">
/// <item><description><b>Integrity</b> — every entry names a modifier that really exists,
/// and every element type really declares it. A wrong entry makes
/// <c>PoolResetSetCodeFix</c> emit code that does not compile.</description></item>
/// <item><description><b>Staleness</b> — a newly added generic modifier whose name matches
/// a settable WinUI dependency property must be classified, either into the table or into
/// <see cref="ModifierTable.DeliberatelyExcluded"/> with a reason. Adding a modifier
/// therefore forces a decision instead of silently widening the gap.</description></item>
/// </list>
/// </summary>
public class ModifierTableIntegrityTests
{
    // ── Integrity ────────────────────────────────────────────────────────────

    [Fact]
    public void Every_Entry_Names_A_Modifier_That_Exists()
    {
        var generic = ReadGenericModifierNames();
        var typeSpecific = ReadTypeSpecificModifiers();

        var broken = new List<string>();
        foreach (var (prop, info) in ModifierTable.Properties)
        {
            var exists = generic.Contains(info.Modifier)
                || typeSpecific.ContainsKey(info.Modifier);
            if (!exists)
                broken.Add($"{prop} -> .{info.Modifier}()");
        }

        Assert.True(
            broken.Count == 0,
            "These ModifierTable entries name a modifier that does not exist in " +
            "ElementExtensions*.cs, so PoolResetSetCodeFix would rewrite '.Set(...)' into a " +
            $"call that does not compile: [{string.Join(", ", broken)}]. " +
            "Fix the modifier name, or drop the entry.");
    }

    [Fact]
    public void Every_TypeSpecific_Entry_Lists_Only_Element_Types_That_Declare_It()
    {
        // This is the assertion that keeps the code fix sound for the type-specific half.
        // `.TextWrapping(...)` compiles on TextBlockElement but not on, say, BorderElement —
        // so if the listed element types drift from what ElementExtensions actually declares,
        // the fix silently starts producing uncompilable rewrites on the extra types.
        var typeSpecific = ReadTypeSpecificModifiers();

        var wrong = new List<string>();
        foreach (var (prop, info) in ModifierTable.Properties)
        {
            if (info.ElementTypes is null)
                continue;

            if (!typeSpecific.TryGetValue(info.Modifier, out var declaredOn))
            {
                wrong.Add($"{prop}: '.{info.Modifier}()' has no type-specific overloads at all");
                continue;
            }

            foreach (var listed in info.ElementTypes.Where(listed => !declaredOn.Contains(listed)))
                wrong.Add($"{prop}: '.{info.Modifier}()' is NOT declared on {listed}");
        }

        Assert.True(
            wrong.Count == 0,
            "ModifierTable lists element types that do not declare the modifier: " +
            $"[{string.Join("; ", wrong)}]. The code fix would emit a call that does not " +
            "compile on those receivers.");
    }

    [Fact]
    public void TypeSpecific_Entries_Do_Not_Omit_Element_Types_That_Declare_The_Modifier()
    {
        // The inverse of the previous test. Omissions are not unsafe — a missing element
        // type only costs a diagnostic — but they are silent, and they accumulate. Anything
        // deliberately left out (inline RichText* run/paragraph types, which are not
        // Elements and have no '.Set') is filtered rather than exempted case by case.
        var typeSpecific = ReadTypeSpecificModifiers();

        var missing = new List<string>();
        foreach (var (prop, info) in ModifierTable.Properties)
        {
            if (info.ElementTypes is null)
                continue;
            if (!typeSpecific.TryGetValue(info.Modifier, out var declaredOn))
                continue;

            // Only element records participate in the '.Set' DSL; the inline
            // RichTextParagraph / RichTextRun / RichTextHyperlink types do not.
            foreach (var declared in declaredOn.Where(declared =>
                declared.EndsWith("Element", StringComparison.Ordinal)
                && !info.ElementTypes.Contains(declared)))
            {
                missing.Add($"{prop}: {declared} declares '.{info.Modifier}()' but is not listed");
            }
        }

        Assert.True(
            missing.Count == 0,
            "ModifierTable omits element types that declare the modifier, so REACTOR_MOD_002 " +
            $"will not fire on those receivers: [{string.Join("; ", missing)}]. " +
            "Add them to the entry's elementTypes list.");
    }

    [Fact]
    public void No_Property_Is_Both_Mapped_And_Excluded()
    {
        var overlap = ModifierTable.Properties.Keys
            .Where(ModifierTable.DeliberatelyExcluded.ContainsKey)
            .ToList();

        Assert.True(
            overlap.Count == 0,
            "These properties appear in BOTH ModifierTable.Properties and " +
            $"DeliberatelyExcluded, which is contradictory: [{string.Join(", ", overlap)}].");
    }

    [Fact]
    public void Every_Exclusion_Carries_A_Reason()
    {
        var blank = ModifierTable.DeliberatelyExcluded
            .Where(kvp => string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        Assert.True(
            blank.Count == 0,
            $"These exclusions have no documented reason: [{string.Join(", ", blank)}]. " +
            "An unexplained exclusion is indistinguishable from an oversight.");
    }

    // ── Staleness ────────────────────────────────────────────────────────────

    [Fact]
    public void Every_Generic_Modifier_Matching_A_Settable_WinUI_Property_Is_Classified()
    {
        // The load-bearing test. When someone adds `public static T Foo<T>(this T el, ...)`
        // and WinUI has a settable `Foo` property, `.Set(x => x.Foo = v)` becomes
        // rewritable — and without this test nobody would notice that the analyzer does not
        // know about it. Forcing a choice between "map it" and "exclude it with a reason"
        // is what stops the table drifting behind the DSL.
        var candidates = ReadGenericModifierNames()
            .Where(IsSettableWinUiProperty)
            .Where(name => !ModifierTable.Properties.ContainsKey(name))
            .Where(name => !ModifierTable.DeliberatelyExcluded.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            candidates.Count == 0,
            "These generic modifiers match a settable WinUI property but are neither mapped " +
            "in ModifierTable.Properties nor listed in ModifierTable.DeliberatelyExcluded: " +
            $"[{string.Join(", ", candidates)}]. " +
            "Either add a mapping (so REACTOR_MOD_002 suggests the modifier for " +
            "'.Set(x => x.PROP = ...)'), or add an exclusion explaining why the modifier is " +
            "not an equivalent replacement.");
    }

    [Fact]
    public void Every_Type_Specific_Modifier_Matching_A_Settable_WinUI_Property_Is_Classified()
    {
        // The generic test above cannot see a modifier that only exists in the type-specific
        // shape — including this table's own RichTextBlockElement font overloads. A property
        // whose ONLY modifier is type-specific would slip past unclassified, which is the
        // exact gap that let `.FontSize` on a RichTextBlock go unsuggested. Same forced
        // choice between "map it" and "exclude it with a reason", other declaration shape.
        var candidates = ReadTypeSpecificModifiers().Keys
            .Where(IsSettableWinUiProperty)
            .Where(name => !ModifierTable.Properties.ContainsKey(name))
            .Where(name => !ModifierTable.DeliberatelyExcluded.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            candidates.Count == 0,
            "These type-specific modifiers match a settable WinUI property but are neither " +
            "mapped in ModifierTable.Properties nor listed in ModifierTable.DeliberatelyExcluded: " +
            $"[{string.Join(", ", candidates)}]. " +
            "Either add a mapping with the declaring element types (so REACTOR_MOD_002 " +
            "suggests the modifier for '.Set(x => x.PROP = ...)' on those receivers), or add " +
            "an exclusion explaining why the modifier is not an equivalent replacement.");
    }

    /// <summary>
    /// True when one of the WinUI base types Reactor's modifiers target declares a public
    /// settable instance property with this name — i.e. a name a <c>.Set</c> lambda could
    /// plausibly assign. Reflection only reads metadata; no WinUI object is constructed, so
    /// this is safe in the headless test host.
    /// </summary>
    private static bool IsSettableWinUiProperty(string name) =>
        HasSettableProperty<Microsoft.UI.Xaml.UIElement>(name)
        || HasSettableProperty<Microsoft.UI.Xaml.FrameworkElement>(name)
        || HasSettableProperty<Microsoft.UI.Xaml.Controls.Control>(name)
        || HasSettableProperty<Microsoft.UI.Xaml.Controls.ContentControl>(name)
        || HasSettableProperty<Microsoft.UI.Xaml.Controls.Panel>(name)
        || HasSettableProperty<Microsoft.UI.Xaml.Controls.Border>(name)
        || HasSettableProperty<Microsoft.UI.Xaml.Controls.TextBlock>(name);

    // Generic + annotated rather than iterating a Type[]: the trim analyzer cannot see
    // through an array element to the reflection target, so the array form trips IL2075.
    private static bool HasSettableProperty<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string name)
    {
        var prop = typeof(T).GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        return prop is not null && prop.CanWrite;
    }

    // ── Attached-property integrity ──────────────────────────────────────────

    /// <summary>
    /// Owner types <see cref="ModifierTable.AttachedProperties"/> is allowed to name, with
    /// the reflection probes used to verify each entry against the real type.
    /// </summary>
    /// <remarks>
    /// Hand-listed rather than resolved from the entry's namespace string via
    /// <c>Type.GetType</c>, so the checks stay statically analyzable (IL2057) and so adding a
    /// new owner to the table is a deliberate two-place edit rather than a silent widening.
    /// Reflection only reads metadata; no WinUI object is constructed.
    /// </remarks>
    private static readonly Dictionary<string, (Func<string, bool> HasSetter, Func<string, bool> HasDependencyProperty, Func<string, Type?> SetterValueType)>
        KnownAttachedOwners = new(StringComparer.Ordinal)
        {
            ["Microsoft.UI.Xaml.Automation.AutomationProperties"] = (
                setter => HasStaticTwoArgMethod(typeof(Microsoft.UI.Xaml.Automation.AutomationProperties), setter),
                prop => HasDependencyPropertyField(typeof(Microsoft.UI.Xaml.Automation.AutomationProperties), prop),
                setter => StaticTwoArgValueType(typeof(Microsoft.UI.Xaml.Automation.AutomationProperties), setter)),
            ["Microsoft.UI.Xaml.Controls.ToolTipService"] = (
                setter => HasStaticTwoArgMethod(typeof(Microsoft.UI.Xaml.Controls.ToolTipService), setter),
                prop => HasDependencyPropertyField(typeof(Microsoft.UI.Xaml.Controls.ToolTipService), prop),
                setter => StaticTwoArgValueType(typeof(Microsoft.UI.Xaml.Controls.ToolTipService), setter)),
            ["Microsoft.UI.Xaml.Controls.TitleBar"] = (
                setter => HasStaticTwoArgMethod(typeof(Microsoft.UI.Xaml.Controls.TitleBar), setter),
                prop => HasDependencyPropertyField(typeof(Microsoft.UI.Xaml.Controls.TitleBar), prop),
                setter => StaticTwoArgValueType(typeof(Microsoft.UI.Xaml.Controls.TitleBar), setter)),
            ["Microsoft.UI.Reactor.Layout.FlexPanel"] = (
                setter => HasStaticTwoArgMethod(typeof(Microsoft.UI.Reactor.Layout.FlexPanel), setter),
                prop => HasDependencyPropertyField(typeof(Microsoft.UI.Reactor.Layout.FlexPanel), prop),
                setter => StaticTwoArgValueType(typeof(Microsoft.UI.Reactor.Layout.FlexPanel), setter)),
        };

    [Fact]
    public void Every_Attached_Entry_Matches_A_Real_Setter_And_DependencyProperty()
    {
        // The attached analog of Every_Entry_Names_A_Modifier_That_Exists, and stricter
        // because an attached entry carries three names that can each be wrong independently:
        // the owner, the static setter the analyzer matches at the call site, and the
        // dependency property PoolResetSetConsistencyTests scans CleanElement for. A typo in
        // the setter makes the rule silently stop firing; a typo in the property makes the
        // consistency invariant pass vacuously.
        var broken = new List<string>();

        foreach (var (key, info) in ModifierTable.AttachedProperties)
        {
            Assert.Equal(info.Owner + "." + info.Property, key);

            var ownerKey = info.OwnerNamespace + "." + info.Owner;
            if (!KnownAttachedOwners.TryGetValue(ownerKey, out var probes))
            {
                broken.Add($"{key}: unknown owner type '{ownerKey}' — add it to KnownAttachedOwners");
                continue;
            }

            if (!probes.HasSetter(info.Setter))
                broken.Add($"{key}: '{ownerKey}.{info.Setter}(_, _)' does not exist");

            if (!probes.HasDependencyProperty(info.Property))
                broken.Add($"{key}: '{ownerKey}.{info.Property}Property' is not a DependencyProperty field");
        }

        Assert.True(
            broken.Count == 0,
            "These ModifierTable.AttachedProperties entries do not match the real type: " +
            $"[{string.Join("; ", broken)}].");
    }

    [Fact]
    public void Every_Attached_Entry_Names_A_Generic_Modifier_That_Exists()
    {
        // Same guarantee as the instance table's version — a wrong modifier name makes
        // PoolResetSetCodeFix emit a call that does not compile — but resolved by reflection
        // over the built assembly rather than by scanning ElementExtensions*.cs, because
        // .Flex(...) lives in FlexExtensions.cs, which that source glob does not cover.
        var broken = ModifierTable.AttachedProperties
            .Where(pair => !DeclaredGenericModifiers.Value.Contains(pair.Value.Modifier))
            .Select(pair => $"{pair.Key} -> .{pair.Value.Modifier}()")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            broken.Count == 0,
            "These attached entries name a modifier that no Reactor extension class declares " +
            "as 'public static T Name<T>(this T el, ...)', so the suggestion — and any code " +
            $"fix built from it — would not compile: [{string.Join(", ", broken)}].");
    }

    [Fact]
    public void No_Attached_Property_Is_Both_Mapped_And_Excluded()
    {
        var overlap = ModifierTable.AttachedProperties.Keys
            .Where(ModifierTable.DeliberatelyExcludedAttached.ContainsKey)
            .ToList();

        Assert.True(
            overlap.Count == 0,
            "These attached properties appear in BOTH ModifierTable.AttachedProperties and " +
            $"DeliberatelyExcludedAttached, which is contradictory: [{string.Join(", ", overlap)}].");
    }

    [Fact]
    public void Every_Attached_Exclusion_Carries_A_Reason()
    {
        var blank = ModifierTable.DeliberatelyExcludedAttached
            .Where(kvp => string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        Assert.True(
            blank.Count == 0,
            $"These attached exclusions have no documented reason: [{string.Join(", ", blank)}]. " +
            "An unexplained exclusion is indistinguishable from an oversight.");
    }

    [Fact]
    public void Attached_Setter_Lookup_Covers_Every_Entry()
    {
        // AttachedBySetter is what the analyzer actually queries; AttachedProperties is what
        // the consistency test scans. A property/setter pair that collapses to the same
        // Owner.Setter key on two entries would silently drop one of them from the rule.
        Assert.Equal(ModifierTable.AttachedProperties.Count, ModifierTable.AttachedBySetter.Count);

        foreach (var (key, info) in ModifierTable.AttachedProperties)
        {
            Assert.True(
                ModifierTable.AttachedBySetter.TryGetValue(info.Owner + "." + info.Setter, out var viaSetter),
                $"'{key}' is missing from ModifierTable.AttachedBySetter.");
            Assert.Same(info, viaSetter);
        }
    }

    [Fact]
    public void Every_Attached_Setter_Is_Named_After_Its_DependencyProperty()
    {
        // Without this, a row could pair one property with an unrelated setter of the same
        // arity — e.g. FullDescription + SetName — and every other check would still pass:
        // both names exist on AutomationProperties, and the generated stubs in
        // PoolResetSetConsistencyTests are built FROM the row, so they would agree with it.
        // The result would be a diagnostic that names the wrong property and a consistency
        // invariant that silently stops covering the real one.
        var divergent = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // FlexPanel prefixes the DP to avoid colliding with FrameworkElement.MinWidth /
            // MinHeight, but leaves the setters unprefixed.
            ["FlexPanel.FlexMinWidth"] = "SetMinWidth",
            ["FlexPanel.FlexMinHeight"] = "SetMinHeight",
        };

        var mismatched = ModifierTable.AttachedProperties
            .Where(pair => divergent.TryGetValue(pair.Key, out var expected)
                ? pair.Value.Setter != expected
                : pair.Value.Setter != "Set" + pair.Value.Property)
            .Select(pair => $"{pair.Key} -> {pair.Value.Setter}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            mismatched.Count == 0,
            "These attached entries name a setter that does not correspond to their dependency " +
            $"property: [{string.Join(", ", mismatched)}]. Either fix the pairing, or — if the " +
            "owner really does name them differently — add the row to this test's 'divergent' " +
            "map with the reason.");
    }

    [Fact]
    public void Every_AutoFixable_Attached_Entry_Maps_One_To_One_Onto_Its_Modifier()
    {
        // The assertion behind AutoFix: true. PoolResetSetCodeFix passes the setter's single
        // value argument straight through to the modifier, so that is only sound when the
        // modifier really has a one-value overload whose parameter accepts the setter's value
        // type. Checked against the real DSL and the real owner type, so flipping any
        // AutoFix: false row to true fails here rather than shipping a rewrite that does not
        // compile — .PositionInSet takes two values, .Required() takes none, and .Flex(...)
        // takes eleven.
        var broken = new List<string>();

        foreach (var (key, info) in ModifierTable.AttachedProperties.Where(pair => pair.Value.AutoFix))
        {
            var ownerKey = info.OwnerNamespace + "." + info.Owner;
            if (!KnownAttachedOwners.TryGetValue(ownerKey, out var probes))
                continue; // Reported by Every_Attached_Entry_Matches_A_Real_Setter_And_DependencyProperty.

            var setterValueType = probes.SetterValueType(info.Setter);
            if (setterValueType is null)
                continue; // Same.

            // FixValueType is the documented escape for a setter typed more loosely than its
            // modifier: the analyzer refuses the fix unless the value really is that type, so
            // that — not the setter's parameter — is what the modifier must accept.
            var required = info.FixValueType;

            var accepted = ModifierValueTypes(info.Modifier);
            if (accepted.Count == 0)
            {
                broken.Add($"{key}: no '{info.Modifier}<T>(this T, value)' overload exists");
                continue;
            }

            var ok = accepted.Any(candidate => required is not null
                ? FullName(candidate) == required
                : AcceptsValueOfType(candidate, setterValueType));

            if (!ok)
            {
                var expected = required ?? FullName(setterValueType);
                broken.Add(
                    $"{key}: '{info.Modifier}' has no single-value overload accepting '{expected}' " +
                    $"(overloads accept: {string.Join(" | ", accepted.Select(FullName))})");
            }
        }

        Assert.True(
            broken.Count == 0,
            "These attached entries are marked AutoFix: true but the modifier does not line up " +
            $"1:1 with the setter, so the code fix would not compile: [{string.Join("; ", broken)}]. " +
            "Mark them AutoFix: false, or set fixValueType when the setter is merely typed more " +
            "loosely than the modifier.");
    }

    /// <summary>
    /// Value-parameter types of every <c>public static T Name&lt;T&gt;(this T el, X value)</c>
    /// overload — the single-value fluent shape the attached code fix rewrites into.
    /// </summary>
    private static List<Type> ModifierValueTypes(string modifier)
    {
        var types = new List<Type>();
        CollectModifierValueTypes(typeof(Microsoft.UI.Reactor.ElementExtensions), modifier, types);
        CollectModifierValueTypes(typeof(Microsoft.UI.Reactor.FlexExtensions), modifier, types);
        return types;
    }

    private static void CollectModifierValueTypes(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type,
        string modifier,
        List<Type> types)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.IsGenericMethodDefinition
                || !string.Equals(method.Name, modifier, StringComparison.Ordinal))
            {
                continue;
            }
            var typeArguments = method.GetGenericArguments();
            var parameters = method.GetParameters();
            if (typeArguments.Length != 1 || parameters.Length != 2)
                continue;
            if (parameters[0].ParameterType != typeArguments[0])
                continue;
            types.Add(parameters[1].ParameterType);
        }
    }

    /// <summary>
    /// True when a value of <paramref name="setterValueType"/> can be passed verbatim to a
    /// parameter of <paramref name="modifierParameterType"/>. Covers the identity case and the
    /// implicit <c>T</c> → <c>T?</c> lift (<c>TitleBar.SetIsDragRegion(_, bool)</c> →
    /// <c>.IsDragRegion(bool?)</c>), which reflection does not model as assignability.
    /// </summary>
    private static bool AcceptsValueOfType(Type modifierParameterType, Type setterValueType)
        => modifierParameterType == setterValueType
            || Nullable.GetUnderlyingType(modifierParameterType) == setterValueType
            || modifierParameterType.IsAssignableFrom(setterValueType);

    private static string FullName(Type type) => type.FullName ?? type.Name;

    /// <summary>
    /// Names of every <c>public static T Name&lt;T&gt;(this T el, ...)</c> modifier declared
    /// by Reactor's extension classes. Restricted to the classes that actually hold them so
    /// the reflection stays targeted (and trim-analyzable); a modifier added to a third class
    /// fails <see cref="Every_Attached_Entry_Names_A_Generic_Modifier_That_Exists"/> until
    /// that class is listed here.
    /// </summary>
    private static readonly Lazy<HashSet<string>> DeclaredGenericModifiers = new(() =>
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectGenericModifiers(typeof(Microsoft.UI.Reactor.ElementExtensions), names);
        CollectGenericModifiers(typeof(Microsoft.UI.Reactor.FlexExtensions), names);
        return names;
    });

    private static void CollectGenericModifiers(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type,
        HashSet<string> names)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.IsGenericMethodDefinition)
                continue;
            var typeArguments = method.GetGenericArguments();
            var parameters = method.GetParameters();
            // The fluent shape: one type parameter, and the receiver is that parameter.
            if (typeArguments.Length != 1 || parameters.Length == 0)
                continue;
            if (parameters[0].ParameterType != typeArguments[0])
                continue;
            names.Add(method.Name);
        }
    }

    // Type parameter + annotation rather than a generic type argument: the extension classes
    // are static, and a static type cannot be used as a type argument (CS0718).
    private static bool HasStaticTwoArgMethod(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type,
        string name)
        => StaticTwoArgValueType(type, name) is not null;

    /// <summary>
    /// The value (second) parameter type of the owner's <c>public static void Set&lt;X&gt;(target, value)</c>,
    /// or <c>null</c> when no such method exists.
    /// </summary>
    private static Type? StaticTwoArgValueType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type,
        string name)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 2)
                return parameters[1].ParameterType;
        }
        return null;
    }

    private static bool HasDependencyPropertyField(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties)] Type type,
        string propertyName)
    {
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        // Reactor's own attached properties are `public static readonly DependencyProperty`
        // fields; the WinRT projection surfaces WinUI's as static *properties* instead. Accept
        // either — what matters is that the DP identifier CleanElement clears really exists
        // under this exact name.
        var field = type.GetField(propertyName + "Property", Flags);
        if (field is not null)
            return field.FieldType == typeof(Microsoft.UI.Xaml.DependencyProperty);

        var property = type.GetProperty(propertyName + "Property", Flags);
        return property is not null
            && property.PropertyType == typeof(Microsoft.UI.Xaml.DependencyProperty);
    }

    // ── Drift against the runtime authority ──────────────────────────────────

    [Fact]
    public void Every_ControlGate_Matches_The_Types_ApplyModifiers_Writes_To()
    {
        // ControlGate hand-copies allow-lists that Reconciler.ApplyModifiers encodes
        // independently, and the two failure directions are both silent: a gate that is too
        // WIDE makes the analyzer suggest a modifier the reconciler never writes (the rewrite
        // compiles and does nothing — the ValueList/CellComponent regression), while one that
        // is too NARROW just drops diagnostics. Nothing else notices either, so pin the copy
        // to its source.
        var actualGates = ReadApplyModifierControlGates();
        var problems = new List<string>();

        foreach (var (prop, info) in ModifierTable.Properties)
        {
            if (info.ControlGate is not { } declared)
                continue;

            if (!actualGates.TryGetValue(prop, out var actual))
            {
                problems.Add(
                    $"{prop}: ModifierTable declares a control gate [{string.Join("|", declared)}], " +
                    "but ApplyModifiers has no 'fe is <Type>' test guarded by 'm." + prop + "'");
                continue;
            }

            if (!actual.SetEquals(declared))
            {
                problems.Add(
                    $"{prop}: ModifierTable says [{string.Join("|", declared.OrderBy(t => t, StringComparer.Ordinal))}] " +
                    $"but ApplyModifiers writes to [{string.Join("|", actual.OrderBy(t => t, StringComparer.Ordinal))}]");
            }
        }

        Assert.True(
            problems.Count == 0,
            "ModifierTable.ControlGate has drifted from Reconciler.ApplyModifiers, which is the " +
            "runtime authority for which controls a modifier is actually written to:\n  " +
            string.Join("\n  ", problems));
    }

    /// <summary>
    /// The reverse of <see cref="Every_ControlGate_Matches_The_Types_ApplyModifiers_Writes_To"/>:
    /// every control gate that exists in <c>ApplyModifiers</c> must be accounted for here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// That test iterates <see cref="ModifierTable.Properties"/> and skips entries whose
    /// <see cref="ModifierInfo.ControlGate"/> is <see langword="null"/>, so a gate the reconciler
    /// enforces but the table does not name is invisible to it — including a brand-new type-gated
    /// modifier, and the <c>IsEnabled</c> / <c>H|VContentAlignment</c> trio whose gate is
    /// deliberately left null for the <c>.Set</c> direction.
    /// </para>
    /// <para>
    /// That gap matters because <see cref="NoOpModifierAnalyzer"/> (<c>REACTOR_MOD_003</c>) reads the
    /// same table in the opposite direction — "you wrote the modifier, does it reach this control?"
    /// — where a null gate means "never report" rather than "no predicate needed". Requiring every
    /// reconciler gate to be either declared or listed in
    /// <see cref="ModifierTable.GateOnlyInReconciler"/> makes adding one a deliberate decision for
    /// both rules instead of a silent no-op for one of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_ApplyModifiers_ControlGate_Is_Declared_Or_Explicitly_Recorded()
    {
        var actualGates = ReadApplyModifierControlGates();

        // Self-validation: the extraction must actually have found the gates. Without this the
        // test would pass vacuously if ReadApplyModifierControlGates ever stopped matching (a
        // rename of `fe`, a restructure of the guards), whereas the forward test would fail loudly.
        Assert.True(
            actualGates.Count >= 8,
            $"Only {actualGates.Count} control gates were read out of ApplyModifiers; expected at least 8 " +
            "(Padding, CornerRadius, BorderThickness, BorderBrush, Background, Foreground, and the fonts). " +
            "The gate reader has probably stopped matching — fix it rather than lowering this floor.");

        var problems = new List<string>();

        foreach (var (prop, actual) in actualGates.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (ModifierTable.Properties.TryGetValue(prop, out var info) && info.ControlGate is not null)
                continue;   // covered by the forward test above.

            if (ModifierTable.GateOnlyInReconciler.ContainsKey(prop))
                continue;

            problems.Add(
                $"{prop}: ApplyModifiers gates it on [{string.Join("|", actual.OrderBy(t => t, StringComparer.Ordinal))}] " +
                "but ModifierTable neither declares a ControlGate for it nor records it in " +
                "GateOnlyInReconciler. Declare the gate (so REACTOR_MOD_002 withholds its suggestion and " +
                "REACTOR_MOD_003 reports the silent drop), or add a GateOnlyInReconciler entry saying why " +
                "neither rule needs it.");
        }

        Assert.True(
            problems.Count == 0,
            "Reconciler.ApplyModifiers gates modifiers that ModifierTable does not account for:\n  " +
            string.Join("\n  ", problems));
    }

    /// <summary>
    /// Every entry in <see cref="ModifierTable.GateOnlyInReconciler"/> must name a gate that
    /// <c>ApplyModifiers</c> really enforces, and carry a reason — otherwise the exclusion list
    /// accumulates stale rows that quietly suppress the completeness check above.
    /// </summary>
    [Fact]
    public void Every_GateOnlyInReconciler_Entry_Is_Real_And_Explained()
    {
        var actualGates = ReadApplyModifierControlGates();
        var problems = new List<string>();

        foreach (var (prop, reason) in ModifierTable.GateOnlyInReconciler)
        {
            if (!actualGates.ContainsKey(prop))
            {
                problems.Add(
                    $"{prop}: recorded as gated-only-in-the-reconciler, but ApplyModifiers has no " +
                    "'fe is <Type>' test for it. Remove the row.");
            }

            if (string.IsNullOrWhiteSpace(reason))
                problems.Add($"{prop}: no reason recorded.");

            if (ModifierTable.Properties.TryGetValue(prop, out var info) && info.ControlGate is not null)
            {
                problems.Add(
                    $"{prop}: declares a ControlGate AND appears in GateOnlyInReconciler. The declared " +
                    "gate wins, so the row is dead — remove it.");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n  ", problems));
    }

    /// <summary>
    /// <c>Reconciler</c> carries a <b>second</b> copy of the same allow-list:
    /// <c>GetDependencyPropertyName</c> decides which properties a <c>ThemeRef</c> binding may emit
    /// a <c>&lt;Setter&gt;</c> for. Nothing pins it to <c>ApplyModifiers</c>, so
    /// <c>.Background(Theme.X)</c> and <c>.Background("#fff")</c> can silently disagree about which
    /// controls they reach — and both analyzers would be right about only one of them.
    /// </summary>
    [Fact]
    public void GetDependencyPropertyName_Agrees_With_ApplyModifiers_And_The_Table()
    {
        var applyGates = ReadApplyModifierControlGates();
        var themeGates = ReadGetDependencyPropertyNameGates();

        // Self-validation: Background, Foreground, BorderBrush.
        Assert.True(
            themeGates.Count >= 3,
            $"Only {themeGates.Count} gates were read out of GetDependencyPropertyName; expected at least 3. " +
            "The reader has probably stopped matching.");

        var problems = new List<string>();

        foreach (var (prop, themeGate) in themeGates.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!applyGates.TryGetValue(prop, out var applyGate))
            {
                problems.Add(
                    $"{prop}: GetDependencyPropertyName gates the ThemeRef path on " +
                    $"[{string.Join("|", themeGate.OrderBy(t => t, StringComparer.Ordinal))}] but ApplyModifiers " +
                    "has no control gate for it at all.");
                continue;
            }

            if (!themeGate.SetEquals(applyGate))
            {
                problems.Add(
                    $"{prop}: the ThemeRef path reaches [{string.Join("|", themeGate.OrderBy(t => t, StringComparer.Ordinal))}] " +
                    $"but the brush path reaches [{string.Join("|", applyGate.OrderBy(t => t, StringComparer.Ordinal))}]. " +
                    "A modifier that works with a literal brush and not with a Theme token (or vice versa) is a " +
                    "silent, overload-dependent bug.");
            }

            if (ModifierTable.Properties.TryGetValue(prop, out var info)
                && info.ControlGate is { } declared
                && !themeGate.SetEquals(declared))
            {
                problems.Add(
                    $"{prop}: ModifierTable declares [{string.Join("|", declared.OrderBy(t => t, StringComparer.Ordinal))}] " +
                    $"but the ThemeRef path reaches [{string.Join("|", themeGate.OrderBy(t => t, StringComparer.Ordinal))}].");
            }
        }

        Assert.True(
            problems.Count == 0,
            "Reconciler's two applicability copies have drifted:\n  " + string.Join("\n  ", problems));
    }

    /// <summary>
    /// Property name → the WinUI type names <c>Reconciler.GetDependencyPropertyName</c> will emit a
    /// <c>{ThemeResource}</c> setter for, read out of <c>Reconciler.cs</c>. The method's body is a
    /// chain of <c>if (property == "X" &amp;&amp; (fe is A || fe is B)) return "X";</c>, so each
    /// branch's string comparison names the property and the type tests are the gate.
    /// </summary>
    private static Dictionary<string, HashSet<string>> ReadGetDependencyPropertyNameGates()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var file = Path.Join(root!, "src", "Reactor", "Core", "Reconciler.cs");
        Assert.True(File.Exists(file), $"Reconciler.cs not found at {file}");

        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(file));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "GetDependencyPropertyName");

        Assert.NotNull(method);

        var gates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var ifStatement in method!.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax>())
        {
            var property = ifStatement.Condition
                .DescendantNodesAndSelf()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>()
                .Select(literal => literal.Token.ValueText)
                .FirstOrDefault();

            if (property is null)
                continue;

            var typeNames = ifStatement.Condition
                .DescendantNodesAndSelf()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax>()
                .Where(b => b.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.IsExpression
                            && b.Left is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax { Identifier.Text: "fe" })
                .Select(b => SimpleTypeName(b.Right))
                .Where(typeName => typeName is not null);

            foreach (var typeName in typeNames)
            {
                if (!gates.TryGetValue(property, out var set))
                    gates[property] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(typeName!);
            }
        }

        return gates;
    }

    /// <summary>
    /// Unqualified name of a type reference written as either <c>Type</c> or <c>Ns.Type</c>, or
    /// <see langword="null"/> for any other shape (array, generic, tuple, …), which the gates never
    /// use.
    /// </summary>
    private static string? SimpleTypeName(Microsoft.CodeAnalysis.SyntaxNode type) => type switch
    {
        Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax simple => simple.Identifier.Text,
        _ => null,
    };

    /// <summary>
    /// <see cref="NoOpModifierAnalyzer"/> resolves an element's mounted control from Reactor's
    /// public <c>Set(this TElement, Action&lt;TControl&gt;)</c> overload, because the generator
    /// attributes do not flow to consumers (<c>Reactor.Wrappers.Abstractions</c> is referenced with
    /// <c>PrivateAssets="all"</c>). That is only sound while the <c>Set</c> overload names the same
    /// control the descriptor was generated for — so pin the two together for every element that
    /// declares the attribute.
    /// </summary>
    /// <remarks>
    /// Reflection reads metadata only; no WinUI object is constructed, so this is safe in the
    /// headless host. Changing an element's <c>Set</c> signature without changing its descriptor —
    /// or vice versa — fails here rather than silently moving the analyzer's gate.
    /// </remarks>
    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Test-only contract guard: enumerates the Reactor assembly's element types and the ElementExtensions surface by design. This host is never trimmed; behaviour-neutral.")]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "Test-only contract guard: reflects the public static methods of ElementExtensions, resolved by name from the Reactor assembly. Intentional and JIT-only; behaviour-neutral.")]
    public void Every_Element_Set_Overload_Names_The_Control_Its_Descriptor_Mounts()
    {
        var elementType = typeof(Microsoft.UI.Reactor.Core.Element);

        // The Set-overload map and the generator-attribute lookup both live on ReactorSurface, so
        // this fact and the agent-kit doc gate resolve an element's mounted control through exactly
        // one implementation. Two copies is how the pairing they pin would drift apart unnoticed.
        Assert.NotNull(ReactorSurface.Instance.ElementExtensionsType);

        var checkedElements = 0;
        var problems = new List<string>();

        // Projected + filtered in the pipeline (CodeQL cs/linq/missed-where); this also avoids
        // resolving the declared control twice.
        var attributed = elementType.Assembly.GetTypes()
            .Where(t => elementType.IsAssignableFrom(t) && !t.IsAbstract)
            .Select(element => (Element: element, Declared: ReactorSurface.DeclaredControl(element)))
            .Where(pair => pair.Declared is not null)
            .OrderBy(pair => pair.Element.Name, StringComparer.Ordinal);

        foreach (var (element, declared) in attributed)
        {
            var fromSet = ReactorSurface.Instance.SetControls(element);
            if (fromSet.Count == 0)
                continue;   // no Set overload; the analyzer skips these elements entirely.

            checkedElements++;

            if (!fromSet.Contains(declared!))
            {
                problems.Add(
                    $"{element.Name}: the descriptor mounts {declared!.Name}, but its Set overload(s) take " +
                    $"[{string.Join("|", fromSet.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal))}]. " +
                    "REACTOR_MOD_003 reads the mounted control off Set, so it would gate against the wrong type.");
            }
        }

        Assert.True(
            problems.Count == 0,
            "An element's Set overload has drifted from the control its descriptor mounts:\n  " +
            string.Join("\n  ", problems));

        // Self-validation: dozens of elements carry both. A collapse to zero would mean the
        // attribute or Set reflection stopped resolving and the guard is running vacuously.
        Assert.True(
            checkedElements >= 40,
            $"Only {checkedElements} elements were cross-checked; expected 40+. The Set/attribute " +
            "reflection has probably stopped resolving.");
    }

    /// <summary>
    /// A generated descriptor's <c>Customize</c> hook may read a <b>common modifier</b> off the
    /// element and write it to the control itself — <c>RichTextBlockElement</c> does exactly that
    /// for <c>Padding</c>. On such an element <c>ApplyModifiers</c>' control gate is not the
    /// authority: the value is applied even though the gate says it would be dropped, so
    /// <see cref="NoOpModifierAnalyzer"/> must stay silent or it reports a false positive on correct
    /// code. That exception list is hand-maintained, so pin it to the descriptors.
    /// </summary>
    [Fact]
    public void Descriptor_Applied_Common_Modifiers_Match_The_Analyzer_Exception_List()
    {
        var (found, customizeHooks) = ReadDescriptorAppliedCommonModifiers();

        // Self-validation: descriptor Customize hooks are everywhere in Element.cs; a collapse to
        // zero means the reader stopped matching and the comparison below would pass vacuously.
        Assert.True(
            customizeHooks >= 20,
            $"Only {customizeHooks} descriptor Customize hooks were parsed; expected 20+. The reader " +
            "has probably stopped matching.");

        var declared = new HashSet<string>(
            NoOpModifierAnalyzer.DescriptorAppliedModifiers, StringComparer.Ordinal);

        var missing = found.Except(declared, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var stale = declared.Except(found, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0,
            "A descriptor now applies a gated common modifier itself, but NoOpModifierAnalyzer still " +
            "treats ApplyModifiers' gate as the authority for it — REACTOR_MOD_003 would report a false " +
            "positive on correct code. Add to NoOpModifierAnalyzer.DescriptorAppliedModifiers:\n  " +
            string.Join("\n  ", missing));

        Assert.True(
            stale.Length == 0,
            "NoOpModifierAnalyzer.DescriptorAppliedModifiers suppresses a modifier no descriptor applies " +
            "any more, so a real silent drop is going unreported. Remove:\n  " +
            string.Join("\n  ", stale));
    }

    /// <summary>
    /// Scans every generated-descriptor <c>Customize</c> hook in <c>src/Reactor</c> for reads of a
    /// gated common modifier off the element lambda parameter (e.g.
    /// <c>get: static e =&gt; e.Padding…</c>), keyed as <c>Namespace.ElementType|Modifier</c>.
    /// Returns the set plus the number of hooks inspected, for the non-vacuity floor.
    /// </summary>
    private static (HashSet<string> Keys, int CustomizeHooks) ReadDescriptorAppliedCommonModifiers()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var sourceDir = Path.Join(root!, "src", "Reactor");
        Assert.True(Directory.Exists(sourceDir), $"src/Reactor not found at {sourceDir}");

        // Only the modifiers REACTOR_MOD_003 reports on can produce a false positive.
        var gated = new HashSet<string>(
            ModifierTable.Properties.Where(p => p.Value.ControlGate is not null).Select(p => p.Key),
            StringComparer.Ordinal);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var hooks = 0;

        foreach (var file in Directory.GetFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
                     .Select(File.ReadAllText)
                     .Where(text => text.Contains("Customize")))
        {
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(file);
            foreach (var method in tree.GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == "Customize"
                            && m.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax))
            {
                hooks++;

                var elementName = QualifiedTypeName(
                    (Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax)method.Parent!);

                var gatedReads = method.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>()
                    .Where(access =>
                        access.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax receiver
                        && gated.Contains(access.Name.Identifier.Text)
                        && IsLambdaParameter(access, receiver.Identifier.Text));

                foreach (var access in gatedReads)
                    keys.Add(NoOpModifierAnalyzer.ElementModifierKey(elementName, access.Name.Identifier.Text));
            }
        }

        return (keys, hooks);
    }

    /// <summary>Namespace-qualified name of the type declaration owning a member.</summary>
    private static string QualifiedTypeName(Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax type)
    {
        for (Microsoft.CodeAnalysis.SyntaxNode? node = type.Parent; node is not null; node = node.Parent)
        {
            var ns = node switch
            {
                Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax file => file.Name.ToString(),
                Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax block => block.Name.ToString(),
                _ => null,
            };
            if (ns is not null)
                return ns + "." + type.Identifier.Text;
        }

        return type.Identifier.Text;
    }

    /// <summary>
    /// True when <paramref name="name"/> is a parameter of some lambda enclosing
    /// <paramref name="node"/> — i.e. the member access reads the descriptor's element/control
    /// argument rather than an unrelated local of the same name.
    /// </summary>
    private static bool IsLambdaParameter(Microsoft.CodeAnalysis.SyntaxNode node, string name)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax simple
                    when simple.Parameter.Identifier.Text == name:
                    return true;
                case Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedLambdaExpressionSyntax paren
                    when paren.ParameterList.Parameters.Any(p => p.Identifier.Text == name):
                    return true;
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Modifier property name → the WinUI type names <c>ApplyModifiers</c> actually writes it
    /// to, read out of <c>Reconciler.cs</c>.
    /// </summary>
    /// <remarks>
    /// Parsed with Roslyn rather than matched with a regex: the gate lives in a type-test
    /// pattern nested inside the <c>if (m.PROP…)</c> that guards it, and tying the two
    /// together textually would be guesswork about brace depth. Walking the syntax tree makes
    /// the containment relationship exact, so this test fails on real drift instead of on
    /// reformatting.
    /// </remarks>
    private static Dictionary<string, HashSet<string>> ReadApplyModifierControlGates()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var file = Path.Join(root!, "src", "Reactor", "Core", "Reconciler.cs");
        Assert.True(File.Exists(file), $"Reconciler.cs not found at {file}");

        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(file));
        var methods = tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Where(m => m.Identifier.Text == "ApplyModifiers")
            .ToList();

        Assert.True(methods.Count > 0, "No ApplyModifiers method found in Reconciler.cs");

        var gates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var method in methods)
        {
            // Padding and BorderThickness are computed into a local first (to overlay the
            // BiDi-aware inline variants), so the guard reads `resolvedPadding`, not
            // `m.Padding`. Map those locals back to the modifier they were seeded from.
            var localToProperty = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var declarator in method.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
                .Where(declarator => declarator.Initializer is not null))
            {
                var seed = ModifierPropertyNames(declarator.Initializer!.Value).FirstOrDefault();
                if (seed is not null)
                    localToProperty[declarator.Identifier.Text] = seed;
            }

            foreach (var ifStatement in method.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax>())
            {
                // Which modifier(s) does this branch guard? `m.Background is not null`,
                // `resolvedPadding.HasValue`, `oldM?.FontSize.HasValue == true`, … A guard can name
                // more than one — `m.PaddingInlineStart.HasValue || m.PaddingInlineEnd.HasValue`
                // gates BOTH on the same control set — so every name is attributed, not just the
                // first. Taking only the first left PaddingInlineEnd invisible to
                // Every_ApplyModifiers_ControlGate_Is_Declared_Or_Explicitly_Recorded, which is
                // precisely the bookkeeping hole that test exists to close.
                var guarded = ModifierPropertyNames(ifStatement.Condition)
                    .Concat(ifStatement.Condition
                        .DescendantNodesAndSelf()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>()
                        .Select(id => localToProperty.TryGetValue(id.Identifier.Text, out var mapped) ? mapped : null)
                        .Where(name => name is not null)!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (guarded.Length == 0)
                    continue;

                // Type patterns on the FrameworkElement inside this branch (and its else clauses)
                // are the gate: `fe is WinUI.Control padCtrl` or
                // `fe switch { WinUI.Control padCtrl => ... }`.
                var isPatternTypeNames = ifStatement.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IsPatternExpressionSyntax>()
                    .Where(p => p.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax { Identifier.Text: "fe" }
                                && p.Pattern is Microsoft.CodeAnalysis.CSharp.Syntax.DeclarationPatternSyntax)
                    .Select(p => SimpleTypeName(((Microsoft.CodeAnalysis.CSharp.Syntax.DeclarationPatternSyntax)p.Pattern).Type))
                    .Where(typeName => typeName is not null);

                var switchPatternTypeNames = ifStatement.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.SwitchExpressionSyntax>()
                    .Where(s => s.GoverningExpression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax { Identifier.Text: "fe" })
                    .SelectMany(s => s.Arms)
                    .Select(arm => arm.Pattern)
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.DeclarationPatternSyntax>()
                    .Select(pattern => SimpleTypeName(pattern.Type))
                    .Where(typeName => typeName is not null);

                var typeNames = isPatternTypeNames.Concat(switchPatternTypeNames);

                foreach (var typeName in typeNames)
                {
                    foreach (var modifier in guarded)
                    {
                        if (!gates.TryGetValue(modifier!, out var set))
                            gates[modifier!] = set = new HashSet<string>(StringComparer.Ordinal);
                        set.Add(typeName!);
                    }
                }
            }
        }

        return gates;
    }

    /// <summary>
    /// Modifier property names read off the new or old modifier bag inside
    /// <paramref name="node"/>, in source order — both <c>m.Foo</c> / <c>oldM.Foo</c> and the
    /// conditional <c>oldM?.Foo</c> form.
    /// </summary>
    private static IEnumerable<string> ModifierPropertyNames(Microsoft.CodeAnalysis.SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            switch (descendant)
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax access
                    when access.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax bag
                        && (bag.Identifier.Text == "m" || bag.Identifier.Text == "oldM"):
                    yield return access.Name.Identifier.Text;
                    break;

                // `oldM?.Padding` — the name hangs off a member binding, and the receiver is
                // on the enclosing conditional access.
                case Microsoft.CodeAnalysis.CSharp.Syntax.ConditionalAccessExpressionSyntax conditional
                    when conditional.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax bag
                        && (bag.Identifier.Text == "m" || bag.Identifier.Text == "oldM"):
                {
                    var binding = conditional.WhenNotNull
                        .DescendantNodesAndSelf()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberBindingExpressionSyntax>()
                        .FirstOrDefault();
                    if (binding is not null)
                        yield return binding.Name.Identifier.Text;
                    break;
                }
            }
        }
    }

    // ── Source-scanning helpers ──────────────────────────────────────────────
    //
    // Source scanning rather than reflection over ElementExtensions, to match the approach
    // already proven in PoolResetSetConsistencyTests and to distinguish the generic
    // `T Foo<T>(this T el, ...)` shape from the type-specific overloads — a distinction
    // reflection over extension methods makes awkward.

    private static HashSet<string> ReadGenericModifierNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in ReadElementExtensionSources())
        {
            foreach (Match m in Regex.Matches(
                source, @"public\s+static\s+T\s+(\w+)\s*<T>\s*\(\s*this\s+T\s+\w+"))
            {
                names.Add(m.Groups[1].Value);
            }
        }
        return names;
    }

    /// <summary>
    /// Modifier method name → the element types declaring a type-specific overload, i.e.
    /// <c>public static XxxElement Foo(this XxxElement el, ...)</c>.
    /// </summary>
    private static Dictionary<string, HashSet<string>> ReadTypeSpecificModifiers()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var source in ReadElementExtensionSources())
        {
            foreach (Match m in Regex.Matches(
                source, @"public\s+static\s+(\w+)\s+(\w+)\s*\(\s*this\s+(\w+)\s+\w+"))
            {
                var returnType = m.Groups[1].Value;
                var method = m.Groups[2].Value;
                var receiver = m.Groups[3].Value;

                // A fluent modifier returns its receiver type. This also filters out the
                // generic form, whose receiver is the type parameter `T`.
                if (receiver == "T" || returnType != receiver)
                    continue;

                if (!map.TryGetValue(method, out var set))
                    map[method] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(receiver);
            }
        }
        return map;
    }

    private static IEnumerable<string> ReadElementExtensionSources()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var dir = Path.Join(root!, "src", "Reactor", "Elements");
        Assert.True(Directory.Exists(dir), $"Elements directory not found at {dir}");

        var files = Directory.GetFiles(dir, "ElementExtensions*.cs");
        Assert.True(files.Length > 0, $"No ElementExtensions*.cs found in {dir}");

        foreach (var file in files)
            yield return File.ReadAllText(file);
    }
}
