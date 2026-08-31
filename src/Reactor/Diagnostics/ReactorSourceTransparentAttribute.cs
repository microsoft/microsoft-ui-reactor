using System;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// Spec 010 — marks a helper method as <em>source-transparent</em>: elements it
/// returns are attributed to the line that CALLED it, not to the line inside it
/// that ran the DSL factory.
///
/// <para>Without this, a thin forwarding helper collapses every one of its call
/// sites onto a single line, because an interceptor replaces the <em>call
/// site</em> and the call site of <c>TextBlock(...)</c> in
/// <c>static Element MyHeader() =&gt; TextBlock("header");</c> is inside
/// <c>MyHeader</c>. Annotating <c>MyHeader</c> flips the attribution outward:
/// the source-map generator emits no interceptor for the factory calls in its
/// body, and instead intercepts calls <em>to</em> <c>MyHeader</c> and stamps the
/// caller's line.</para>
///
/// <para><b>Deliberately opt-in.</b> It is not a blanket rule for helpers,
/// because for most element-returning methods the body line is the RIGHT answer
/// — a <c>Component.Render()</c> body is where the author actually wrote the UI,
/// and deferring it to the reconciler's call site would be a regression. Only a
/// thin forwarder, whose own line carries no information a reader wants, benefits.
/// This attribute is how that intent is expressed.</para>
///
/// <para><b>Nesting.</b> Annotated methods compose: a transparent helper calling
/// another transparent helper keeps deferring outward until it reaches a caller
/// that is not annotated, and that caller's line is what gets stamped.</para>
///
/// <example>
/// <code>
/// [ReactorSourceTransparent]
/// internal static Element Field(string label, string value)
///     =&gt; HStack(TextBlock(label), TextBlock(value));
///
/// // Both rows report THEIR OWN line, instead of both reporting the
/// // `HStack(` line inside Field.
/// var form = VStack(
///     Field("Name", name),
///     Field("Email", email));
/// </code>
/// </example>
/// </summary>
/// <remarks>
/// <para>
/// <b>Requirements.</b> The generator has to be able to emit an interceptor that
/// forwards to the annotated method, so it must be <see langword="static"/>,
/// return an <c>Element</c>, and be reachable by name from a generated file in
/// the same compilation — i.e. <see langword="public"/> or
/// <see langword="internal"/> (never <see langword="private"/>), not declared in
/// a <c>file</c>-local or generic type, and not a local function (C# interceptors
/// cannot intercept those). An annotation the generator cannot honour is reported
/// as <c>REACTOR_SOURCEMAP001</c> rather than silently doing nothing, and
/// attribution falls back to today's behaviour — the helper's own line — so a bad
/// annotation never makes attribution worse than no annotation.
/// </para>
/// <para>
/// <b>Where it takes effect.</b> Rule 1 (suppressing stamps inside the body)
/// applies in the compilation that <em>declares</em> the method; rule 2 (stamping
/// at the call site) applies in the compilation that <em>calls</em> it. Both need
/// the source-map generator loaded, so annotating a method in a library whose
/// consumer has not enabled <c>ReactorSourceMap</c> simply leaves those elements
/// unstamped.
/// </para>
/// <para>
/// A plain marker attribute with no members and no reflection surface, so it is
/// trim- and AOT-safe. It is read by the generator from metadata, which is why it
/// must be <see langword="public"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ReactorSourceTransparentAttribute : Attribute
{
}
