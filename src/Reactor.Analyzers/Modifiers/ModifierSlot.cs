using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.UI.Reactor.Analyzers.Modifiers;

// Spec 067 §4.2: one declaration per common modifier. Plain data (strings, enums, arrays), so it
// compiles in netstandard2.0 and is constructible headless. It lives in the analyzer, the strictest
// consumer, and nothing links it elsewhere: Reactor.Tests reads it through the analyzer's
// InternalsVisibleTo. Type and DP names are strings; reading a real DependencyProperty would
// activate WinUI.

/// <summary>The record a modifier field lives on (spec 034 buckets).</summary>
internal enum ModifierBucket { Root, Layout, Visual, Accessibility }

/// <summary>
/// A grouping label for readers and migration PRs. Not an apply block: apply order is the
/// <see cref="ModifierSchema.Slots"/> array order (spec 067 §5).
/// </summary>
internal enum ModifierFamily
{
    Theme, Box, Size, Alignment, Composition, Visibility, ToolTip, Flyout, ControlState, Border,
    Brush, Automation, Focus, Accessibility, Typography, Behavior, Lifecycle,
}

/// <summary>Spec 067 §3 rows: how the apply arm is written.</summary>
internal enum ApplyShape
{
    /// <summary>Row A/B: diff guard, optional ordered type chain, direct property write, ClearValue on unset.</summary>
    Simple,
    /// <summary>Row C: a pure value conversion before the write.</summary>
    Converted,
    /// <summary>Row D: a static attached-property setter.</summary>
    Attached,
    /// <summary>Row E: several fields resolve to one DP through a hand-written resolver.</summary>
    Composite,
    /// <summary>Row F: real logic with cross-system state.</summary>
    HandCoded,
    /// <summary>Row G′: a DP written by the reference-edge machinery once the target resolves.</summary>
    ReferenceEdge,
    /// <summary>Row G: not a property write (handlers, gestures, lifecycle, refs, host-read state).</summary>
    Behavior,
}

/// <summary>How the shallow-skip gate (<c>ModifiersEqualCore</c>) compares a field.</summary>
internal enum SkipEquality
{
    /// <summary>The bucket's comparison: record Equals for Layout/Accessibility (NaN equals NaN), == for Visual and root fields.</summary>
    Value,
    /// <summary><c>BrushesEqual</c>: colour and opacity.</summary>
    BrushValue,
    /// <summary><c>FontFamiliesEqual</c>: <c>Source</c>.</summary>
    FontFamilyValue,
    /// <summary><c>ReferenceEquals</c>.</summary>
    Reference,
    /// <summary>Full mode: equal only when both are null. Visible mode: presence only.</summary>
    BlocksSkipWhenSet,
    /// <summary>Never compared. Requires <see cref="ModifierSlot.SkipReason"/>.</summary>
    NotCompared,
}

/// <summary>How the apply arm decides a field changed (spec 067 §6.6).</summary>
internal enum WriteEquality { Value, Reference }

/// <summary>What <c>ElementPool.CleanElement</c> does to a receiver's DP on pool return (spec 067 §6.2).</summary>
internal enum PoolReset
{
    /// <summary><c>ClearValue</c>, so a renter sees the control's default or styled value.</summary>
    ClearValue,
    /// <summary>A literal written back as a local value. Requires <see cref="ModifierReceiver.PoolDefault"/> and a reason.</summary>
    WriteDefault,
    /// <summary>Nothing. Requires a reason, e.g. the pool never recycles the receiver type.</summary>
    NotReset,
}

/// <summary>Which consumers a receiver row feeds (spec 067 §4.2).</summary>
[Flags]
internal enum ReceiverRole
{
    None = 0,
    /// <summary><c>ApplyModifiers</c> writes the plain value to this receiver.</summary>
    Apply = 1,
    /// <summary>The <c>ThemeRef</c> overload reaches this receiver (<c>GetDependencyPropertyName</c>).</summary>
    Theme = 2,
}

/// <summary>One control type a modifier reaches, and the DP it writes there.</summary>
internal sealed class ModifierReceiver
{
    internal ModifierReceiver(string type, string property, string dpOwner, ReceiverRole roles)
    {
        Type = type;
        Property = property;
        DpOwner = dpOwner;
        Roles = roles;
    }

    /// <summary>Full name of the receiver type, e.g. <c>Microsoft.UI.Xaml.Controls.Control</c>.</summary>
    public string Type { get; }

