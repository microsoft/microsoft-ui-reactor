using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor.Analyzers.Modifiers;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.ModifierMatrix;
using Microsoft.UI.Reactor.ModifierGen;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Spec 067 §10.2: the schema-generated probe table and the frozen matrix the
/// <c>ModifierMatrix_*</c> selftests run must agree, and the generated files must be fresh. The
/// matrix stays hand-written on purpose, so a deliberate behaviour change edits the schema and the
/// matrix together and both show in review.
/// </summary>
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: resolves WinUI type names with Assembly.GetType. JIT-only host, never trimmed.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only: reflects the static DP identifiers of WinUI types. JIT-only host, never trimmed.")]
public class ModifierMatrixSchemaAgreementTests
{
    [Fact]
    public void Generated_Files_Are_Fresh()
    {
        var root = Microsoft.UI.Reactor.Cli.Pack.RepoRootFinder.FindRepoRoot()!;
        foreach (var (relativePath, content) in ModifierEmitter.EmitAll())
        {
            var path = global::System.IO.Path.Join(root, relativePath);
            Assert.True(global::System.IO.File.Exists(path), $"{relativePath} is missing. Run: dotnet run --project tools/Reactor.ModifierGen");
            var current = global::System.IO.File.ReadAllText(path).ReplaceLineEndings("\n");
            Assert.True(current == content, $"{relativePath} is stale. Run: dotnet run --project tools/Reactor.ModifierGen");
        }
    }

