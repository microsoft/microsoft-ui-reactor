using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
    private const string EmptyElementMetadataName = "Microsoft.UI.Reactor.Core.EmptyElement";

    /// <summary>
    /// The opt-in marker for helper-method attribution (mechanism 1). Matched by
    /// display string rather than <c>GetTypeByMetadataName</c> so a consumer that
    /// somehow sees two copies of the type still gets consistent treatment.
    /// </summary>
    internal const string TransparentAttributeMetadataName =
        "Microsoft.UI.Reactor.Diagnostics.ReactorSourceTransparentAttribute";

    /// <summary>
    /// Reported when <c>[ReactorSourceTransparent]</c> is applied to a method the
    /// generator cannot emit a forwarding interceptor for.
    ///
    /// <para>The alternative — doing nothing — would make the attribute a silent
    /// no-op on exactly the shape people reach for first (a <c>private static</c>
    /// helper inside a component), with no signal that the intended attribution
    /// never happened.</para>
    /// </summary>
    internal static readonly DiagnosticDescriptor UnusableTransparentAnnotation = new(
        "REACTOR_SOURCEMAP_001",
        "[ReactorSourceTransparent] cannot be honoured on this method",
        "'{0}' is marked [ReactorSourceTransparent], but source attribution cannot be deferred to its "
        + "callers because {1}. Elements it returns keep reporting its own line.",
        "Reactor.SourceMap",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "The source-map generator honours [ReactorSourceTransparent] by emitting an interceptor that "
        + "forwards to the annotated method and stamps the caller's line. That is only possible for a "
        + "static, Element-returning, ordinary method that generated code in the same compilation can "
        + "name - so public or internal, not private, not a local function, and not nested in a "
        + "file-local or generic type. When the annotation cannot be honoured the generator leaves the "
        + "method's own call sites stamped as usual, so attribution is never worse than it would be "
        + "without the attribute.");

    /// <summary>
    /// DSL factories that return an element the CALLER supplied rather than one they
    /// built: <c>When</c>/<c>If</c> return <c>then()</c>, <c>Expr</c> returns
    /// <c>render()</c>. See the rationale at the use site in <c>TryDescribe</c>.
    ///
    /// <para><c>internal</c> so PassThroughFactoryDriftTests compares the DISCOVERED
    /// pass-through set against this one directly. A hand-copied list in the test would
    /// only guard the DSL surface, and would stay green if this set were edited.</para>
    /// </summary>
    internal static readonly ImmutableHashSet<string> PassThroughFactories =
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

        // Spec 010 — mechanism 1's failure mode. `[ReactorSourceTransparent]` is opt-in,
        // so the only feedback a consumer gets is whether attribution moved; on an
        // annotation the generator cannot honour, nothing moves and nothing says why.
        // This pass reports that case instead. It reads the ANNOTATED DECLARATIONS
        // rather than the call sites, so a method that is annotated but never called
        // still warns.
        var annotationProblems = context.SyntaxProvider.ForAttributeWithMetadataName(
                TransparentAttributeMetadataName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => DescribeAnnotationProblem(ctx, ct))
            .Where(static x => x is not null)
            .Collect();

        var input = callSites.Combine(enabled).Combine(needsPolyfill).Combine(pathMap);

        context.RegisterSourceOutput(input, static (spc, tuple) =>
        {
            var (((sites, isEnabled), polyfill), map) = tuple;
            if (!isEnabled || sites.IsDefaultOrEmpty) return;
            spc.AddSource("ReactorSourceMap.Interceptors.g.cs", Emit(sites!, polyfill, map));
        });

        // Gated on the same opt-in as the interceptors: in a compilation where the
        // generator emits nothing, the attribute is inert by design and a warning about
        // it would be noise.
        context.RegisterSourceOutput(annotationProblems.Combine(enabled), static (spc, tuple) =>
        {
            var (problems, isEnabled) = tuple;
            if (!isEnabled) return;
            foreach (var problem in problems)
            {
                spc.ReportDiagnostic(problem!.ToDiagnostic());
            }
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
        //
        // Argument stamping is bound to the SIGNATURE, not to the call, and therefore
        // also uses the open definition — see CouldHaveConvertedArguments. A reviewer
        // read that as a missed case (`Wrap<Element>("x")` really does have a constructed
        // `Element` parameter and a genuine user-defined conversion, both confirmed
        // against Roslyn 5.9) and it is not: the emitted interceptor declares that
        // parameter as `T __a0`, so writing an `Element?` back into it is
        // `CS1503: cannot convert from 'T' to 'Element?'`. Measured — switching the
        // filter to the constructed method makes the consumer's build fail. Declining
        // to stamp is what keeps the emitted code compiling; the element still gets a
        // location from the interceptor's own return-path stamp.
        method = method.OriginalDefinition;

        // Only the Reactor DSL surface, plus (spec 010 mechanism 1) any static
        // Element-returning method the author explicitly marked
        // `[ReactorSourceTransparent]`.
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
        // Corollary of the same filter: element-producing entry points that live
        // OUTSIDE Factories are likewise unstamped unless annotated —
        // `PendingFactory.Pending(...)` builds its element by calling
        // `Factories.Component<,>` from inside Reactor's own assembly, where there is no
        // call site in the consumer's compilation to intercept, and instance members such
        // as `IntlAccessor.RichMessage(...)` are already excluded by the IsStatic check
        // above. Both report no location rather than a framework line, and are pinned by
        // NonFactoriesEntryPointTests.
        //
        // The annotation is what makes the widening safe: it is a per-method assertion by
        // the author that the method's own line carries no information, so intercepting
        // the CALL to it is the attribution they want. Without it there is no way to tell
        // a thin forwarder from a `Render()` body, where the body line is correct.
        var compilation = ctx.SemanticModel.Compilation;
        var isFactory = method.ContainingType?.ToDisplayString() == FactoriesMetadataName;
        var isTransparentTarget = IsTransparent(method);

        if (!isFactory && !isTransparentTarget) return null;

        // Resolved once per candidate, and only AFTER the cheap filters above, so an
        // ordinary invocation never pays for it. Every "is this an Element" question below
        // goes through these symbols rather than a rendered type name — see ReturnsElement.
        if (compilation.GetTypeByMetadataName(ElementMetadataName) is not { } elementSymbol) return null;

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
        if (isFactory && PassThroughFactories.Contains(method.Name)) return null;

        // Mechanism 1 rule 2. An annotated method is only worth intercepting if the
        // emitted interceptor can actually forward to it by name; the diagnostic pass
        // reports the ones that cannot, so silence here is never the whole story.
        if (!isFactory && !IsForwardable(method, compilation, elementSymbol)) return null;

        // Generic factories (Component<T>, Component<T,TProps>, ForEach<T>, Memo<TKey>,
        // ListView<T>, …). An interceptor for a generic method has to restate the
        // type parameters AND their constraints; Roslyn accepts that (proven by
        // GenericFactoryInterceptorTests), so they are emitted rather than skipped.
        if (method.IsGenericMethod && method.TypeParameters.Any(HasUnrenderableConstraint))
            return null;

        // The interceptor has to be able to call the original with exactly the
        // arguments it received; by-ref parameters would need matching ref kinds
        // on both sides. Nothing in the DSL uses them today — bail rather than
        // emit something subtly wrong if that changes.
        if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return null;

        // Must return something we can stamp.
        if (!ReturnsElement(method.ReturnType, elementSymbol)) return null;

        // Mechanism 1 rule 1, and the whole of rule 3. A call written INSIDE a
        // transparent method is not stamped at all, because the element it produces
        // belongs to whoever called that method. Rule 2 then stamps it at that outer
        // call site. Applying this to transparent-target calls as well as factory calls
        // is what makes the behaviour recursive: transparent calling transparent keeps
        // deferring outward until it reaches a caller that is not annotated.
        //
        // Conditioned on the enclosing method being FORWARDABLE, not merely annotated:
        // if no rule-2 interceptor will exist to re-stamp at the caller, suppressing here
        // would trade today's "the helper's line" for "no line at all". An annotation the
        // generator cannot honour must never make attribution worse than no annotation.
        if (IsInsideTransparentMethod(ctx.SemanticModel, invocation, compilation, elementSymbol, ct)) return null;

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
        //
        // The position within that file is the argument list's OPENING PAREN, not the
        // start of the invocation. Roslyn derives [CallerLineNumber] from the open paren,
        // so for a call split across lines the two disagree: in
        //     Factories
        //         .TextBlock("x")
        // the invocation starts on the `Factories` line while CallerInfo reports the
        // `.TextBlock(` line. Measured both shapes against live CallerInfo probes,
        // including `Fact.TextBlock\n    ("y")` where the name and the paren land on
        // different lines and CallerInfo follows the PAREN, not the name.
        // MultilineCallSiteTests pins it.
        var parenSpan = invocation.ArgumentList.OpenParenToken.Span;
        var lineSpan = invocation.SyntaxTree.GetMappedLineSpan(parenSpan, ct);

        return new CallSite(
            attribute: location.GetInterceptsLocationAttributeSyntax(),
            filePath: ResolveMappedPath(lineSpan, invocation.SyntaxTree.FilePath),
            line: lineSpan.StartLinePosition.Line + 1,
            signature: Signature.From(method, elementSymbol, compilation.GetTypeByMetadataName(EmptyElementMetadataName)),
            argumentStamps: DescribeArgumentStamps(ctx, invocation, method, elementSymbol, ct));
    }

    // ── Mechanism 2: argument-position stamping ───────────────────────────

    /// <summary>
    /// Finds the arguments of this call that reached their <c>Element</c> parameter
    /// through an implicit <em>user-defined</em> conversion, and records the line each
    /// one was written on.
    ///
    /// <para><b>Why this exists.</b> <c>Element.cs</c> declares
    /// <c>implicit operator Element(string text) =&gt; Factories.TextBlock(text)</c>, so
    /// in <c>VStack("hi")</c> the child element is built by a <c>TextBlock</c> call
    /// inside Reactor's own assembly. That call site is not in the consumer's
    /// compilation, and it cannot be reached any other way: per the interceptors spec,
    /// "interception can only occur for calls to ordinary member methods — not
    /// constructors, delegates, properties, local functions, <em>operators</em>", and
    /// the operator's body is already compiled into Reactor.dll. The one place the
    /// consumer's line number is still available is the ENCLOSING call, which the
    /// generator is already intercepting — so the interceptor stamps the converted
    /// argument on its way past.</para>
    ///
    /// <para><b>Per argument, not per call.</b> The line recorded is the argument
    /// expression's own start line, so
    /// <code>
    /// VStack(
    ///     "a",   // reports this line
    ///     "b");  // and this one
    /// </code>
    /// does not collapse both children onto the <c>VStack(</c> line.</para>
    ///
    /// <para><b>Only compiler-built arrays are touched.</b> A <c>params</c> argument in
    /// expanded form is an array the compiler materialized at this call site, so the
    /// interceptor owns it and may write back into its slots. In normal form
    /// (<c>VStack(myArray)</c>) the array belongs to the caller — and, being an array of
    /// already-converted elements, carries no per-element conversions anyway — so
    /// nothing is emitted for it and no caller-visible array is ever mutated.</para>
    /// </summary>
    private static ImmutableArray<ArgumentStamp> DescribeArgumentStamps(
        GeneratorSyntaxContext ctx,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        INamedTypeSymbol elementSymbol,
        System.Threading.CancellationToken ct)
    {
        // Symbol-only pre-check before touching the operation tree. GetOperation binds a
        // full IOperation graph for the invocation, which is markedly more work than the
        // GetSymbolInfo this generator already does — and the overwhelming majority of DSL
        // calls (every `TextBlock(string)`, every modifier-shaped factory) have no
        // Element-typed parameter for a conversion to land on. Skipping those keeps the
        // per-call-site generation cost where layer 1 measured it.
        if (!CouldHaveConvertedArguments(method, elementSymbol))
            return ImmutableArray<ArgumentStamp>.Empty;

        if (ctx.SemanticModel.GetOperation(invocation, ct) is not IInvocationOperation operation)
            return ImmutableArray<ArgumentStamp>.Empty;

        ImmutableArray<ArgumentStamp>.Builder? builder = null;

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter is null) continue;
            var ordinal = argument.Parameter.Ordinal;

            if (argument.ArgumentKind == ArgumentKind.ParamArray)
            {
                // Expanded form. `IsImplicit` on the array creation is the load-bearing
                // check: it is what distinguishes the array the compiler built for THIS
                // call site (safe to write into) from one the caller handed over.
                if (argument.Value is not IArrayCreationOperation { Initializer: { } initializer } creation
                    || !creation.IsImplicit
                    || creation.Type is not IArrayTypeSymbol arrayType
                    || !IsElementItself(arrayType.ElementType, elementSymbol))
                {
                    continue;
                }

                for (int i = 0; i < initializer.ElementValues.Length; i++)
                {
                    if (TryDescribeConvertedArgument(initializer.ElementValues[i], ordinal, i, elementSymbol, ct) is { } stamp)
                    {
                        (builder ??= ImmutableArray.CreateBuilder<ArgumentStamp>()).Add(stamp);
                    }
                }
            }
            else if (argument.ArgumentKind == ArgumentKind.Explicit
                     && IsElementItself(argument.Parameter.Type, elementSymbol)
                     && TryDescribeConvertedArgument(argument.Value, ordinal, arrayIndex: -1, elementSymbol, ct) is { } single)
            {
                (builder ??= ImmutableArray.CreateBuilder<ArgumentStamp>()).Add(single);
            }
        }

        return builder is null ? ImmutableArray<ArgumentStamp>.Empty : builder.ToImmutable();
    }

    /// <summary>
    /// True when at least one parameter could receive an implicitly converted
    /// <c>Element</c>. Purely a cost filter for
    /// <see cref="DescribeArgumentStamps"/> — the per-argument analysis re-checks
    /// everything it depends on.
    ///
    /// <para>Deliberately asked of the OPEN definition. The emitted interceptor declares
    /// its parameters from the open signature, so a generic parameter is rendered as
    /// <c>T __a0</c> even when the call substitutes <c>T = Element</c>; stamping it would
    /// emit <c>CS1503: cannot convert from 'T' to 'Element?'</c> into the consumer's
    /// build. Answering from the constructed method therefore looks more thorough and is
    /// actively wrong — verified by building the suite with it switched over.</para>
    /// </summary>
    private static bool CouldHaveConvertedArguments(IMethodSymbol method, INamedTypeSymbol elementSymbol)
    {
        foreach (var parameter in method.Parameters)
        {
            if (IsElementItself(parameter.Type, elementSymbol)) return true;
            if (parameter.Type is IArrayTypeSymbol array && IsElementItself(array.ElementType, elementSymbol))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Describes one argument if — and only if — it reached <c>Element</c> through an
    /// implicit user-defined conversion. An argument that was already an
    /// element (<c>VStack(TextBlock("a"))</c>) is left alone: its own call site is
    /// intercepted directly and is the more precise answer.
    /// </summary>
    private static ArgumentStamp? TryDescribeConvertedArgument(
        IOperation value,
        int parameterOrdinal,
        int arrayIndex,
        INamedTypeSymbol elementSymbol,
        System.Threading.CancellationToken ct)
    {
        if (value is not IConversionOperation conversion) return null;
        if (!conversion.Conversion.IsUserDefined) return null;
        if (conversion.OperatorMethod is null) return null;

        // The operator's RESULT has to be exactly Element, because the emitted helper is
        // typed `Element?` in and `Element?` out and its return value is written back
        // into the argument slot. A hypothetical operator producing a derived record
        // would not round-trip through it.
        if (!IsElementItself(conversion.Type, elementSymbol)) return null;

        var operand = conversion.Operand;
        var tree = operand.Syntax.SyntaxTree;
        var lineSpan = tree.GetMappedLineSpan(operand.Syntax.Span, ct);

        return new ArgumentStamp(
            parameterOrdinal,
            arrayIndex,
            ResolveMappedPath(lineSpan, tree.FilePath),
            lineSpan.StartLinePosition.Line + 1);
    }

    /// <summary>
    /// True for <c>Element</c> exactly, false for a derived element record.
    ///
    /// <para>Compares SYMBOLS, not rendered names, for the reason spelled out on
    /// <see cref="ReturnsElement"/>: a <c>params Element?[]</c> parameter reports its
    /// element type as <c>Microsoft.UI.Reactor.Core.Element?</c>, and a string comparison
    /// against the bare metadata name silently misses every one of them.</para>
    /// </summary>
    private static bool IsElementItself(ITypeSymbol? type, INamedTypeSymbol elementSymbol)
        => type is not null && SymbolEqualityComparer.Default.Equals(type, elementSymbol);

    // ── Mechanism 1: transparent helpers ──────────────────────────────────

    private static bool IsTransparent(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == TransparentAttributeMetadataName)
                return true;
        }
        return false;
    }

    private static bool IsForwardable(IMethodSymbol method, Compilation compilation, INamedTypeSymbol elementSymbol)
        => ForwardabilityProblem(method, compilation, elementSymbol) is null;

    /// <summary>
    /// Why an interceptor cannot be emitted for calls to <paramref name="method"/>, or
    /// null when one can. The string is the tail of the
    /// <c>REACTOR_SOURCEMAP_001</c> message, so it reads as "…because {0}".
    ///
    /// <para>Shared by the discovery pass (which needs the boolean) and the diagnostic
    /// pass (which needs the reason), so the rule that silences the attribute and the
    /// rule that explains the silence cannot drift apart.</para>
    /// </summary>
    private static string? ForwardabilityProblem(
        IMethodSymbol method, Compilation compilation, INamedTypeSymbol elementSymbol)
    {
        if (method.MethodKind == MethodKind.LocalFunction)
            return "C# interceptors cannot intercept calls to local functions";
        if (method.MethodKind != MethodKind.Ordinary)
            return "C# interceptors can only intercept calls to ordinary methods";
        if (!method.IsStatic)
        {
            // Intercepting an instance method requires the interceptor to be an
            // extension method whose `this` parameter matches the receiver. Supportable,
            // but it is a separate shape from the static forwarding used here, so it is
            // an explicit gap rather than something that silently half-works.
            return "it is an instance method, and only static helpers are supported";
        }
        if (!ReturnsElement(method.ReturnType, elementSymbol))
            return "it does not return a Microsoft.UI.Reactor.Core.Element";
        if (method.Parameters.Any(p => p.RefKind != RefKind.None))
            return "it has a by-ref parameter";
        if (method.IsGenericMethod && method.TypeParameters.Any(HasUnrenderableConstraint))
            return "its type parameters cannot be restated on an interceptor";

        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsFileLocal)
                return "it is declared in a file-local type, which generated code cannot name";
            // "Interceptors cannot be declared in generic types at any level of nesting",
            // and an interceptor for a method in a generic type would additionally have to
            // restate the containing type's arity. Out of scope.
            if (type.IsGenericType)
                return "it is declared in a generic type";
        }

        // The interceptor lives in a generated file, so a private or protected member is
        // out of reach however the call site is written. Asking the compilation settles
        // internal-across-assemblies (InternalsVisibleTo) correctly too.
        if (!compilation.IsSymbolAccessibleWithin(method, compilation.Assembly))
            return "generated code cannot reach it; make it internal or public";

        return null;
    }

    /// <summary>
    /// True when <paramref name="node"/> sits inside a transparent method whose calls
    /// the generator will actually intercept.
    ///
    /// <para>Walks the SYMBOL chain rather than the syntax tree so lambdas and local
    /// functions defer outward for free: the enclosing symbol of a call inside
    /// <c>() =&gt; TextBlock("x")</c> is the lambda, whose containing symbol is the
    /// method that declared it. The walk stops at the type boundary, which is where
    /// "inside a method" stops meaning anything.</para>
    /// </summary>
    private static bool IsInsideTransparentMethod(
        SemanticModel model,
        SyntaxNode node,
        Compilation compilation,
        INamedTypeSymbol elementSymbol,
        System.Threading.CancellationToken ct)
    {
        for (var symbol = model.GetEnclosingSymbol(node.SpanStart, ct);
             symbol is not null;
             symbol = symbol.ContainingSymbol)
        {
            if (symbol is ITypeSymbol or INamespaceSymbol) return false;
            if (symbol is IMethodSymbol enclosing
                && IsTransparent(enclosing)
                && IsForwardable(enclosing, compilation, elementSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static AnnotationProblem? DescribeAnnotationProblem(
        GeneratorAttributeSyntaxContext ctx,
        System.Threading.CancellationToken ct)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method) return null;

        var compilation = ctx.SemanticModel.Compilation;
        if (compilation.GetTypeByMetadataName(ElementMetadataName) is not { } elementSymbol) return null;
        if (ForwardabilityProblem(method, compilation, elementSymbol) is not { } problem) return null;

        // Point at the attribute rather than the whole method: it is the attribute that
        // has no effect, and squiggling an entire method body would be disproportionate.
        //
        // A real syntax-tree Location, not Location.Create(path, span, lineSpan): only a
        // location that maps back into a tree in this compilation can be turned off with
        // `#pragma warning disable REACTOR_SOURCEMAP_001`. An external-file location would
        // produce a warning the consumer cannot suppress — and, because Release treats
        // warnings as errors, an unsuppressible build break on code that is deliberately
        // shaped that way. Holding a Location keeps its tree alive in the incremental
        // cache, which is the accepted cost; this pass yields nothing at all in the normal
        // case.
        var reference = ctx.Attributes.Length > 0 ? ctx.Attributes[0].ApplicationSyntaxReference : null;
        var location = reference is not null
            ? reference.GetSyntax(ct).GetLocation()
            : ctx.TargetNode.GetLocation();

        return new AnnotationProblem(location, method.Name, problem);
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

    /// <summary>
    /// True when <paramref name="type"/> is <c>Element</c> or derives from it.
    ///
    /// <para><b>Compares symbols, deliberately.</b> The obvious implementation walks
    /// <c>BaseType</c> comparing <c>ToDisplayString()</c> against the metadata name, and
    /// it is subtly wrong: the default display format <em>renders nullability</em>, so a
    /// method declared to return exactly <c>Element?</c> reports
    /// <c>Microsoft.UI.Reactor.Core.Element?</c>, never matches, and — because
    /// <c>Element</c>'s base is <c>object</c> — the walk goes straight past the answer.
    /// Such a method would be silently skipped. Measured directly against Roslyn 5.9:
    /// <c>Element?.BaseType</c> is <c>object</c>, while
    /// <c>SymbolEqualityComparer.Default.Equals(Element?, Element)</c> is <c>true</c>
    /// (<c>IncludeNullability</c> is <c>false</c>, which is the pair that proves Default
    /// ignores annotations rather than the two simply being the same symbol).</para>
    ///
    /// <para>A nullable DERIVED type is unaffected either way — <c>TextBlockElement?</c>
    /// has an unannotated <c>BaseType</c> — which is exactly why the bug stayed latent:
    /// no factory in the DSL returns bare <c>Element?</c> today. It stops being latent
    /// with <c>[ReactorSourceTransparent]</c>, where a consumer's conditional helper
    /// returning <c>Element?</c> is an entirely natural shape.</para>
    /// </summary>
    private static bool ReturnsElement(ITypeSymbol type, INamedTypeSymbol elementSymbol)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, elementSymbol)) return true;
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
        var needsArgumentHelper = sites.Any(static s => s is { ArgumentStamps.IsEmpty: false });

        foreach (var site in sites.Where(static s => s is not null))
        {
            var sig = site!.Signature;
            var mapped = ApplyPathMap(site.FilePath, pathMap);
            var name = $"__Reactor_{sig.MethodName}_{index}";
            var stamps = site.ArgumentStamps;

            sb.AppendLine($"        {site.Attribute}");
            sb.AppendLine($"        public static {sig.ReturnType} {name}{sig.TypeParameterList}({sig.ParameterList})");
            foreach (var clause in sig.ConstraintClauses)
                sb.AppendLine($"            {clause}");
            sb.AppendLine("        {");

            if (stamps.IsEmpty)
            {
                sb.AppendLine($"            var __e = {sig.OwnerType}.{sig.MethodName}{sig.TypeArgumentList}({sig.ArgumentList});");
                sb.AppendLine("            if (!global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled) return __e;");
            }
            else
            {
                // Arguments have to be stamped BEFORE the factory runs, because the
                // factory copies them into the element it builds — afterwards the array
                // slot is no longer what anyone reads. That forces the flag to be read
                // up front, and caching it in a local is not just tidier: the flag is a
                // mutable process-global, so two separate reads could straddle a write
                // and stamp the arguments of a call whose result is then left unstamped.
                sb.AppendLine("            var __on = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled;");
                sb.AppendLine("            if (__on)");
                sb.AppendLine("            {");
                foreach (var stamp in stamps)
                {
                    var target = stamp.ArrayIndex < 0
                        ? $"__a{stamp.ParameterOrdinal}"
                        : $"__a{stamp.ParameterOrdinal}[{stamp.ArrayIndex}]";
                    var stampPath = ApplyPathMap(stamp.FilePath, pathMap);
                    sb.AppendLine(
                        $"                {target} = __ReactorStampArgument({target}, {Literal(stampPath)}, {stamp.Line})!;");
                }
                sb.AppendLine("            }");
                sb.AppendLine($"            var __e = {sig.OwnerType}.{sig.MethodName}{sig.TypeArgumentList}({sig.ArgumentList});");
                sb.AppendLine("            if (!__on) return __e;");
            }

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
            // answer and this call site is not. It is also what keeps a transparent
            // helper from overwriting a location its own caller-supplied argument
            // already carries.
            sb.AppendLine("            if (__e.CallSite is not null) return __e;");
            // EmptyElement is a shared singleton (EmptyElement.Instance) that Mount
            // filters out before it ever becomes a control, so a location stamped here
            // could never be read back. Cloning it would be pure cost: it breaks the
            // singleton's reference identity AND materializes a 152-byte extras bucket
            // on every conditional-empty render — a hot path for Empty(), and for
            // factories like DevtoolsMenu that yield the sentinel when switched off.
            //
            // Emitted ONLY where the declared return type could actually be one. For a
            // factory returning a concrete record (TextBlockElement, …) the compiler
            // proves the test is always false and reports CS0184, which Release
            // promotes to an error via TreatWarningsAsErrors.
            if (sig.CanReturnEmpty)
            {
                sb.AppendLine("            if (__e is global::Microsoft.UI.Reactor.Core.EmptyElement) return __e;");
            }
            sb.AppendLine("            return __e with");
            sb.AppendLine("            {");
            sb.AppendLine($"                CallSite = new global::Microsoft.UI.Reactor.Core.SourceLocation({Literal(mapped)}, {site.Line})");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine();
            index++;
        }

        if (needsArgumentHelper)
        {
            // Emitted only when something calls it, so a project with no implicit
            // conversions in argument position gets a byte-identical file to before.
            sb.AppendLine("        /// <summary>Stamps an argument that reached its Element parameter through an implicit user-defined conversion.</summary>");
            sb.AppendLine("        private static global::Microsoft.UI.Reactor.Core.Element? __ReactorStampArgument(");
            sb.AppendLine("            global::Microsoft.UI.Reactor.Core.Element? __value, string __file, int __line)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (__value is null) return __value;");
            // First stamp wins, exactly as on the return path: an argument that already
            // knows where it came from is never relabelled by the call it is passed to.
            sb.AppendLine("            if (__value.CallSite is not null) return __value;");
            // Same singleton reasoning as the return path — and here it is not merely a
            // cost question: EmptyElement.Instance is shared process-wide, so cloning it
            // into an argument slot would hand a different instance to code that compares
            // by reference.
            sb.AppendLine("            if (__value is global::Microsoft.UI.Reactor.Core.EmptyElement) return __value;");
            sb.AppendLine("            return __value with");
            sb.AppendLine("            {");
            sb.AppendLine("                CallSite = new global::Microsoft.UI.Reactor.Core.SourceLocation(__file, __line)");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine();
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
        public CallSite(string attribute, string filePath, int line, Signature signature,
                        ImmutableArray<ArgumentStamp> argumentStamps)
        {
            Attribute = attribute;
            FilePath = filePath;
            Line = line;
            Signature = signature;
            ArgumentStamps = argumentStamps;
        }

        public string Attribute { get; }
        public string FilePath { get; }
        public int Line { get; }
        public Signature Signature { get; }

        /// <summary>
        /// Arguments of this call that need stamping in place before the real factory
        /// runs. Empty for the overwhelming majority of call sites.
        /// </summary>
        public ImmutableArray<ArgumentStamp> ArgumentStamps { get; }

        public bool Equals(CallSite? other)
            => other is not null
               && Attribute == other.Attribute
               && FilePath == other.FilePath
               && Line == other.Line
               && Signature.Equals(other.Signature)
               && ArgumentStamps.SequenceEqual(other.ArgumentStamps);

        public override bool Equals(object? obj) => Equals(obj as CallSite);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Attribute.GetHashCode();
                h = (h * 397) ^ FilePath.GetHashCode();
                h = (h * 397) ^ Line;
                h = (h * 397) ^ Signature.GetHashCode();
                foreach (var stamp in ArgumentStamps) h = (h * 397) ^ stamp.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>
    /// One argument of an intercepted call that reached its <c>Element</c> parameter
    /// through an implicit user-defined conversion, plus the position it was written at.
    ///
    /// <para><see cref="ArrayIndex"/> is <c>-1</c> for an ordinary parameter and a slot
    /// index for a <c>params</c> argument in expanded form; the two render as
    /// <c>__a2</c> and <c>__a0[1]</c> respectively.</para>
    /// </summary>
    private sealed class ArgumentStamp : IEquatable<ArgumentStamp>
    {
        public ArgumentStamp(int parameterOrdinal, int arrayIndex, string filePath, int line)
        {
            ParameterOrdinal = parameterOrdinal;
            ArrayIndex = arrayIndex;
            FilePath = filePath;
            Line = line;
        }

        public int ParameterOrdinal { get; }
        public int ArrayIndex { get; }
        public string FilePath { get; }
        public int Line { get; }

        public bool Equals(ArgumentStamp? other)
            => other is not null
               && ParameterOrdinal == other.ParameterOrdinal
               && ArrayIndex == other.ArrayIndex
               && FilePath == other.FilePath
               && Line == other.Line;

        public override bool Equals(object? obj) => Equals(obj as ArgumentStamp);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = ParameterOrdinal;
                h = (h * 397) ^ ArrayIndex;
                h = (h * 397) ^ FilePath.GetHashCode();
                return (h * 397) ^ Line;
            }
        }
    }

    /// <summary>
    /// A <c>[ReactorSourceTransparent]</c> annotation the generator cannot honour.
    /// </summary>
    private sealed class AnnotationProblem : IEquatable<AnnotationProblem>
    {
        public AnnotationProblem(Location location, string methodName, string reason)
        {
            Location = location;
            MethodName = methodName;
            Reason = reason;
        }

        public Location Location { get; }
        public string MethodName { get; }
        public string Reason { get; }

        public Diagnostic ToDiagnostic()
            => Diagnostic.Create(UnusableTransparentAnnotation, Location, MethodName, Reason);

        public bool Equals(AnnotationProblem? other)
            => other is not null
               && Location.Equals(other.Location)
               && MethodName == other.MethodName
               && Reason == other.Reason;

        public override bool Equals(object? obj) => Equals(obj as AnnotationProblem);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Location.GetHashCode();
                h = (h * 397) ^ MethodName.GetHashCode();
                return (h * 397) ^ Reason.GetHashCode();
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
                          bool canReturnEmpty,
                          string typeParameterList, string typeArgumentList, ImmutableArray<string> constraintClauses)
        {
            CanReturnEmpty = canReturnEmpty;
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

        /// <summary>
        /// True when the declared return type could actually be an <c>EmptyElement</c> —
        /// i.e. the base <c>Element</c> or <c>EmptyElement</c> itself. A concrete element
        /// record cannot be, and testing for it would be CS0184 (error in Release).
        /// </summary>
        public bool CanReturnEmpty { get; }

        /// <summary><c>&lt;T, TProps&gt;</c> on the interceptor declaration, or empty.</summary>
        public string TypeParameterList { get; }

        /// <summary><c>&lt;T, TProps&gt;</c> on the forwarding call, or empty.</summary>
        public string TypeArgumentList { get; }

        /// <summary>One rendered <c>where …</c> clause per constrained type parameter.</summary>
        public ImmutableArray<string> ConstraintClauses { get; }

        public static Signature From(
            IMethodSymbol method, INamedTypeSymbol elementSymbol, INamedTypeSymbol? emptyElementSymbol)
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

            // Only the base Element (or EmptyElement itself) can hold an EmptyElement at
            // runtime; a concrete element record provably cannot, and testing for it is
            // CS0184 — a warning that Release turns into a build error.
            //
            // Symbol comparison, for the same reason ReturnsElement uses it: a return type
            // of exactly `Element?` renders as "…Element?" and would miss a name match, so
            // a nullable-Element-returning factory would lose its EmptyElement guard and
            // clone the shared singleton.
            var returnType = method.ReturnType.OriginalDefinition;
            var canReturnEmpty = SymbolEqualityComparer.Default.Equals(returnType, elementSymbol)
                || (emptyElementSymbol is not null
                    && SymbolEqualityComparer.Default.Equals(returnType, emptyElementSymbol));

            return new Signature(
                method.ContainingType.ToDisplayString(s_typeFormat),
                method.Name,
                method.ReturnType.ToDisplayString(s_typeFormat),
                string.Join(", ", parameters),
                string.Join(", ", arguments),
                method.ReturnType.NullableAnnotation == NullableAnnotation.Annotated,
                canReturnEmpty,
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




