using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.SourceMap.Generator;

/// <summary>
/// Spec 010 Route B — stamps <c>Element.CallSite</c> onto every Reactor DSL call
/// site using C# interceptors (stable in C# 14 / .NET 10). (The spec proposes
/// <c>Element.Source</c>; that name does not compile, because several element
/// records already declare an incompatible positional <c>Source</c> member.)
///
/// <para>The generator runs in the <em>consumer's</em> compilation and, for each
/// <c>Microsoft.UI.Reactor.Factories.*</c> invocation that returns an
/// <c>Element</c>, emits a same-signature interceptor that calls the original
/// factory and then stamps the call site's file + line. No factory signature
/// changes and no call site is edited — which is the whole point of this route,
/// and the only way to cover the 39 <c>params Element?[] children</c> factories
/// (C# forbids a trailing optional parameter after <c>params</c>, so
/// <c>[CallerLineNumber]</c> structurally cannot reach them).</para>
///
/// <para><b>Opt-in.</b> Emitting an interceptor into a project that has not
/// listed the interceptor namespace in <c>&lt;InterceptorsNamespaces&gt;</c> is a
/// hard <c>CS9137</c> build error, not a silent no-op. The generator therefore
/// emits nothing unless the consumer set <c>ReactorSourceMap=true</c>, which is
/// the same condition under which <c>Microsoft.UI.Reactor.targets</c> appends
/// the namespace. The two must stay welded together.</para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SourceMapInterceptorGenerator : IIncrementalGenerator
{
    internal const string InterceptorNamespace = "Microsoft.UI.Reactor.Generated";
    private const string FactoriesMetadataName = "Microsoft.UI.Reactor.Factories";
    private const string ElementMetadataName = "Microsoft.UI.Reactor.Core.Element";

    /// <summary>
    /// DSL factories that return an element the CALLER supplied rather than one they
    /// built: <c>When</c>/<c>If</c> return <c>then()</c>, <c>Expr</c> returns
    /// <c>render()</c>. See the rationale at the use site in <c>TryDescribe</c>.
    /// </summary>
    private static readonly ImmutableHashSet<string> PassThroughFactories =
        ImmutableHashSet.Create(StringComparer.Ordinal, "When", "If", "Expr");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ── Opt-in gate ───────────────────────────────────────────────────
        var enabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            p.GlobalOptions.TryGetValue("build_property.ReactorSourceMap", out var v)
            && v.Equals("true", StringComparison.OrdinalIgnoreCase));

        // Whether the BCL already declares InterceptsLocationAttribute. As of
        // .NET 10 it does NOT (verified: CS0234), so the polyfill below is the
        // live path — but probe rather than assume, so a future BCL that adds it
        // does not produce a duplicate-definition break.
        var needsPolyfill = context.CompilationProvider.Select(static (c, _) =>
            c.GetTypeByMetadataName("System.Runtime.CompilerServices.InterceptsLocationAttribute") is null);

        // PathMap, so the emitted literal matches what [CallerFilePath] would
        // produce under DeterministicSourcePaths (Directory.Build.props:117-119
        // turns that on when CI=true). The compiler rewrites CallerFilePath, but
        // it does NOT rewrite a string literal a generator emitted — we have to
        // apply the map ourselves or Route B leaks local disk paths into CI
        // binaries that Route A would have normalized.
        var pathMap = context.CompilationProvider.Select(static (c, _) =>
            (c.Options.SourceReferenceResolver as SourceFileResolver)?.PathMap
            ?? ImmutableArray<KeyValuePair<string, string>>.Empty);

        var callSites = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax,
                transform: static (ctx, ct) => TryDescribe(ctx, ct))
            .Where(static x => x is not null)
            .Collect();

        var input = callSites.Combine(enabled).Combine(needsPolyfill).Combine(pathMap);

        context.RegisterSourceOutput(input, static (spc, tuple) =>
        {
            var (((sites, isEnabled), polyfill), map) = tuple;
            if (!isEnabled || sites.IsDefaultOrEmpty) return;
            spc.AddSource("ReactorSourceMap.Interceptors.g.cs", Emit(sites!, polyfill, map));
        });
    }

    // ── Call-site discovery ───────────────────────────────────────────────

    private static CallSite? TryDescribe(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method) return null;
        if (!method.IsStatic) return null;
        if (method.MethodKind != MethodKind.Ordinary) return null;

        // GetSymbolInfo on a generic call returns the CONSTRUCTED method, whose
        // Parameters carry SUBSTITUTED types (e.g. `TProps` already replaced by
        // the caller's `ProbeProps`). Rendering those while also declaring
        // `<T, TProps>` produces an interceptor that mixes an open signature with
        // closed parameter types — which fails to bind, and additionally leaks the
        // caller's type arguments into the interceptor's signature (CS0122 when
        // one of them is a private nested type). Everything about the SIGNATURE
        // must come from the open definition; only the call site comes from the
        // constructed symbol.
        method = method.OriginalDefinition;

        // Only the Reactor DSL surface.
        //
        // Deliberately NOT widened to "any static that returns an Element", which
        // would be the obvious way to also cover the factories
        // Reactor.Wrappers.Generator emits onto the element type
        // (`FooWrapperElement.Foo(...)`). That was tried and rejected: Roslyn runs
        // every source generator against the SAME input compilation, so this
        // generator cannot see symbols another generator emitted. Widening the
        // filter therefore covers wrapper factories only in project shapes that
        // happen to compile twice (a WinUI XAML app, where the second pass sees the
        // first pass's generated files) and silently covers nothing in a
        // single-pass library. Both halves of that were measured — see
        // WrapperFactoryInterceptionTests, which pins the limitation. Coverage that
        // varies with project shape is worse than a documented, uniform gap.
        if (method.ContainingType?.ToDisplayString() != FactoriesMetadataName) return null;

        // Pass-throughs are never stamped, because they did not create the element.
        // When/If/Expr return `then()` / `render()` verbatim, so the returned element
        // belongs to whatever call site built it. Stamping here would name the `When(`
        // line as the creator.
        //
        // A first-stamp-wins guard in the emitted body is NOT sufficient on its own:
        // it only defers when the inner element already carries a location, and an
        // element built while mapping was disabled (or pulled from a memo cache) has
        // none — so the pass-through would happily claim it. Declining to intercept
        // these at all keeps "unknown" rather than inventing a confident wrong answer.
        //
        // This list is name-based and therefore driftable. PassThroughFactoryDriftTests
        // in Reactor.Tests reflects over Factories and fails if the set of
        // base-Element-returning factories that take a Func<...Element...> ever stops
        // matching it, so a fourth pass-through is a loud failure rather than a silent
        // misattribution.
        if (PassThroughFactories.Contains(method.Name)) return null;

        // Generic factories (Component<T>, Component<T,TProps>, ForEach<T>, Memo<TKey>,
        // ListView<T>, …). An interceptor for a generic method has to restate the
        // type parameters AND their constraints; whether Roslyn accepts that is
        // the point of the generic spike, so they are emitted rather than skipped.
        if (method.IsGenericMethod && method.TypeParameters.Any(HasUnrenderableConstraint))
            return null;

        // The interceptor has to be able to call the original with exactly the
        // arguments it received; by-ref parameters would need matching ref kinds
        // on both sides. Nothing in the DSL uses them today — bail rather than
        // emit something subtly wrong if that changes.
        if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return null;

        // Must return something we can stamp.
        if (!ReturnsElement(method.ReturnType)) return null;

        var location = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);
        if (location is null) return null;

        // MAPPED span, and a mapped path resolved the way Roslyn resolves it.
        //
        // Tooling that emits C# on a developer's behalf (T4, Razor, custom codegen)
        // marks its output with #line so positions resolve back to the file the
        // developer actually edits; [CallerLineNumber], [CallerFilePath] and the
        // debugger all honour it. GetLineSpan does not, so reading the unmapped span
        // would report a position in generated .cs that nobody edits and break the
        // CallerInfo parity this route claims.
        //
        // The subtlety is the path. A directive names its file relatively
        // (`#line 5000 "virtual-source.cs"`), and [CallerFilePath] reports it RESOLVED
        // against the directory of the physical file, not as the bare relative name.
        // Emitting FileLinePositionSpan.Path verbatim yields "virtual-source.cs" where
        // CallerInfo yields "...\\ProjectDir\\virtual-source.cs" — same file, different
        // string, and consumers compare these as strings. LineDirectiveParityTests pins
        // both halves against live CallerInfo probes under a real directive.
        var lineSpan = invocation.SyntaxTree.GetMappedLineSpan(invocation.Span, ct);

        return new CallSite(
            attribute: location.GetInterceptsLocationAttributeSyntax(),
            filePath: ResolveMappedPath(lineSpan, invocation.SyntaxTree.FilePath),
            line: lineSpan.StartLinePosition.Line + 1,
            signature: Signature.From(method));
    }

    /// <summary>
    /// The path <c>[CallerFilePath]</c> would report for a position: the syntax tree's
    /// own path normally, or a <c>#line</c> directive's file resolved against that
    /// tree's directory when one is in effect.
    /// </summary>
    private static string ResolveMappedPath(FileLinePositionSpan span, string treePath)
    {
        if (!span.HasMappedPath || string.IsNullOrEmpty(span.Path)) return treePath;
        if (System.IO.Path.IsPathRooted(span.Path)) return span.Path;

        var dir = System.IO.Path.GetDirectoryName(treePath);
        return string.IsNullOrEmpty(dir)
            ? span.Path
            : System.IO.Path.Combine(dir, span.Path);
    }

    /// <summary>
    /// A type parameter whose constraints reference ANOTHER type parameter of the
    /// same method (e.g. <c>Component&lt;T, TProps&gt;</c>'s
    /// <c>where T : Component&lt;TProps&gt;</c>) is still renderable — the names
    /// are in scope on the interceptor too. This hook exists for constraint
    /// shapes that genuinely cannot be restated; today only an unexpected
    /// nullability form on a `class` constraint qualifies, and nothing in the
    /// Reactor DSL hits it.
    /// </summary>
    private static bool HasUnrenderableConstraint(ITypeParameterSymbol tp) => false;

    private static bool ReturnsElement(ITypeSymbol type)    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == ElementMetadataName) return true;
        }
        return false;
    }

    // ── Emit ──────────────────────────────────────────────────────────────

    private static string Emit(
        ImmutableArray<CallSite?> sites,
        bool needsPolyfill,
        ImmutableArray<KeyValuePair<string, string>> pathMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Spec 010 Route B — Reactor source-map interceptors.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (needsPolyfill)
        {
            sb.AppendLine("namespace System.Runtime.CompilerServices");
            sb.AppendLine("{");
            sb.AppendLine("    // Not present in the .NET 10 BCL; the compiler recognizes this");
            sb.AppendLine("    // declaration by full name, so every interceptor generator has to");
            sb.AppendLine("    // supply its own copy.");
            sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
            sb.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
            sb.AppendLine("    {");
            sb.AppendLine("        public InterceptsLocationAttribute(int version, string data) { _ = version; _ = data; }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine($"namespace {InterceptorNamespace}");
        sb.AppendLine("{");
        sb.AppendLine("    file static class ReactorSourceMapInterceptors");
        sb.AppendLine("    {");

        int index = 0;
        foreach (var site in sites.Where(static s => s is not null))
        {
            var sig = site!.Signature;
            var mapped = ApplyPathMap(site.FilePath, pathMap);
            var name = $"__Reactor_{sig.MethodName}_{index}";

            sb.AppendLine($"        {site.Attribute}");
            sb.AppendLine($"        public static {sig.ReturnType} {name}{sig.TypeParameterList}({sig.ParameterList})");
            foreach (var clause in sig.ConstraintClauses)
                sb.AppendLine($"            {clause}");
            sb.AppendLine("        {");
            sb.AppendLine($"            var __e = {sig.OwnerType}.{sig.MethodName}{sig.TypeArgumentList}({sig.ArgumentList});");
            sb.AppendLine("            if (!global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled) return __e;");
            // The null guard is emitted only for a nullable-annotated return; on a
            // non-nullable one it would be dead code the nullable analysis flags.
            if (sig.ReturnsNullable)
            {
                sb.AppendLine("            if (__e is null) return __e;");
            }
            // Defence in depth. Pass-throughs (When/If/Expr) are excluded from
            // interception entirely in TryDescribe, because a guard here cannot save
            // them: an element built while mapping was off carries no location, so
            // "first stamp wins" would let the wrapper claim it. This guard still
            // earns its place for a factory that returns a CACHED element it did not
            // build on this call (a memo hit), where the existing stamp is the right
            // answer and this call site is not.
            sb.AppendLine("            if (__e.CallSite is not null) return __e;");
            sb.AppendLine("            return __e with");
            sb.AppendLine("            {");
            sb.AppendLine($"                CallSite = new global::Microsoft.UI.Reactor.Core.SourceLocation({Literal(mapped)}, {site.Line})");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine();
            index++;
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Applies the compiler's <c>PathMap</c> to a source path so the emitted
    /// literal matches what <c>[CallerFilePath]</c> would have produced.
    ///
    /// <para>Prefix substitution alone is NOT enough: Roslyn also rewrites the
    /// separators of the remainder to match the separator used by the
    /// replacement value, so a Windows path mapped to <c>/_/</c> comes out fully
    /// forward-slashed. Verified against a live <c>[CallerFilePath]</c> under
    /// <c>CI=true</c> — without this normalization the two source-map providers
    /// disagree on the path string (<c>/_/tests\Foo\Bar.cs</c> vs
    /// <c>/_/tests/Foo/Bar.cs</c>), which would make a "go to source" consumer
    /// behave differently depending on which provider is wired in.</para>
    /// <para>Comparison is <see cref="StringComparison.Ordinal"/>, matching Roslyn's
    /// <c>PathUtilities.NormalizePathPrefix</c> — which backs <c>SourceFileResolver</c>
    /// and therefore <c>[CallerFilePath]</c> — and whose own comment reads "we expect
    /// the client to use consistent capitalization; we use ordinal (case-sensitive)
    /// comparisons". Case-INSENSITIVE matching here would be a silent divergence: given
    /// a PathMap key whose casing differs from the real path, this generator would
    /// rewrite the literal while the compiler left <c>[CallerFilePath]</c> alone, and
    /// the two providers would disagree on where the code lives.</para>
    /// </summary>
    internal static string ApplyPathMap(string path, ImmutableArray<KeyValuePair<string, string>> pathMap)
    {
        if (pathMap.IsDefaultOrEmpty || string.IsNullOrEmpty(path)) return path;
        foreach (var entry in pathMap.Where(entry => path.StartsWith(entry.Key, StringComparison.Ordinal)))
        {
            var suffix = path.Substring(entry.Key.Length);
            if (entry.Value.IndexOf('/') >= 0 && entry.Value.IndexOf('\\') < 0)
                suffix = suffix.Replace('\\', '/');
            else if (entry.Value.IndexOf('\\') >= 0 && entry.Value.IndexOf('/') < 0)
                suffix = suffix.Replace('/', '\\');

            return entry.Value + suffix;
        }
        return path;
    }

    private static string Literal(string value) => "@\"" + value.Replace("\"", "\"\"") + "\"";

    // ── Models ────────────────────────────────────────────────────────────

    private sealed class CallSite : IEquatable<CallSite>
    {
        public CallSite(string attribute, string filePath, int line, Signature signature)
        {
            Attribute = attribute;
            FilePath = filePath;
            Line = line;
            Signature = signature;
        }

        public string Attribute { get; }
        public string FilePath { get; }
        public int Line { get; }
        public Signature Signature { get; }

        public bool Equals(CallSite? other)
            => other is not null
               && Attribute == other.Attribute
               && FilePath == other.FilePath
               && Line == other.Line
               && Signature.Equals(other.Signature);

        public override bool Equals(object? obj) => Equals(obj as CallSite);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Attribute.GetHashCode();
                h = (h * 397) ^ FilePath.GetHashCode();
                h = (h * 397) ^ Line;
                return (h * 397) ^ Signature.GetHashCode();
            }
        }
    }

    /// <summary>
    /// The pieces of the intercepted method's signature that the interceptor has
    /// to restate verbatim. Parameters are emitted WITHOUT default values on
    /// purpose: interception happens after overload resolution, so the compiler
    /// has already materialized any omitted optional argument. <c>params</c> IS
    /// restated because an expanded-form call site binds the array at the call
    /// site and the interceptor must accept it in the same form.
    /// </summary>
    private sealed class Signature : IEquatable<Signature>
    {
        private static readonly SymbolDisplayFormat s_typeFormat =
            SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(
                    SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                    | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        private Signature(string ownerType, string methodName, string returnType, string parameterList, string argumentList, bool returnsNullable,
                          string typeParameterList, string typeArgumentList, ImmutableArray<string> constraintClauses)
        {
            OwnerType = ownerType;
            MethodName = methodName;
            ReturnType = returnType;
            ParameterList = parameterList;
            ArgumentList = argumentList;
            ReturnsNullable = returnsNullable;
            TypeParameterList = typeParameterList;
            TypeArgumentList = typeArgumentList;
            ConstraintClauses = constraintClauses;
        }

        /// <summary>
        /// Fully-qualified type that declares the intercepted factory. Usually
        /// <c>Microsoft.UI.Reactor.Factories</c>, but the wrapper generator emits
        /// its factory as a static ON the element type, so the forwarding call
        /// has to name the real owner rather than assume <c>Factories</c>.
        /// </summary>
        public string OwnerType { get; }

        public string MethodName { get; }
        public string ReturnType { get; }
        public string ParameterList { get; }
        public string ArgumentList { get; }
        public bool ReturnsNullable { get; }

        /// <summary><c>&lt;T, TProps&gt;</c> on the interceptor declaration, or empty.</summary>
        public string TypeParameterList { get; }

        /// <summary><c>&lt;T, TProps&gt;</c> on the forwarding call, or empty.</summary>
        public string TypeArgumentList { get; }

        /// <summary>One rendered <c>where …</c> clause per constrained type parameter.</summary>
        public ImmutableArray<string> ConstraintClauses { get; }

        public static Signature From(IMethodSymbol method)
        {
            var parameters = new List<string>(method.Parameters.Length);
            var arguments = new List<string>(method.Parameters.Length);

            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var p = method.Parameters[i];
                var name = "__a" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var type = p.Type.ToDisplayString(s_typeFormat);
                var prefix = p.IsParams ? "params " : string.Empty;
                parameters.Add($"{prefix}{type} {name}");
                arguments.Add(name);
            }

            var typeParams = string.Empty;
            var constraints = ImmutableArray<string>.Empty;
            if (method.IsGenericMethod)
            {
                typeParams = "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Name)) + ">";
                constraints = method.TypeParameters
                    .Select(RenderConstraintClause)
                    .Where(c => c is not null)
                    .Select(c => c!)
                    .ToImmutableArray();
            }

            return new Signature(
                method.ContainingType.ToDisplayString(s_typeFormat),
                method.Name,
                method.ReturnType.ToDisplayString(s_typeFormat),
                string.Join(", ", parameters),
                string.Join(", ", arguments),
                method.ReturnType.NullableAnnotation == NullableAnnotation.Annotated,
                typeParams,
                typeParams,
                constraints);
        }

        /// <summary>
        /// Renders <c>where T : …</c> for one type parameter, or null when it is
        /// unconstrained. Order is fixed by the language: the primary constraint
        /// (<c>class</c> / <c>struct</c> / <c>unmanaged</c> / <c>notnull</c>)
        /// first, then base and interface types, then <c>new()</c> last.
        /// Constraint types are printed fully qualified WITH nullable
        /// annotations, so a constraint that names another type parameter of the
        /// same method — <c>where T : Component&lt;TProps&gt;</c> — round-trips:
        /// <c>TProps</c> is in scope on the interceptor under the same name.
        /// </summary>
        private static string? RenderConstraintClause(ITypeParameterSymbol tp)
        {
            var parts = new List<string>();

            if (tp.HasUnmanagedTypeConstraint) parts.Add("unmanaged");
            else if (tp.HasValueTypeConstraint) parts.Add("struct");
            else if (tp.HasReferenceTypeConstraint)
                parts.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (tp.HasNotNullConstraint) parts.Add("notnull");

            for (int i = 0; i < tp.ConstraintTypes.Length; i++)
            {
                var ct = tp.ConstraintTypes[i];
                var rendered = ct.ToDisplayString(s_typeFormat);
                // A value-type constraint already implies non-nullability; only a
                // reference constraint carries a meaningful '?' here.
                if (i < tp.ConstraintNullableAnnotations.Length
                    && tp.ConstraintNullableAnnotations[i] == NullableAnnotation.Annotated
                    && !ct.IsValueType
                    && !rendered.EndsWith("?", StringComparison.Ordinal))
                {
                    rendered += "?";
                }
                parts.Add(rendered);
            }

            if (tp.HasConstructorConstraint) parts.Add("new()");

            return parts.Count == 0 ? null : $"where {tp.Name} : {string.Join(", ", parts)}";
        }

        public bool Equals(Signature? other)
            => other is not null
               && OwnerType == other.OwnerType
               && MethodName == other.MethodName
               && ReturnType == other.ReturnType
               && ParameterList == other.ParameterList
               && TypeParameterList == other.TypeParameterList
               && ConstraintClauses.SequenceEqual(other.ConstraintClauses);

        public override bool Equals(object? obj) => Equals(obj as Signature);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = OwnerType.GetHashCode();
                h = (h * 397) ^ MethodName.GetHashCode();
                h = (h * 397) ^ ReturnType.GetHashCode();
                h = (h * 397) ^ ParameterList.GetHashCode();
                h = (h * 397) ^ TypeParameterList.GetHashCode();
                foreach (var c in ConstraintClauses) h = (h * 397) ^ c.GetHashCode();
                return h;
            }
        }
    }
}


