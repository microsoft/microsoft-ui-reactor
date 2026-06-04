namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

/// <summary>
/// Suppresses the REACTOR0050 analyzer for a descriptor field or property whose
/// <c>OneWay</c> entries intentionally use <see cref="Optional{T}"/> without a
/// dependency-property backing value to clear.
/// </summary>
/// <remarks>
/// Prefer providing the <c>dp:</c> argument to <c>ControlDescriptor.OneWay</c>
/// whenever the target WinUI property is backed by a dependency property. Use
/// this attribute only for descriptor entries whose unset value should skip the
/// write because there is no dependency property, or because clearing the local
/// value would be semantically wrong for that control.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NoClearValueAttribute : Attribute
{
}
