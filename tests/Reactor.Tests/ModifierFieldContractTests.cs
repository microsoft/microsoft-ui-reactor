using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 067 §10.1, layer 0: every modifier field is declared once below, with the way the
/// shallow-skip gate compares it, and the hand-written <c>Merge</c> and skip-equality code is
/// checked against that declaration for every field. A new field with no declaration fails
/// <see cref="Every_Modifier_Field_Is_Declared_Exactly_Once"/>, so it can't ship half-wired
/// (the class of bug behind spec 059's <c>IsDragRegion</c>, #976's <c>OnUpdateAction</c> and
/// spec 067 §14's <c>XYFocusKeyboardNavigation</c>).
/// </summary>
/// <remarks>
/// The table is today's behaviour, not a schema. Layer 1 moves these facts into
/// <c>ModifierSchema</c> and this file reads them from there. Brush and FontFamily fields can't
/// be constructed headless, so their merge and equality checks run in the
/// <c>ModifierFieldContract_BrushAndFont</c> selftest.
/// </remarks>
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Test-only: reflects the modifier records' properties and the sample types' constructors / Invoke. JIT-only host, never trimmed.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Test-only: constructs an empty modifier bucket record by type. JIT-only host, never trimmed.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only: reflects the modifier records' properties. JIT-only host, never trimmed.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: Enum.GetValues(Type) and Expression.Compile for sample values. JIT-only host, never AOT-compiled.")]
public class ModifierFieldContractTests
{
    /// <summary>How the shallow-skip gate (<c>ModifiersEqualCore</c>) compares a field. Spec 067 §4.2.</summary>
    internal enum Skip
    {
        /// <summary>Compared by value (<c>==</c> or the bucket record's <c>Equals</c>).</summary>
        Value,
        /// <summary><c>BrushesEqual</c>: colour and opacity. Selftest only.</summary>
        BrushValue,
        /// <summary><c>FontFamiliesEqual</c>: <c>Source</c>. Selftest only.</summary>
        FontFamilyValue,
        /// <summary><c>ReferenceEquals</c>.</summary>
        Reference,
        /// <summary>Full mode: equal only when both sides are null. Visible mode: presence only.</summary>
        BlocksSkipWhenSet,
        /// <summary>Never compared. Requires a reason.</summary>
        NotCompared,
    }

    internal enum Bucket { Root, Layout, Visual, Accessibility }

    /// <summary>One modifier field and how the skip gate compares it.</summary>
    /// <param name="Bucket">The record that declares the field.</param>
    /// <param name="Name">The property name on that record.</param>
    /// <param name="Skip">How <c>ModifiersEqualCore</c> compares it.</param>
    /// <param name="Callback">A callback slot, ignored by <c>ModifiersVisiblyEqual</c>.</param>
    /// <param name="Reason">Why a <see cref="Skip.NotCompared"/> field is not compared. Required for that kind only.</param>
    internal sealed record Field(Bucket Bucket, string Name, Skip Skip, bool Callback = false, string? Reason = null);

    private const string GestureReason =
        "Dispatched through per-element gesture / drag state that the skip path refreshes without "
        + "re-subscribing (GestureDispatch.RefreshGestureDragStateOnSkip, #721). Comparing it would "
        + "re-arm an in-flight gesture (see ModifierCallbacksEqual).";