    /// <summary>The property written on the receiver.</summary>
    public string Property { get; }

    /// <summary>Full name of the type declaring <c>&lt;Property&gt;Property</c>.</summary>
    public string DpOwner { get; }

    public ReceiverRole Roles { get; }

    /// <summary>What the pool does to this DP on this receiver. Defaults to <see cref="PoolReset.ClearValue"/>.</summary>
    public PoolReset Pool { get; private set; } = PoolReset.ClearValue;

    /// <summary>The C# literal a <see cref="PoolReset.WriteDefault"/> writes, e.g. <c>14</c>.</summary>
    public string? PoolDefault { get; private set; }

    /// <summary>Why the receiver is not cleared: required for <see cref="PoolReset.WriteDefault"/> and <see cref="PoolReset.NotReset"/>.</summary>
    public string? PoolReason { get; private set; }

    internal void SetPool(PoolReset pool, string reason, string? poolDefault = null)
    {
        Pool = pool;
        PoolReason = reason;
        PoolDefault = poolDefault;
    }

    /// <summary>The receiver's simple type name, the form the analyzer's gates use.</summary>
    public string SimpleType => Type.Substring(Type.LastIndexOf('.') + 1);
}

/// <summary>One common-modifier field and every fact the framework needs about it.</summary>
internal sealed class ModifierSlot
{
    private readonly List<ModifierReceiver> _receivers = new List<ModifierReceiver>();

    internal ModifierSlot(string name, ModifierBucket bucket, string clrType, ModifierFamily family, ApplyShape shape)
    {
        Name = name;
        Bucket = bucket;
        ClrType = clrType;
        Family = family;
        Shape = shape;
        FluentNames = new[] { name };
    }

    /// <summary>The record property name.</summary>
    public string Name { get; }

    /// <summary>The public fluent modifiers that write this field. Defaults to <see cref="Name"/>.</summary>
    public string[] FluentNames { get; private set; }

    public ModifierBucket Bucket { get; }

    /// <summary>The field's value type, without the nullable wrapper.</summary>
    public string ClrType { get; }

    public ModifierFamily Family { get; }

    public ApplyShape Shape { get; }

    /// <summary>Receivers in the order the apply chain tests them.</summary>
    public IReadOnlyList<ModifierReceiver> Receivers => _receivers;

    /// <summary>Row D: the type declaring the attached property.</summary>
    public string? AttachedOwner { get; private set; }

    /// <summary>
    /// Row C: the conversion before the write, as the name of a static method in
    /// <c>ModifierConverters</c> (spec 067 §4.2). The generated apply arm calls it on the value.
    /// </summary>
    public string? Converter { get; private set; }

    /// <summary>Row E: the apply family this field feeds, e.g. <c>Padding</c> for <c>PaddingInlineStart</c>.</summary>
    public string? CompositeFamily { get; private set; }

    /// <summary>Rows E/F/G′: the reconciler method that owns the arm.</summary>
    public string? HandCodedMethod { get; private set; }

    public SkipEquality Skip { get; private set; } = SkipEquality.Value;

    /// <summary>Why a <see cref="SkipEquality.NotCompared"/> field is not compared.</summary>
    public string? SkipReason { get; private set; }

    public WriteEquality Write { get; private set; } = WriteEquality.Value;

    /// <summary>A callback slot: <c>ModifiersVisiblyEqual</c> ignores it.</summary>
    public bool CallbackSlot { get; private set; }

    /// <summary>
    /// <c>ModifierInfo.ControlGate</c>: an editorial analyzer fact (spec 067 §4.5), not derived
    /// from <see cref="Receivers"/>. Null where the analyzer deliberately has no gate.
    /// </summary>
    public string[]? AnalyzerControlGate { get; private set; }

    /// <summary>Non-null puts the slot in <c>ModifierTable.GateOnlyInReconciler</c> with this reason.</summary>
    public string? GateOnlyInReconcilerReason { get; private set; }

    /// <summary><c>ModifierInfo.PoolResetGate</c>, pinned from today's assignability derivation.</summary>
    public string[]? AnalyzerPoolResetGate { get; private set; }

    /// <summary>Presence records a shared edge for remaining-owner restore (<c>HasRestorableModifier</c>).</summary>
    public bool RestorableOwner { get; private set; }

    /// <summary>A non-identity value takes the composition visual at mount (<c>DeclaresTransform</c>).</summary>
    public bool TakesVisualAtMount { get; private set; }

