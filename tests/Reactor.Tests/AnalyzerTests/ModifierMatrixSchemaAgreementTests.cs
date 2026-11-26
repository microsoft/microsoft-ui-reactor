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
        Assert.Equal(probes, cases);
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
            .OrderBy(s => s, StringComparer.Ordinal);
        var negatives = ModifierMatrixExpectations.Negatives.Select(n => n.Slot).ToHashSet(StringComparer.Ordinal);
        var reasons = ModifierMatrixExpectations.NoNegative.Select(n => n.Slot).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(negatives.Intersect(reasons));
        Assert.All(ModifierMatrixExpectations.NoNegative, n => Assert.False(string.IsNullOrWhiteSpace(n.Reason)));
        Assert.Equal(gated, negatives.Union(reasons).OrderBy(s => s, StringComparer.Ordinal));
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