    internal static readonly Field[] Fields =
    [
        // ── Root ──────────────────────────────────────────────────────
        new(Bucket.Root, "ToolTip", Skip.Value),
        new(Bucket.Root, "RichToolTip", Skip.BlocksSkipWhenSet),
        new(Bucket.Root, "ToolTipPlacement", Skip.Value),
        new(Bucket.Root, "ToolTipPlacementTargetRef", Skip.Reference),
        new(Bucket.Root, "AttachedFlyout", Skip.BlocksSkipWhenSet),
        new(Bucket.Root, "ContextFlyout", Skip.BlocksSkipWhenSet),
        new(Bucket.Root, "IsEnabled", Skip.Value),
        new(Bucket.Root, "AutomationName", Skip.Value),
        new(Bucket.Root, "AutomationId", Skip.Value),
        new(Bucket.Root, "ElementSoundMode", Skip.Value),
        new(Bucket.Root, "OnMountAction", Skip.NotCompared,
            Reason: "Fires once at mount (oldM is null), so a later change is inert."),
        new(Bucket.Root, "OnUnmountAction", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnUpdateAction", Skip.Reference, Callback: true),
        new(Bucket.Root, "FontFamily", Skip.FontFamilyValue),
        new(Bucket.Root, "FontSize", Skip.Value),
        new(Bucket.Root, "FontWeight", Skip.Value),
        new(Bucket.Root, "OnSizeChanged", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPointerPressed", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPointerMoved", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPointerReleased", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPointerEntered", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPointerExited", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPointerCanceled", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPointerCaptureLost", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPointerWheelChanged", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnTapped", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnDoubleTapped", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnRightTapped", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnHolding", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnKeyDown", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnKeyUp", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPreviewKeyDown", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnPreviewKeyUp", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnCharacterReceived", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnGotFocus", Skip.Reference, Callback: true),
        new(Bucket.Root, "OnLostFocus", Skip.Reference, Callback: true),
        new(Bucket.Root, "Pan", Skip.NotCompared, Reason: GestureReason),
        new(Bucket.Root, "Pinch", Skip.NotCompared, Reason: GestureReason),
        new(Bucket.Root, "Rotate", Skip.NotCompared, Reason: GestureReason),
        new(Bucket.Root, "LongPress", Skip.NotCompared, Reason: GestureReason),
        new(Bucket.Root, "DragSource", Skip.NotCompared, Reason: GestureReason),
        new(Bucket.Root, "DropTarget", Skip.NotCompared, Reason: GestureReason),
        new(Bucket.Root, "IsDragRegion", Skip.Value),
        new(Bucket.Root, "HeadingLevel", Skip.Value),
        new(Bucket.Root, "IsTabStop", Skip.Value),
        new(Bucket.Root, "IsHitTestVisible", Skip.Value),
        new(Bucket.Root, "TabIndex", Skip.Value),
        new(Bucket.Root, "AccessKey", Skip.Value),
        new(Bucket.Root, "XYFocusKeyboardNavigation", Skip.Value),
        new(Bucket.Root, "XYFocusUpRef", Skip.Reference),
        new(Bucket.Root, "XYFocusDownRef", Skip.Reference),
        new(Bucket.Root, "XYFocusLeftRef", Skip.Reference),
        new(Bucket.Root, "XYFocusRightRef", Skip.Reference),
        new(Bucket.Root, "OnAccessKeyDisplayRequested", Skip.Reference, Callback: true),
        new(Bucket.Root, "Ref", Skip.Reference, Callback: true),
        new(Bucket.Root, "Backdrop", Skip.NotCompared,
            Reason: "Not applied by ApplyModifiers. The host reads the root tree's Backdrop on every render (ReactorHost, ReactorHostControl)."),

        // ── Layout (record value-equality) ────────────────────────────
        new(Bucket.Layout, "Margin", Skip.Value),
        new(Bucket.Layout, "Padding", Skip.Value),
        new(Bucket.Layout, "Width", Skip.Value),
        new(Bucket.Layout, "Height", Skip.Value),
        new(Bucket.Layout, "MinWidth", Skip.Value),
        new(Bucket.Layout, "MinHeight", Skip.Value),
        new(Bucket.Layout, "MaxWidth", Skip.Value),
        new(Bucket.Layout, "MaxHeight", Skip.Value),
        new(Bucket.Layout, "HorizontalAlignment", Skip.Value),
        new(Bucket.Layout, "VerticalAlignment", Skip.Value),
        new(Bucket.Layout, "HorizontalContentAlignment", Skip.Value),
        new(Bucket.Layout, "VerticalContentAlignment", Skip.Value),
        new(Bucket.Layout, "IsVisible", Skip.Value),
        new(Bucket.Layout, "MarginInlineStart", Skip.Value),
        new(Bucket.Layout, "MarginInlineEnd", Skip.Value),
        new(Bucket.Layout, "PaddingInlineStart", Skip.Value),
        new(Bucket.Layout, "PaddingInlineEnd", Skip.Value),
        new(Bucket.Layout, "BorderInlineStart", Skip.Value),
        new(Bucket.Layout, "RequestedTheme", Skip.Value),

        // ── Visual (field by field) ───────────────────────────────────
        new(Bucket.Visual, "Background", Skip.BrushValue),
        new(Bucket.Visual, "Foreground", Skip.BrushValue),
        new(Bucket.Visual, "BorderBrush", Skip.BrushValue),
        new(Bucket.Visual, "BorderThickness", Skip.Value),
        new(Bucket.Visual, "CornerRadius", Skip.Value),
        new(Bucket.Visual, "Opacity", Skip.Value),
        new(Bucket.Visual, "Scale", Skip.Value),
        new(Bucket.Visual, "Rotation", Skip.Value),
        new(Bucket.Visual, "Translation", Skip.Value),
        new(Bucket.Visual, "CenterPoint", Skip.Value),
        new(Bucket.Visual, "TransformOrigin", Skip.Value),

        // ── Accessibility (record value-equality, so reference-typed fields compare by reference) ──
        new(Bucket.Accessibility, "HelpText", Skip.Value),
        new(Bucket.Accessibility, "FullDescription", Skip.Value),
        new(Bucket.Accessibility, "LandmarkType", Skip.Value),
        new(Bucket.Accessibility, "AccessibilityView", Skip.Value),
        new(Bucket.Accessibility, "IsRequiredForForm", Skip.Value),
        new(Bucket.Accessibility, "LiveSetting", Skip.Value),
        new(Bucket.Accessibility, "PositionInSet", Skip.Value),
        new(Bucket.Accessibility, "SizeOfSet", Skip.Value),
        new(Bucket.Accessibility, "Level", Skip.Value),
        new(Bucket.Accessibility, "ItemStatus", Skip.Value),
        new(Bucket.Accessibility, "LabeledBy", Skip.Value),
        new(Bucket.Accessibility, "LabeledByRef", Skip.Reference),
        new(Bucket.Accessibility, "DescribedByRefs", Skip.Reference),
        new(Bucket.Accessibility, "FlowsToRefs", Skip.Reference),
        new(Bucket.Accessibility, "FlowsFromRefs", Skip.Reference),
        new(Bucket.Accessibility, "TabFocusNavigation", Skip.Value),
    ];

    private static readonly string[] Containers = ["Layout", "Visual", "Accessibility"];

    private static Type BucketType(Bucket bucket) => bucket switch
    {
        Bucket.Root => typeof(ElementModifiers),
        Bucket.Layout => typeof(LayoutModifiers),
        Bucket.Visual => typeof(VisualModifiers),
        Bucket.Accessibility => typeof(AccessibilityModifiers),
        _ => throw new ArgumentOutOfRangeException(nameof(bucket)),
    };

    // Public and internal instance properties (so internal OnUpdateAction counts), without the
    // compiler's EqualityContract.
    private static IEnumerable<PropertyInfo> DataProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.Name != "EqualityContract" && p.GetIndexParameters().Length == 0);

