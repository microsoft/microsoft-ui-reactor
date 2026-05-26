using Microsoft.CodeAnalysis;

namespace Microsoft.UI.Reactor.Compile.Analyzer;

/// <summary>
/// Spec 047 §13 Q10 / §14 Phase 1 (1.10) — the three diagnostics shipped
/// by <c>Reactor.Compile.Analyzer</c>.
///
/// Two of the three (<see cref="StringEventReference"/> and
/// <see cref="ControlledReadBackType"/>) are <em>provisional</em>: their
/// rule bodies are no-ops until Phase 2's descriptor model
/// (<c>Prop.Controlled(..., changeEvent: nameof(...))</c>) lands. The
/// descriptors are reserved here so the IDs are stable across phases.
///
/// <see cref="CustomEventDelegateType"/> is concrete and active in Phase 1
/// against <c>ReactorBinding&lt;T&gt;.OnCustomEvent&lt;TArgs&gt;</c>.
/// </summary>
internal static class ReactorCompileDiagnostics
{
    /// <summary>
    /// Diagnostic category — all three rules live under the same category
    /// so consumers can suppress / escalate them as a group.
    /// </summary>
    public const string Category = "Reactor.Compile";

    /// <summary>
    /// REACTOR1001 — string-form event reference does not resolve to a
    /// public event on the declared control type. Provisional: activates
    /// in Phase 2 when the descriptor model ships.
    /// </summary>
    public static readonly DiagnosticDescriptor StringEventReference = new(
        id: "REACTOR1001",
        title: "String-form event reference does not resolve on the control type",
        messageFormat: "Event '{0}' is not a public event on '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Reactor descriptors that take a string event name (e.g. " +
            "`Prop.Controlled(..., changeEvent: \"Toggled\")`) must name a " +
            "public event declared on the control type captured by the " +
            "descriptor's generic parameter. This rule activates when the " +
            "Phase 2 descriptor model lands; today it is a no-op placeholder " +
            "so the ID is reserved.");

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

    /// <summary>
    /// REACTOR1003 — <c>Prop.Controlled</c>'s <c>readBack</c> return type
    /// does not match the <c>set</c> lambda's value type. Provisional:
    /// activates in Phase 2 when the descriptor model ships.
    /// </summary>
    public static readonly DiagnosticDescriptor ControlledReadBackType = new(
        id: "REACTOR1003",
        title: "Prop.Controlled readBack return type does not match set lambda value type",
        messageFormat:
            "Prop.Controlled has readBack returning '{0}' but the set lambda " +
            "takes '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The Phase 2 descriptor `Prop.Controlled(getValue, set, readBack)` " +
            "requires readBack's return type to match the value type accepted " +
            "by set. This rule activates when the Phase 2 descriptor model " +
            "lands; today it is a no-op placeholder so the ID is reserved.");
}
