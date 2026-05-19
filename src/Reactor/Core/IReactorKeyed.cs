namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Identity-on-data convention for items rendered into Reactor collection
/// elements (<see cref="TemplatedListElementBase"/> and
/// <see cref="LazyStackElementBase"/>) and into hand-built keyed children
/// (<c>FlexColumn(items.Select(... .WithKey(item)))</c>).
/// </summary>
/// <remarks>
/// <para>
/// When a data type implements <see cref="IReactorKeyed"/>, collection
/// elements default their <c>KeySelector</c> to <c>t =&gt; t.Key</c> and a
/// <c>WithKey&lt;T, TKey&gt;(this T el, TKey item) where T : Element where TKey : IReactorKeyed</c>
/// overload becomes available (the open <c>T</c> preserves the element's
/// concrete type through the fluent chain). Explicit <c>KeySelector</c> and
/// <c>WithKey(string)</c> continue to work for types you do not own.
/// </para>
/// <para>
/// The returned <see cref="Key"/> value must be:
/// <list type="bullet">
/// <item><description><b>Stable</b> across re-renders for the lifetime of
/// the item — using a value that changes (e.g. row index) defeats keyed
/// reconciliation and produces the same visual churn as having no key.</description></item>
/// <item><description><b>Unique</b> within the containing list — duplicate
/// keys trigger a bulk-replace bailout and a one-shot diagnostic.</description></item>
/// <item><description><b>Non-null</b> — a null key is treated as a developer
/// bug and bails out the diff for the affected list.</description></item>
/// </list>
/// </para>
/// <para>
/// See spec 042 §3 (the unified identity model) and §5 (identity-on-data
/// convention) for the design rationale.
/// </para>
/// </remarks>
public interface IReactorKeyed
{
    /// <summary>
    /// Stable, unique, non-null identifier for this item within the list it
    /// belongs to. Read at every reconcile pass; must be cheap to compute
    /// (ideally a field read, not a derived value).
    /// </summary>
    string Key { get; }
}