    private static bool IsHeadless(Field f) => f.Skip is not (Skip.BrushValue or Skip.FontFamilyValue);

    // ════════════════════════════════════════════════════════════════
    //  Completeness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Every_Modifier_Field_Is_Declared_Exactly_Once()
    {
        var duplicates = Fields.GroupBy(f => (f.Bucket, f.Name)).Where(g => g.Count() > 1).Select(g => $"{g.Key.Bucket}.{g.Key.Name}");
        Assert.Empty(duplicates);

        var shimmed = DataProperties(typeof(LayoutModifiers)).Concat(DataProperties(typeof(VisualModifiers)))
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var bucket in Enum.GetValues<Bucket>())
        {
            var actual = DataProperties(BucketType(bucket))
                .Select(p => p.Name)
                .Where(name => bucket != Bucket.Root || (!Containers.Contains(name) && !shimmed.Contains(name)))
                .ToHashSet(StringComparer.Ordinal);
            var declared = Fields.Where(f => f.Bucket == bucket).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

            Assert.True(actual.SetEquals(declared),
                $"{bucket}: undeclared [{string.Join(", ", actual.Except(declared))}], "
                + $"declared but missing [{string.Join(", ", declared.Except(actual))}]. "
                + $"Declare a new field in {nameof(ModifierFieldContractTests)}.{nameof(Fields)} with its {nameof(Skip)} kind "
                + "(and a Reason if NotCompared), wire it into Merge and ModifiersEqualCore, and update "
                + "ModifierApplyOrderTests' frozen order. A Brush or FontFamily field also needs a CheckField line "
                + "in ModifierFieldContractFixture and an entry in ModifierFieldContractSelfTestFields (Reactor.AppTests.Host).");
        }

