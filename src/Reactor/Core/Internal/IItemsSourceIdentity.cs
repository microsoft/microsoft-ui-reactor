namespace Microsoft.UI.Reactor.Core.Internal;

/// <summary>
/// The item list an items-host element renders, compared by reference to tell whether an update
/// handed the host a different list (the perf provider's <c>changes</c> count, issue #1538).
/// </summary>
internal interface IItemsSourceIdentity
{
    object ItemsSource { get; }
}
