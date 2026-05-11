// Symbol-binding helper for Tier-3 rules. Spec 038 tasks doc §3.1a.
//
// Every rule binds target types and members through Roslyn ISymbol references
// resolved against the live Compilation, NOT by string-matching
// MemberAccessExpressionSyntax.Name.ValueText. That's the §3.1a contract; it
// exists because rules are load-bearing across Reactor minor releases, and a
// silent string-equality miss after a rename would degrade the rule to "never
// fires" with no signal — exactly the failure mode we cannot tolerate.
//
// The resolver caches per-Compilation. Compilations are immutable in Roslyn,
// so the cache key is the Compilation reference itself.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class RuleSymbolResolver
{
    static readonly ConditionalWeakTable<CSharpCompilation, RuleSymbolResolver> _cache = new();

    readonly CSharpCompilation _compilation;
    readonly ConcurrentDictionary<string, INamedTypeSymbol?> _types = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<(INamedTypeSymbol type, string name), IMethodSymbol?> _methods
        = new();
    readonly ConcurrentDictionary<(INamedTypeSymbol type, string name), ISymbol?> _members
        = new();

    RuleSymbolResolver(CSharpCompilation compilation) { _compilation = compilation; }

    /// <summary>
    /// Returns the resolver bound to <paramref name="compilation"/>, creating
    /// it on first call. Two callers with the same compilation reference get
    /// the same resolver instance (and therefore share its caches).
    /// </summary>
    public static RuleSymbolResolver For(CSharpCompilation compilation)
        => _cache.GetValue(compilation, c => new RuleSymbolResolver(c));

    /// <summary>
    /// Resolves a fully-qualified type name (e.g. "Microsoft.UI.Reactor.Core.ButtonElement")
    /// against the compilation's metadata. Returns null when the type is not
    /// in the compilation graph (rule should short-circuit to Silent and
    /// surface as self-disabled in --list-rules).
    /// </summary>
    public INamedTypeSymbol? ResolveType(string fullyQualifiedName)
    {
        if (string.IsNullOrEmpty(fullyQualifiedName)) return null;
        return _types.GetOrAdd(fullyQualifiedName, name =>
            _compilation.GetTypeByMetadataName(name));
    }

    /// <summary>
    /// Resolves a method on a previously-resolved type by name. Returns the
    /// first matching member (rules that need overload resolution should walk
    /// <see cref="INamedTypeSymbol.GetMembers(string)"/> directly).
    /// </summary>
    public IMethodSymbol? ResolveMethod(INamedTypeSymbol type, string methodName)
    {
        return _methods.GetOrAdd((type, methodName), key =>
        {
            foreach (var m in key.type.GetMembers(key.name).OfType<IMethodSymbol>())
                return m;
            return null;
        });
    }

    /// <summary>
    /// Resolves a non-method member (property / field) on a previously-resolved
    /// type by name. Returns null when the member is absent.
    /// </summary>
    public ISymbol? ResolveMember(INamedTypeSymbol type, string memberName)
    {
        return _members.GetOrAdd((type, memberName), key =>
        {
            foreach (var s in key.type.GetMembers(key.name))
            {
                if (s is IMethodSymbol) continue;
                return s;
            }
            return null;
        });
    }
}