        // The container slots themselves must still exist, or the exclusion above hides a rename.
        foreach (var container in Containers)
            Assert.NotNull(typeof(ElementModifiers).GetProperty(container));
    }

    [Fact]
    public void Every_Layout_And_Visual_Field_Has_A_Root_Shim_Of_The_Same_Type()
    {
        foreach (var f in Fields.Where(f => f.Bucket is Bucket.Layout or Bucket.Visual))
        {
            var bucketProperty = BucketType(f.Bucket).GetProperty(f.Name)!;
            var shim = typeof(ElementModifiers).GetProperty(f.Name, BindingFlags.Instance | BindingFlags.Public);
            Assert.True(shim is not null, $"{f.Bucket}.{f.Name} has no public root shim on ElementModifiers.");
            Assert.Equal(bucketProperty.PropertyType, shim!.PropertyType);
        }
    }

    [Fact]
    public void Not_Compared_Fields_Record_A_Reason()
    {
        foreach (var f in Fields)
        {
            if (f.Skip == Skip.NotCompared)
                Assert.False(string.IsNullOrWhiteSpace(f.Reason), $"{f.Name} is not compared by the skip gate but records no reason.");
            else
                Assert.Null(f.Reason);
        }
    }

    [Fact]
    public void The_Selftest_Covers_Exactly_The_Fields_That_Cannot_Run_Headless()
    {
        // The two tiers together cover every field only if this set matches what the
        // ModifierFieldContract_BrushAndFont fixture checks (it asserts the same list on its side).
        var nonHeadless = Fields.Where(f => !IsHeadless(f)).Select(f => f.Name).Order(StringComparer.Ordinal);
        Assert.True(
            nonHeadless.SequenceEqual(ModifierFieldContractSelfTestFields.Names.Order(StringComparer.Ordinal)),
            $"Brush / FontFamily fields [{string.Join(", ", nonHeadless)}] must match ModifierFieldContractSelfTestFields.Names "
            + $"[{string.Join(", ", ModifierFieldContractSelfTestFields.Names)}]. Add the field there and a CheckField line in ModifierFieldContractFixture.");
    }

    // ════════════════════════════════════════════════════════════════
    //  Merge round trip
    // ════════════════════════════════════════════════════════════════

    public static TheoryData<string> HeadlessFields()
    {
        var data = new TheoryData<string>();
        foreach (var f in Fields.Where(IsHeadless)) data.Add($"{f.Bucket}.{f.Name}");
        return data;
    }

    [Theory]
    [MemberData(nameof(HeadlessFields))]
    public void Merge_Carries_The_Other_Value_When_Set_And_Keeps_Ours_When_Unset(string id)
    {
        var f = Find(id);
        var (v1, v2, _) = Samples(Property(f).PropertyType);

        var ours = With(f, v1);

        // other sets it: other wins.
        AssertCarries(f, v2, ours.Merge(With(f, v2)));
        // other leaves it unset, but carries the same bucket so the bucket's own Merge runs.
        AssertCarries(f, v1, ours.Merge(EmptyBucket(f.Bucket)));
        // other has no modifiers in this bucket at all.
        AssertCarries(f, v1, ours.Merge(new ElementModifiers()));
        // ours is unset: other's value comes through.
        AssertCarries(f, v2, EmptyBucket(f.Bucket).Merge(With(f, v2)));
    }

    private static void AssertCarries(Field f, object expected, ElementModifiers merged)
    {
        var actual = Read(f, merged);
        if (expected.GetType().IsValueType || expected is string)
            Assert.Equal(expected, actual);
        else
            Assert.Same(expected, actual);
    }

    // ════════════════════════════════════════════════════════════════
    //  Skip equality, both directions
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Four pairs per field, under both <c>ModifiersEqual</c> and <c>ModifiersVisiblyEqual</c>:
    /// unset vs set, set vs a different value, set vs the same instance, set vs a fresh but
    /// value-equal instance. The "equal" rows matter as much as the "unequal" ones: a comparer
    /// that always says "changed" passes an unequal-only test while collapsing the skip rate.
    /// </summary>
    [Theory]
    [MemberData(nameof(HeadlessFields))]
    public void Skip_Gate_Compares_The_Field_As_Declared(string id)
    {
        var f = Find(id);
        var (v1, v2, fresh) = Samples(Property(f).PropertyType);

        var set = With(f, v1);
        (string Pair, ElementModifiers? A, ElementModifiers B)[] pairs =
        [
            ("null vs set", null, set),
            ("unset vs set", new ElementModifiers(), set),
            ("different", set, With(f, v2)),
            ("same instance", set, With(f, v1)),
            ("fresh equal", set, With(f, fresh())),
        ];

        // Expected equality per pair, full mode then visible mode. A null bag never equals a set
        // one in full mode, whatever the kind: ModifiersEqualCore returns false before reading any
        // field, which is what lets a gesture or OnMount added to a bare element take the update.
        // Visible mode reads null as an empty bag, so it matches the "unset" row.
        var (full, visible) = f.Skip switch
        {
            Skip.Value => (new[] { false, false, false, true, true }, new[] { false, false, false, true, true }),
            Skip.Reference when f.Callback => (new[] { false, false, false, true, false }, new[] { true, true, true, true, true }),
            Skip.Reference => (new[] { false, false, false, true, false }, new[] { false, false, false, true, false }),
            Skip.BlocksSkipWhenSet => (new[] { false, false, false, false, false }, new[] { false, false, true, true, true }),
            Skip.NotCompared => (new[] { false, true, true, true, true }, new[] { true, true, true, true, true }),
            _ => throw new InvalidOperationException($"{f.Skip} is not headless."),
        };

        for (var i = 0; i < pairs.Length; i++)
        {
            var (pair, a, b) = pairs[i];
            Assert.True(full[i] == Element.ModifiersEqual(a, b), $"{id} ({f.Skip}) {pair}: ModifiersEqual(a, b) should be {full[i]}.");
            Assert.True(full[i] == Element.ModifiersEqual(b, a), $"{id} ({f.Skip}) {pair}: ModifiersEqual(b, a) should be {full[i]}.");
            Assert.True(visible[i] == Element.ModifiersVisiblyEqual(a, b), $"{id} ({f.Skip}) {pair}: ModifiersVisiblyEqual(a, b) should be {visible[i]}.");
            Assert.True(visible[i] == Element.ModifiersVisiblyEqual(b, a), $"{id} ({f.Skip}) {pair}: ModifiersVisiblyEqual(b, a) should be {visible[i]}.");
        }
    }

    [Fact]
    public void The_Sample_Values_Can_Tell_The_Pairs_Apart()
    {
        // Positive control for the pair matrix: v1 and v2 differ, and a fresh v1 is equal to v1
        // but, for reference types, a different instance. Otherwise a pair row would be a tautology.
        foreach (var f in Fields.Where(IsHeadless))
        {
            var (v1, v2, fresh) = Samples(Property(f).PropertyType);
            var again = fresh();
            Assert.False(Equals(v1, v2) || ReferenceEquals(v1, v2), $"{f.Name}: v1 and v2 must differ. Fix its case in {nameof(Samples)}.");
            if (!v1.GetType().IsValueType)
                Assert.False(ReferenceEquals(v1, again), $"{f.Name}: fresh() must return a new instance.");
            if (v1 is string || v1.GetType().IsValueType)
                Assert.Equal(v1, again);
            // A cloned delegate is Delegate.Equals to the original, so a skip gate that compared
            // callbacks with Equals instead of ReferenceEquals would fail the "fresh equal" row.
            if (v1 is Delegate)
                Assert.True(Equals(v1, again), $"{f.Name}: fresh() must return a value-equal delegate.");
        }
    }

    private static IEnumerable<Field> FloatingPoint() =>
        Fields.Where(f => Nullable.GetUnderlyingType(Property(f).PropertyType) is { } t && (t == typeof(double) || t == typeof(float)));

    public static TheoryData<string> FloatingPointFields()
    {
        var data = new TheoryData<string>();
        foreach (var f in FloatingPoint()) data.Add($"{f.Bucket}.{f.Name}");
        return data;
    }

    /// <summary>
    /// NaN is where the buckets' value comparisons differ (spec 067 §4.2): Layout and
    /// Accessibility compare through record <c>Equals</c> (NaN equals NaN), Visual and root fields
    /// through <c>==</c> (NaN never equals NaN). <c>Width(double.NaN)</c> is WinUI's "Auto", so a
    /// Layout comparison that switched to <c>==</c> would stop those elements ever skipping.
    /// </summary>
    [Theory]
    [MemberData(nameof(FloatingPointFields))]
    public void Skip_Gate_NaN_Equality_Matches_The_Bucket(string id)
    {
        var f = Find(id);
        object nan = Nullable.GetUnderlyingType(Property(f).PropertyType) == typeof(float) ? (object)float.NaN : double.NaN;
        var expected = f.Bucket is Bucket.Layout or Bucket.Accessibility;

        var a = With(f, nan);
        var b = With(f, nan);
        Assert.True(expected == Element.ModifiersEqual(a, b), $"{id}: ModifiersEqual(NaN, NaN) should be {expected}.");
        Assert.True(expected == Element.ModifiersVisiblyEqual(a, b), $"{id}: ModifiersVisiblyEqual(NaN, NaN) should be {expected}.");
    }

    [Fact]
    public void Floating_Point_Fields_Cover_Both_NaN_Behaviours()
    {
        // Positive control for the NaN theory: both outcomes are exercised.
        var buckets = FloatingPoint().Select(f => f.Bucket).ToHashSet();
        Assert.Contains(Bucket.Layout, buckets);
        Assert.Contains(Bucket.Visual, buckets);
        Assert.Contains(Bucket.Root, buckets);
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private static Field Find(string id) => Fields.Single(f => $"{f.Bucket}.{f.Name}" == id);

    private static PropertyInfo Property(Field f) =>
        BucketType(f.Bucket).GetProperty(f.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static ElementModifiers EmptyBucket(Bucket bucket) => bucket switch
    {
        Bucket.Layout => new ElementModifiers { Layout = new LayoutModifiers() },
        Bucket.Visual => new ElementModifiers { Visual = new VisualModifiers() },
        Bucket.Accessibility => new ElementModifiers { Accessibility = new AccessibilityModifiers() },
        _ => new ElementModifiers(),
    };

    // Writes through the record's init accessor. Layout / Visual fields go into their bucket
    // directly, so the test doesn't depend on the root shims.
    private static ElementModifiers With(Field f, object value)
    {
        if (f.Bucket == Bucket.Root)
        {
            var m = new ElementModifiers();
            Property(f).SetValue(m, value);
            return m;
        }

        var bucket = Activator.CreateInstance(BucketType(f.Bucket))!;
        Property(f).SetValue(bucket, value);
        return f.Bucket switch
        {
            Bucket.Layout => new ElementModifiers { Layout = (LayoutModifiers)bucket },
            Bucket.Visual => new ElementModifiers { Visual = (VisualModifiers)bucket },
            _ => new ElementModifiers { Accessibility = (AccessibilityModifiers)bucket },
        };
    }

    private static object? Read(Field f, ElementModifiers m)
    {
        object? holder = f.Bucket switch
        {
            Bucket.Root => m,
            Bucket.Layout => m.Layout,
            Bucket.Visual => m.Visual,
            _ => m.Accessibility,
        };
        return holder is null ? null : Property(f).GetValue(holder);
    }

    /// <summary>
    /// Two distinct values of <paramref name="type"/>, and a factory for a fresh value equal to
    /// the first (a new instance for reference types). Throws for a type it doesn't know, so a
    /// new field type fails loudly here rather than being skipped.
    /// </summary>
    /// <remarks>
    /// For value types the first sample is the type's default (<c>false</c>, <c>0</c>,
    /// <c>Thickness(0)</c>, …), so the unset rows compare "not set" against a set default. A
    /// comparer that collapsed the nullable (<c>GetValueOrDefault()</c>) would equate the two and
    /// fail those rows; with a non-default first sample it would pass every row.
    /// </remarks>
    private static (object V1, object V2, Func<object> Fresh) Samples(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
        {
            var zero = Enum.ToObject(type, 0);
            var other = Enum.GetValues(type).Cast<object>().First(v => !v.Equals(zero));
            return (zero, other, () => zero);
        }
        if (type == typeof(bool)) return (false, true, () => false);
        if (type == typeof(int)) return (0, 2, () => 0);
        if (type == typeof(double)) return (0.0, 2.5, () => 0.0);
        if (type == typeof(float)) return (0f, 2.5f, () => 0f);
        if (type == typeof(string)) return ("a", "b", () => new string('a', 1));
        if (type == typeof(Thickness)) return (new Thickness(0), new Thickness(2), () => new Thickness(0));
        if (type == typeof(CornerRadius)) return (new CornerRadius(0), new CornerRadius(2), () => new CornerRadius(0));
        if (type == typeof(global::System.Numerics.Vector3))
            return (global::System.Numerics.Vector3.Zero, new global::System.Numerics.Vector3(2), () => global::System.Numerics.Vector3.Zero);
        if (type == typeof(global::System.Numerics.Vector2))
            return (global::System.Numerics.Vector2.Zero, new global::System.Numerics.Vector2(2), () => global::System.Numerics.Vector2.Zero);
        if (type == typeof(global::Windows.UI.Text.FontWeight))
            return (default(global::Windows.UI.Text.FontWeight), new global::Windows.UI.Text.FontWeight(700), () => default(global::Windows.UI.Text.FontWeight));
        if (type == typeof(Element)) return (TextBlock("a"), TextBlock("b"), () => TextBlock("a"));
        if (type == typeof(ElementRef)) return (new ElementRef(), new ElementRef(), () => new ElementRef());
        if (type == typeof(IReadOnlyList<ElementRef>))
        {
            var r = new ElementRef();
            return (new[] { r }, new[] { new ElementRef() }, () => new[] { r });
        }
        if (type == typeof(BackdropChoice))
            return (BackdropChoice.Of(BackdropKind.Mica), BackdropChoice.Of(BackdropKind.DesktopAcrylic), () => BackdropChoice.Of(BackdropKind.Mica));
        if (typeof(Delegate).IsAssignableFrom(type))
        {
            var d1 = NewDelegate(type);
            return (d1, NewDelegate(type), () => (Delegate)((ICloneable)d1).Clone());
        }
        if (type == typeof(DropTargetConfig))
        {
            // No constructor arguments, so give each instance its own handler to make v1 != v2.
            DropTargetConfig Make() => new() { OnDrop = (Action<DragTargetArgs>)NewDelegate(typeof(Action<DragTargetArgs>)) };
            return (Make(), Make(), Make);
        }
        if (typeof(DependencyObject).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"{type} can't be constructed headless. Declare the field {nameof(Skip.BrushValue)} / {nameof(Skip.FontFamilyValue)} "
                + "(or add a new non-headless kind) and cover it in ModifierFieldContractFixture.");
        if (type.GetConstructors().OrderBy(c => c.GetParameters().Length).FirstOrDefault() is { } ctor && !type.IsValueType)
        {
            // Configuration records (gesture, drag-drop): construct from sample arguments.
            object Make() => ctor.Invoke(ctor.GetParameters().Select(p => Samples(p.ParameterType).V1).ToArray());
            var (a, b) = (Make(), Make());
            if (Equals(a, b))
                throw new InvalidOperationException(
                    $"Samples for {type} are value-equal. Add a case to {nameof(Samples)} that makes v1 != v2 (see {nameof(DropTargetConfig)}).");
            return (a, b, Make);
        }

        throw new InvalidOperationException($"No sample values for {type}. Add them to {nameof(Samples)}.");
    }

    // A distinct, behaviour-equal delegate instance on every call.
    private static Delegate NewDelegate(Type delegateType)
    {
        var invoke = delegateType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        return Expression.Lambda(delegateType, Expression.Default(invoke.ReturnType), parameters).Compile();
    }
}
