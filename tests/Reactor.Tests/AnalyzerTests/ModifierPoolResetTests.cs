using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Analyzers.Modifiers;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.ModifierMatrix;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Spec 067 §6.2 layer 5: each receiver's declared <see cref="PoolReset"/> against what
/// <c>ElementPool.CleanElement</c> really does, read through <see cref="CleanElementScan"/>, and
/// against <see cref="PoolResetExpectations"/>, the frozen set the <c>ModifierPoolReset</c> selftest
/// checks at runtime. Layer 5 generates the modifier-backed resets from these declarations, so this
/// is the differential pin: the generated reset must produce exactly the set the hand-written
/// <c>CleanElement</c> produced.
/// </summary>
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: resolves WinUI type names with Assembly.GetType. JIT-only host, never trimmed.")]
public class ModifierPoolResetTests
{
    private static IEnumerable<(ModifierSlot Slot, ModifierReceiver Receiver)> Receivers() =>
        ModifierSchema.Slots.SelectMany(slot => slot.ReceiversWith(ReceiverRole.Apply).Select(receiver => (slot, receiver)));

    // The receiver as CleanElementScan names it, then the DP: an attached one names its owner too,
    // so AutomationProperties.Name is not FrameworkElement.Name.
    private static string Key(ModifierSlot slot, ModifierReceiver receiver) =>
        receiver.SimpleType + "." + (AttachedOwner(receiver) is { } owner ? owner + "." : "") + receiver.Property;

    // The owner of an attached DP: one declared neither by the receiver nor by a base every
    // receiver shares (FrameworkElement, UIElement), e.g. AutomationProperties or ToolTipService.
    private static string? AttachedOwner(ModifierReceiver receiver)
    {
        var owner = Simple(receiver.DpOwner);
        return owner == receiver.SimpleType || owner is "FrameworkElement" or "UIElement" ? null : owner;
    }

