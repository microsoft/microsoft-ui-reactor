using System.Text.Json.Serialization;

namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// A snapshot of a <see cref="NavigationHandle{TRoute}"/>'s full state: the back stack,
/// the currently active route, and the forward stack. Obtained from
/// <see cref="NavigationHandle{TRoute}.GetState"/> and restored via
/// <see cref="NavigationHandle{TRoute}.SetState"/>.
/// </summary>
/// <remarks>
/// Reactor intentionally does not pick a serialization format for navigation state.
/// This record is a plain POCO that callers can persist however they like — JSON via
/// <c>System.Text.Json</c> (preferably with a <see cref="JsonSerializerContext"/> for
/// AOT-safety), MessagePack, BinaryFormatter (don't), or by hand. The
/// <c>[JsonPropertyName]</c> attributes are metadata only — they give callers nice
/// camelCase JSON for free if they choose JSON, but impose no runtime cost otherwise.
/// </remarks>
public sealed record NavigationState<TRoute>(
    [property: JsonPropertyName("backStack")] IReadOnlyList<TRoute> BackStack,
    [property: JsonPropertyName("current")] TRoute Current,
    [property: JsonPropertyName("forwardStack")] IReadOnlyList<TRoute> ForwardStack)
    where TRoute : notnull;
