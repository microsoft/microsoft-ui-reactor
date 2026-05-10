// Pre-filter index over `Microsoft.UI.Reactor.Factories.*` static methods.
// The Tier-2 suggesters use this to (a) propose factory names for CS0103,
// and (b) suggest named-argument moves for CS1061 cases like
// `.OnClick(x)` → `Button(label, onClick: x)`. Spec 038 §1.3.
//
// The index is built once per CSharpCompilation and is otherwise immutable.

using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.UI.Reactor.Cli.Check;

internal sealed class FactoryIndex
{
    public const string FactoryTypeFullName = "Microsoft.UI.Reactor.Factories";

    /// <summary>
    /// Factory name → list of overloads (each holding the IMethodSymbol and a
    /// pre-cached array of parameter names for fast named-arg suggestion).
    /// </summary>
    public FrozenDictionary<string, IReadOnlyList<FactoryOverload>> ByName { get; }

    /// <summary>
    /// Flat list of every factory overload across every name. Useful for the
    /// CS0103 fuzzy-match path which doesn't know the name yet.
    /// </summary>
    public IReadOnlyList<FactoryOverload> All { get; }

    FactoryIndex(FrozenDictionary<string, IReadOnlyList<FactoryOverload>> byName, IReadOnlyList<FactoryOverload> all)
    {
        ByName = byName;
        All = all;
    }

    /// <summary>Empty index — used when the compilation does not reference Reactor.</summary>
    public static FactoryIndex Empty { get; } = new(
        FrozenDictionary<string, IReadOnlyList<FactoryOverload>>.Empty,
        Array.Empty<FactoryOverload>());

    public static FactoryIndex Build(CSharpCompilation compilation)
    {
        var factoryType = compilation.GetTypeByMetadataName(FactoryTypeFullName);
        if (factoryType is null)
            return Empty;

        var byName = new Dictionary<string, List<FactoryOverload>>(StringComparer.Ordinal);
        var all = new List<FactoryOverload>();

        foreach (var member in factoryType.GetMembers())
        {
            if (member is not IMethodSymbol m) continue;
            if (m.MethodKind != MethodKind.Ordinary) continue;
            if (!m.IsStatic) continue;
            if (m.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public) continue;

            var parameterNames = m.Parameters.Length == 0
                ? Array.Empty<string>()
                : m.Parameters.Select(p => p.Name).ToArray();
            var overload = new FactoryOverload(m, parameterNames);

            if (!byName.TryGetValue(m.Name, out var bucket))
                byName[m.Name] = bucket = new List<FactoryOverload>();
            bucket.Add(overload);
            all.Add(overload);
        }

        var frozen = byName.ToFrozenDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<FactoryOverload>)kv.Value);
        return new FactoryIndex(frozen, all);
    }

    /// <summary>
    /// Returns true if any overload of any factory has a parameter with the
    /// given name (case-sensitive — matches C#'s named-argument resolution).
    /// </summary>
    public bool TryFindParameter(string parameterName, out FactoryOverload owner, out IParameterSymbol parameter)
    {
        foreach (var ov in All)
        {
            foreach (var p in ov.Method.Parameters)
            {
                if (string.Equals(p.Name, parameterName, StringComparison.Ordinal))
                {
                    owner = ov;
                    parameter = p;
                    return true;
                }
            }
        }
        owner = default!;
        parameter = default!;
        return false;
    }
}

internal sealed record FactoryOverload(IMethodSymbol Method, string[] ParameterNames);
