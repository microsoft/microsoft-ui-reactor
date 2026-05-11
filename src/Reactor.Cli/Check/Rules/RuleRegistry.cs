// Reflection-based discovery for IRulePattern implementations. Spec 038 §3.1.
//
// On first access (and once-only per assembly), the registry scans the
// declaring assembly for non-abstract types implementing IRulePattern,
// instantiates each via its public parameterless constructor, and stores them
// in a name-keyed map. New rules drop into src/Reactor.Cli/Check/Rules/ with
// no central wire-up — that's the §6 design intent (rules are batch-mergeable,
// independently reviewable, individually disable-able via CLI flag).
//
// Discovery is intentionally restricted to the Reactor.Cli assembly (rules
// are first-party only — we don't load plugins from disk). Tests that need
// a non-default ruleset construct a registry via the Of() factory.

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class RuleRegistry
{
    static readonly Lazy<RuleRegistry> _default = new(() => Of(DiscoverFromAssembly(typeof(IRulePattern).Assembly)));

    /// <summary>
    /// Registry built by reflecting over Reactor.Cli for IRulePattern
    /// implementations. This is the production singleton; tests should
    /// construct registries via <see cref="Of(IEnumerable{IRulePattern})"/>.
    /// </summary>
    public static RuleRegistry Default => _default.Value;

    readonly ImmutableArray<IRulePattern> _rules;
    readonly ImmutableDictionary<string, IRulePattern> _byName;

    RuleRegistry(ImmutableArray<IRulePattern> rules)
    {
        _rules = rules;
        _byName = rules.ToImmutableDictionary(r => r.Name, StringComparer.Ordinal);
    }

    public static RuleRegistry Of(IEnumerable<IRulePattern> rules)
    {
        // Detect duplicate Names early: a duplicate would silently hide one
        // of the rules from --disable-rule, which is a category of bug we
        // can't afford in a kill-switch surface.
        var sorted = rules.OrderBy(r => r.Name, StringComparer.Ordinal).ToImmutableArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in sorted)
        {
            if (!seen.Add(r.Name))
                throw new InvalidOperationException($"Duplicate rule Name '{r.Name}' — every rule must have a unique identifier.");
        }
        return new RuleRegistry(sorted);
    }

    /// <summary>All discovered rules, ordered by Name (stable for --list-rules).</summary>
    public ImmutableArray<IRulePattern> All => _rules;

    /// <summary>Tries to find a rule by exact Name.</summary>
    public bool TryGet(string name, out IRulePattern rule)
    {
        if (_byName.TryGetValue(name, out var r)) { rule = r; return true; }
        rule = default!;
        return false;
    }

    /// <summary>
    /// Returns the highest-confidence match across all enabled rules for the
    /// supplied context. Skips rules in <paramref name="disabledNames"/>;
    /// rules whose declared targets fail to resolve self-skip and are
    /// reported via <paramref name="onSelfDisabled"/> for --list-rules /
    /// trace diagnostics.
    /// </summary>
    public RuleHit? BestMatch(in RuleContext ctx, ISet<string>? disabledNames = null, Action<string, string>? onSelfDisabled = null)
    {
        RuleHit? best = null;
        foreach (var rule in _rules)
        {
            if (disabledNames is not null && disabledNames.Contains(rule.Name)) continue;
            if (!TargetsResolve(rule, ctx.Resolver, out var unresolved))
            {
                onSelfDisabled?.Invoke(rule.Name, unresolved!);
                continue;
            }
            RuleSuggestion r;
            try { r = rule.TryMatch(in ctx); }
            catch { continue; }
            if (!r.HasMatch) continue;
            if (best is null || r.Confidence > best.Value.Suggestion.Confidence)
                best = new RuleHit(rule, r);
        }
        return best;
    }

    /// <summary>
    /// Pre-flight target resolution. The exact same check the rule will run
    /// internally at the head of TryMatch, hoisted here so the registry can
    /// short-circuit and report a stable self-disabled state to --list-rules
    /// even on invocations where the rule's diagnostic code doesn't surface.
    /// </summary>
    internal static bool TargetsResolve(IRulePattern rule, RuleSymbolResolver resolver, out string? firstUnresolved)
    {
        foreach (var target in rule.DeclaredTargets)
        {
            if (resolver.ResolveType(target) is null)
            {
                firstUnresolved = target;
                return false;
            }
        }
        firstUnresolved = null;
        return true;
    }

    /// <summary>
    /// Snapshot of per-rule status for the active compilation, used by
    /// --list-rules. Self-disabled rules surface the first unresolved target.
    /// </summary>
    public IReadOnlyList<RuleStatus> Statuses(CSharpCompilation? compilation, ISet<string>? disabledNames = null)
    {
        var resolver = compilation is null ? null : RuleSymbolResolver.For(compilation);
        var list = new List<RuleStatus>(_rules.Length);
        foreach (var rule in _rules)
        {
            if (disabledNames is not null && disabledNames.Contains(rule.Name))
            {
                list.Add(new RuleStatus(rule.Name, rule.Provenance, RuleState.UserDisabled, null));
                continue;
            }
            if (resolver is null)
            {
                list.Add(new RuleStatus(rule.Name, rule.Provenance, RuleState.Enabled, null));
                continue;
            }
            if (!TargetsResolve(rule, resolver, out var unresolved))
            {
                list.Add(new RuleStatus(rule.Name, rule.Provenance, RuleState.SelfDisabled, unresolved));
                continue;
            }
            list.Add(new RuleStatus(rule.Name, rule.Provenance, RuleState.Enabled, null));
        }
        return list;
    }

    static IEnumerable<IRulePattern> DiscoverFromAssembly(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        foreach (var t in types)
        {
            if (t is null) continue;
            if (t.IsAbstract || t.IsInterface) continue;
            if (!typeof(IRulePattern).IsAssignableFrom(t)) continue;
            var ctor = t.GetConstructor(Type.EmptyTypes);
            if (ctor is null) continue;
            IRulePattern instance;
            try { instance = (IRulePattern)ctor.Invoke(null); }
            catch { continue; }
            yield return instance;
        }
    }
}

internal readonly record struct RuleHit(IRulePattern Rule, RuleSuggestion Suggestion);

internal enum RuleState
{
    Enabled,
    UserDisabled,
    SelfDisabled,
}

internal readonly record struct RuleStatus(string Name, string Provenance, RuleState State, string? UnresolvedTarget);
