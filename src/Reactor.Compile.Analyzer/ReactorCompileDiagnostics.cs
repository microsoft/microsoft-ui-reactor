using Microsoft.CodeAnalysis;

namespace Microsoft.UI.Reactor.Compile.Analyzer;

/// <summary>
/// Spec 047 §13 Q10 / §14 Phase 1 (1.10) — the diagnostics shipped by
/// <c>Reactor.Compile.Analyzer</c>.
///
/// <para><see cref="CustomEventDelegateType"/> (REACTOR1002) is the active
/// compile-time validation rule, checking that the <c>TArgs</c> of a
/// <c>ReactorBinding&lt;T&gt;.OnCustomEvent&lt;TArgs&gt;</c> call matches the
/// <c>EventArgs</c> of the event wired in its subscribe/unsubscribe lambdas.</para>
///
/// <para>The provisional REACTOR1001 (string-form event reference) and
/// REACTOR1003 (<c>Prop.Controlled</c> read-back type) placeholders were
/// <b>retired in Phase 4 (§4.7)</b>: the final descriptor API is fully
/// strongly-typed — events wire via typed <c>subscribe</c> lambdas (no string
/// event names) and <c>Controlled&lt;TValue, TArgs&gt;</c> unifies the <c>set</c>
/// and <c>readBack</c> value type so the compiler already rejects a mismatch.
/// Neither rule had a source pattern left to match.</para>
/// </summary>
internal static class ReactorCompileDiagnostics
{
    /// <summary>
    /// Diagnostic category — rules live under the same category so consumers
    /// can suppress / escalate them as a group.
    /// </summary>
    public const string Category = "Reactor.Compile";

    /// <summary>
    /// REACTOR1002 — the <c>TArgs</c> of a
    /// <c>ReactorBinding&lt;T&gt;.OnCustomEvent&lt;TArgs&gt;</c> call does
    /// not match the <c>EventArgs</c> type of an event subscribed via
    /// <c>+=</c> / <c>-=</c> inside its <c>subscribe</c> / <c>unsubscribe</c>
    /// lambdas. Active in Phase 1.
    /// </summary>
    public static readonly DiagnosticDescriptor CustomEventDelegateType = new(
        id: "REACTOR1002",
        title: "OnCustomEvent TArgs does not match the subscribed event's EventArgs",
        messageFormat:
            "OnCustomEvent<{0}> is wired to event '{1}' whose handler takes '{2}'. " +
            "Either change TArgs to '{2}' or wrap the handler in a delegate " +
            "creation expression (e.g. `new RoutedEventHandler(h)`) inside " +
            "the subscribe/unsubscribe lambdas.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "ReactorBinding<TElement>.OnCustomEvent<TArgs>(subscribe, " +
            "unsubscribe, handler) requires the EventArgs of every event " +
            "subscribed inside the subscribe/unsubscribe lambdas to match " +
            "TArgs. When the event uses a non-generic delegate type " +
            "(e.g. RoutedEventHandler), authors must explicitly wrap the " +
            "handler parameter in a delegate creation expression — the " +
            "wrapped form is accepted and only the unwrapped/mismatched " +
            "form is flagged.");
}