    /// <summary>With <see cref="TakesVisualAtMount"/>: the value that does not take the visual.</summary>
    public string? IdentityValue { get; private set; }

    /// <summary>
    /// <c>AppliedModifierMap</c> target for a write that is not a DP on a receiver, e.g.
    /// <c>Visual.RotationAngle</c> for <c>Rotation</c>. Null reports each Apply receiver.
    /// </summary>
    public string? ReportTarget { get; private set; }

    /// <summary><c>AppliedModifierMap</c> reports nothing for this slot while the named slot is set (ToolTip under RichToolTip).</summary>
    public string? ReportSuppressedBy { get; private set; }

    /// <summary>The <c>AppliedModifierMap</c> owner is the hand-written <c>&lt;Name&gt;Owner</c> (AttachedFlyout's flyout-slot mirror).</summary>
    public bool ReportHandWritten { get; private set; }

    /// <summary>Receivers that carry <paramref name="role"/>.</summary>
    public IEnumerable<ModifierReceiver> ReceiversWith(ReceiverRole role) =>
        _receivers.Where(receiver => (receiver.Roles & role) != 0);

    // ── Builders, used only while ModifierSchema.Slots is constructed ──

    internal ModifierSlot Fluent(params string[] names) { FluentNames = names; return this; }

    internal ModifierSlot On(ReceiverRole roles, string type, string? property = null, string? dpOwner = null)
    {
        _receivers.Add(new ModifierReceiver(type, property ?? Name, dpOwner ?? type, roles));
        return this;
    }

    internal ModifierSlot OnFrameworkElement(string? property = null, string dpOwner = ModifierSchema.FrameworkElement)
        => On(ReceiverRole.Apply, ModifierSchema.FrameworkElement, property, dpOwner);

    internal ModifierSlot AttachedTo(string owner, string property)
    {
        AttachedOwner = owner;
        return On(ReceiverRole.Apply, ModifierSchema.FrameworkElement, property, owner);
    }

    internal ModifierSlot Converted(string converter) { Converter = converter; return this; }

    /// <summary>Declares a non-default pool reset for this slot's receiver of <paramref name="type"/>.</summary>
    internal ModifierSlot Pool(string type, PoolReset pool, string reason, string? poolDefault = null)
    {
        if ((pool == PoolReset.WriteDefault) != (poolDefault is not null))
            throw new System.InvalidOperationException(pool == PoolReset.WriteDefault
                ? $"{Name}: {type} is WriteDefault, so it needs the poolDefault literal CleanElement writes back."
                : $"{Name}: {type} is {pool}, so it takes no poolDefault.");
        var found = false;
        foreach (var receiver in _receivers)
        {
            if (receiver.Type != type) continue;
            receiver.SetPool(pool, reason, poolDefault);
            found = true;
        }
        if (!found) throw new System.InvalidOperationException($"{Name}: no receiver of type {type} to declare a pool reset on.");
        return this;
    }

    internal ModifierSlot Composite(string family, string method) { CompositeFamily = family; HandCodedMethod = method; return this; }

    internal ModifierSlot Method(string method) { HandCodedMethod = method; return this; }

    internal ModifierSlot Equality(SkipEquality skip, WriteEquality write = WriteEquality.Value)
    {
        Skip = skip;
        Write = write;
        return this;
    }

    internal ModifierSlot NotCompared(string reason) { Skip = SkipEquality.NotCompared; SkipReason = reason; return this; }

    internal ModifierSlot Callback() { CallbackSlot = true; Skip = SkipEquality.Reference; Write = WriteEquality.Reference; return this; }

    internal ModifierSlot Analyzer(string[]? controlGate = null, string? gateOnlyInReconciler = null, string[]? poolResetGate = null)
    {
        AnalyzerControlGate = controlGate;
        GateOnlyInReconcilerReason = gateOnlyInReconciler;
        AnalyzerPoolResetGate = poolResetGate;
        return this;
    }

    internal ModifierSlot Restorable(bool takesVisualAtMount = false, string? identity = null)
    {
        RestorableOwner = true;
        TakesVisualAtMount = takesVisualAtMount;
        IdentityValue = identity;
        return this;
    }

    internal ModifierSlot Report(string? target = null, string? suppressedBy = null, bool handWritten = false)
    {
        ReportTarget = target;
        ReportSuppressedBy = suppressedBy;
        ReportHandWritten = handWritten;
        return this;
    }
}