    [Fact]
    public void Matrix_Has_One_Case_Per_Generated_Probe()
    {
        var probes = ModifierEmitter.MatrixSlots()
            .SelectMany(slot => slot.ReceiversWith(ReceiverRole.Apply)
                .Select(r => $"{slot.Name} on {Simple(r.Type)} writes {Simple(r.DpOwner)}.{r.Property}Property"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        var cases = ModifierMatrixExpectations.Cases
            .Select(c => $"{c.Slot} on {c.Receiver} writes {c.Dp}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(probes.Count > 40, $"Only {probes.Count} probes: the slot filter is broken.");
        Assert.Equal(cases.Count, cases.Distinct(StringComparer.Ordinal).Count());
        Assert.True(probes.SequenceEqual(cases, StringComparer.Ordinal),
            "Schema probes and ModifierMatrixExpectations.Cases disagree."
            + $"\n  in the schema, missing from Cases: {string.Join("; ", probes.Except(cases, StringComparer.Ordinal))}"
            + $"\n  in Cases, not in the schema: {string.Join("; ", cases.Except(probes, StringComparer.Ordinal))}");
    }

    [Fact]
    public void Every_Hand_Coded_Slot_Is_Excluded_With_A_Reason()
    {
        var handCoded = ModifierSchema.Slots.Where(s => s.Shape == ApplyShape.HandCoded).Select(s => s.Name)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        var excluded = ModifierMatrixExpectations.Excluded.Select(e => e.Slot).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.Contains("Foreground", handCoded);   // positive control: the #1687 slot
        Assert.All(ModifierMatrixExpectations.Excluded, e => Assert.False(string.IsNullOrWhiteSpace(e.Reason)));
        Assert.True(handCoded.SequenceEqual(excluded, StringComparer.Ordinal),
            "Hand-coded schema slots and ModifierMatrixExpectations.Excluded disagree."
            + $"\n  hand-coded, not excluded: {string.Join(", ", handCoded.Except(excluded))}"
            + $"\n  excluded, not hand-coded: {string.Join(", ", excluded.Except(handCoded))}");
    }

    [Fact]
    public void Captioned_Unset_Cases_Are_Matrix_Slots_On_Receivers()
    {
        Assert.NotEmpty(ModifierMatrixExpectations.CaptionedUnset);
        foreach (var (group, slot, carrier, caption) in ModifierMatrixExpectations.CaptionedUnset)
        {
            Assert.Contains(ModifierEmitter.MatrixSlots(), s => s.Name == slot);
            Assert.Contains(ModifierMatrixExpectations.Cases, c => c.Group == group);
            Assert.Contains(ApplyReceivers(slot), r => r.IsAssignableFrom(Carrier(carrier)));
            Assert.False(string.IsNullOrEmpty(caption));
        }
    }

    [Fact]
    public void Case_Carriers_Are_Receivers_And_Negative_Carriers_Are_Not()
    {
        foreach (var c in ModifierMatrixExpectations.Cases)
        {
            var receiver = ApplyReceivers(c.Slot).Single(t => t.Name == c.Receiver);
            Assert.True(receiver.IsAssignableFrom(Carrier(c.Carrier)), $"{c.Slot}: carrier {c.Carrier} is not a {c.Receiver}.");
        }

        foreach (var n in ModifierMatrixExpectations.Negatives)
        {
            var carrier = Carrier(n.Carrier);
            Assert.DoesNotContain(ApplyReceivers(n.Slot), r => r.IsAssignableFrom(carrier));
            // The control declares (or inherits) the DP the negative reads, so "no local value" means something.
            var owner = WinUiType(n.Dp[..n.Dp.IndexOf('.')]);
            Assert.True(owner.IsAssignableFrom(carrier), $"{n.Slot}: {n.Carrier} has no {n.Dp}.");
            Assert.NotNull(owner.GetProperty(n.Dp[(n.Dp.IndexOf('.') + 1)..], BindingFlags.Public | BindingFlags.Static));
        }
    }

    [Fact]
    public void Every_Gated_Slot_Has_A_Negative_Or_A_Reason()
    {
        var gated = ModifierEmitter.MatrixSlots()
            .Where(s => s.ReceiversWith(ReceiverRole.Apply).Any(r => r.Type != ModifierSchema.FrameworkElement))
            .Select(s => s.Name)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        var negatives = ModifierMatrixExpectations.Negatives.Select(n => n.Slot).ToHashSet(StringComparer.Ordinal);
        var reasons = ModifierMatrixExpectations.NoNegative.Select(n => n.Slot).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(negatives.Intersect(reasons));
        Assert.All(ModifierMatrixExpectations.NoNegative, n => Assert.False(string.IsNullOrWhiteSpace(n.Reason)));
        var accounted = negatives.Union(reasons).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(gated.SequenceEqual(accounted, StringComparer.Ordinal),
            "Every gated slot needs a ModifierMatrixExpectations.Negatives entry or a NoNegative reason."
            + $"\n  gated, unaccounted: {string.Join(", ", gated.Except(accounted))}"
            + $"\n  accounted, not gated: {string.Join(", ", accounted.Except(gated))}");
    }

    [Fact]
    public void Every_Matrix_Group_Has_A_Registered_Fixture()
    {
        var root = Microsoft.UI.Reactor.Cli.Pack.RepoRootFinder.FindRepoRoot()!;
        var registry = global::System.IO.File.ReadAllText(global::System.IO.Path.Join(
            root, "tests", "Reactor.AppTests.Host", "SelfTest", "SelfTestFixtureRegistry.cs"));
        foreach (var group in ModifierMatrixExpectations.Cases.Select(c => c.Group).Distinct())
            Assert.Contains($"(\"ModifierMatrix_{group}\", static harness => new ModifierMatrixFixtures.Execution(harness, \"{group}\"))", registry);
        Assert.Contains("(\"ModifierMatrix_Negatives\", static harness => new ModifierMatrixFixtures.Negatives(harness))", registry);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static IEnumerable<Type> ApplyReceivers(string slot) =>
        ModifierSchema.Find(slot)!.ReceiversWith(ReceiverRole.Apply)
            .Select(r => typeof(Microsoft.UI.Xaml.FrameworkElement).Assembly.GetType(r.Type)
                ?? throw new InvalidOperationException($"{slot}: receiver {r.Type} does not resolve."));

    private static Type Carrier(string name) => WinUiType(name);

    private static Type WinUiType(string simpleName)
    {
        var assembly = typeof(Microsoft.UI.Xaml.FrameworkElement).Assembly;
        return assembly.GetType("Microsoft.UI.Xaml.Controls." + simpleName)
            ?? assembly.GetType("Microsoft.UI.Xaml." + simpleName)
            ?? throw new InvalidOperationException($"No WinUI type named {simpleName}.");
    }

    private static string Simple(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];
}
