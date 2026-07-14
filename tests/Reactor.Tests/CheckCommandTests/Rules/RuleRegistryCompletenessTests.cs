using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

/// <summary>
/// Guardrail for spec 038 §6: <see cref="RuleRegistry"/> switched from an
/// <c>Assembly.GetTypes()</c> reflection scan to an explicit registration list
/// (<c>BuiltInRules()</c>) so the CLI stays trim/AOT-clean. That trades the
/// "can't forget a rule" property of reflection for a central list — this test
/// restores it: reflection is unrestricted in the test assembly, so we scan the
/// CLI for every concrete <see cref="IRulePattern"/> and assert each one is
/// actually registered in <see cref="RuleRegistry.Default"/>.
/// </summary>
public class RuleRegistryCompletenessTests
{
    [Fact]
    public void Default_Registers_Every_Concrete_IRulePattern_In_The_Cli_Assembly()
    {
        var reflected = typeof(IRulePattern).Assembly
            .GetTypes()
            .Where(t => typeof(IRulePattern).IsAssignableFrom(t)
                        && !t.IsAbstract
                        && !t.IsInterface
                        && t.GetConstructor(global::System.Type.EmptyTypes) is not null)
            .Select(t => t.Name)
            .OrderBy(n => n, global::System.StringComparer.Ordinal)
            .ToArray();

        var registered = RuleRegistry.Default.All
            .Select(r => r.GetType().Name)
            .OrderBy(n => n, global::System.StringComparer.Ordinal)
            .ToArray();

        var missing = reflected.Except(registered).ToArray();
        Assert.True(
            missing.Length == 0,
            $"IRulePattern implementations found by reflection but not listed in RuleRegistry.BuiltInRules(): {string.Join(", ", missing)}. " +
            "Add them to the explicit list in RuleRegistry.cs.");
    }
}