    // Every declared (receiver, DP) and what the pool leaves on it, in PoolResetExpectations' terms:
    // "Unset" for a ClearValue, the literal for a WriteDefault, "NotReset" for a never-pooled receiver.
    private static Dictionary<string, string> Declared()
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (slot, receiver) in Receivers())
        {
            var key = Key(slot, receiver);
            var leaves = receiver.Pool switch
            {
                PoolReset.ClearValue => "Unset",
                PoolReset.WriteDefault => receiver.PoolDefault!,
                _ => "NotReset",
            };
            Assert.False(declared.TryGetValue(key, out var other) && other != leaves,
                $"{slot.Name}: {key} leaves {leaves} here and {other} on another slot writing the same DP.");
            declared[key] = leaves;
        }
        return declared;
    }

    [Fact]
    public void Every_Declared_Pool_Reset_Matches_CleanElement()
    {
        Assert.NotEmpty(CleanElementScan.Resets);
        var problems = new List<string>();
        foreach (var (slot, receiver) in Receivers())
        {
            var resets = CleanElementScan.Resets
                .Where(r => r.Receiver == receiver.SimpleType && r.Property == receiver.Property
                    && (AttachedOwner(receiver) is not { } attached || (r.Owner is { } owner && owner.EndsWith(attached, StringComparison.Ordinal))))
                .ToList();
            var ok = receiver.Pool switch
            {
                PoolReset.ClearValue => resets.Any(r => r.Shape == ResetShape.ClearValue),
                PoolReset.WriteDefault => resets.Any(r => r.Shape == ResetShape.Assignment && r.Value == receiver.PoolDefault),
                PoolReset.NotReset => resets.Count == 0,
                _ => false,
            };
            if (!ok)
                problems.Add($"{slot.Name}: {Key(slot, receiver)} is declared {receiver.Pool}, but CleanElement has [{string.Join(", ", resets)}].");
            if (receiver.Pool != PoolReset.ClearValue && string.IsNullOrWhiteSpace(receiver.PoolReason))
                problems.Add($"{slot.Name}: {Key(slot, receiver)} is {receiver.Pool} without a reason.");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Not_Reset_Means_Never_Pooled_And_A_Reset_Reaches_A_Pooled_Type()
    {
        var poolable = ModifierSchema.PoolableTypeNames.Select(WinUiType).ToList();
        var problems = new List<string>();
        foreach (var (slot, receiver) in Receivers())
        {
            var type = typeof(Microsoft.UI.Xaml.FrameworkElement).Assembly.GetType(receiver.Type)
                ?? throw new InvalidOperationException($"{slot.Name}: {receiver.Type} does not resolve.");
            var reachesPool = poolable.Any(type.IsAssignableFrom);
            if ((receiver.Pool == PoolReset.NotReset) == reachesPool)
                problems.Add($"{slot.Name}: {Key(slot, receiver)} is {receiver.Pool}, but a pooled type {(reachesPool ? "is" : "is not")} a {receiver.SimpleType}.");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_Selftest_Exercises_Every_Declared_Reset()
    {
        // PoolResetExpectations is the selftest's own frozen oracle. It must name exactly what the
        // schema declares, so ModifierPoolReset fails when any one reset is dropped, and a deliberate
        // pool change edits the schema and that file together.
        var declared = Declared();
        var frozen = PoolResetExpectations.Resets.Select(r => (r.Reset, r.Leaves))
            .Concat(PoolResetExpectations.NotReset.Select(r => (Reset: r, Leaves: "NotReset")))
            .ToDictionary(r => r.Reset, r => r.Leaves, StringComparer.Ordinal);
        var problems = declared.Where(kv => !frozen.TryGetValue(kv.Key, out var leaves) || leaves != kv.Value)
            .Select(kv => kv.Value == "NotReset"
                ? $"Add to PoolResetExpectations.NotReset: \"{kv.Key}\","
                : $"Add to PoolResetExpectations.Resets (and a Dp/Sample arm in ModifierPoolResetFixture): (\"{kv.Key}\", \"{kv.Value}\"),")
            .Concat(frozen.Where(kv => !declared.TryGetValue(kv.Key, out var leaves) || leaves != kv.Value)
                .Select(kv => $"Remove from PoolResetExpectations: {kv.Key} ({kv.Value})"))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // Its pooled-type list is ElementPool's, and every reset's receiver is one it knows.
        Assert.Equal(ModifierSchema.PoolableTypeNames.Order(StringComparer.Ordinal),
            PoolResetExpectations.PooledTypes.Select(t => t.Type).Order(StringComparer.Ordinal));
        Assert.All(PoolResetExpectations.Resets, r => Assert.Contains(r.Reset[..r.Reset.IndexOf('.')], PoolResetExpectations.ReceiverTypes));
    }

    [Fact]
    public void Analyzer_Pool_Reset_Gates_Match_The_Declared_Resets()
    {
        // ModifierInfo.PoolResetGate still lists its pooled receivers by hand. Until it is derived
        // from the declarations, it must name exactly the receivers that are not NotReset.
        var problems = new List<string>();
        foreach (var slot in ModifierSchema.Slots.Where(s => s.AnalyzerPoolResetGate is not null))
        {
            var reset = slot.ReceiversWith(ReceiverRole.Apply).Where(r => r.Pool != PoolReset.NotReset)
                .Select(r => r.SimpleType).Distinct().Order(StringComparer.Ordinal);
            var gate = slot.AnalyzerPoolResetGate!.Order(StringComparer.Ordinal);
            if (!reset.SequenceEqual(gate))
                problems.Add($"{slot.Name}: poolResetGate [{string.Join(", ", gate)}], declared resets on [{string.Join(", ", reset)}].");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Attached_Flyout_Has_No_Pool_Reset_Yet()
    {
        // Issue #1753: WithFlyout's FlyoutBase.AttachedFlyout has no schema receiver and CleanElement
        // never clears it on non-Button pooled types. When #1753 adds that clear, give the slot a
        // receiver and its PoolResetExpectations row, so both oracles pin it, and delete this fact.
        var slot = ModifierSchema.Slots.Single(s => s.Name == "AttachedFlyout");
        Assert.Empty(slot.ReceiversWith(ReceiverRole.Apply));
        Assert.DoesNotContain(CleanElementScan.Resets, r => r.Property == "AttachedFlyout");
    }
    private static string Simple(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];

    private static Type WinUiType(string simpleName)
    {
        var assembly = typeof(Microsoft.UI.Xaml.FrameworkElement).Assembly;
        return assembly.GetType("Microsoft.UI.Xaml.Controls." + simpleName)
            ?? throw new InvalidOperationException($"No WinUI control type named {simpleName}.");
    }
}
