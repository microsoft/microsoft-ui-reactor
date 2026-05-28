namespace Microsoft.UI.Reactor.Core.Internal;

/// <summary>
/// Spec 047 §14 Phase 3 close-out — uniform per-index view-source contract
/// consumed by the shared templated-items realization machinery
/// (<see cref="Reconciler.HandleTemplatedContainerContentChanging"/> and
/// <see cref="Reconciler.RefreshRealizedContainers"/>).
///
/// <para>Two production implementations exist:</para>
/// <list type="bullet">
///   <item><see cref="TemplatedListElementBase"/> — the legacy
///   Element-based path. Each templated-list element directly answers
///   "how many items, what does item N look like."</item>
///   <item>A strategy-side adapter constructed by the descriptor binder
///   when a <c>TemplatedItems&lt;TItem,TElement,TControl&gt;</c> Children
///   strategy is in effect. The adapter closes over the strategy's
///   <c>GetItems</c> + <c>BuildItemView</c> lambdas, refreshed on each
///   Mount/Update so the realization path always sees the live element's
///   data.</item>
/// </list>
///
/// <para>The realization helpers prefer the stashed view-source on the
/// host control (<see cref="Reconciler.GetItemViewSource"/>) over the
/// legacy <c>TemplatedListElementBase</c> path so descriptor-driven
/// controls don't need to inherit from the legacy abstract base.</para>
/// </summary>
internal interface IItemViewSource
{
    int ItemCount { get; }
    Element BuildItemView(int index);
}
