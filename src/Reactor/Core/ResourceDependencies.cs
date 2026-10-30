namespace Microsoft.UI.Reactor.Core;

internal static class ResourceDependencies
{
    // Keep element-wise object.Equals semantics aligned with RenderContext.DepsEqual.
    // A null previous array means no dependency snapshot has been recorded.
    internal static bool DepsEqual(object[]? previous, object[] next)
    {
        if (previous is null || previous.Length != next.Length) return false;
        if (ReferenceEquals(previous, next)) return true;

        for (var i = 0; i < previous.Length; i++)
            if (!Equals(previous[i], next[i])) return false;
        return true;
    }
}
