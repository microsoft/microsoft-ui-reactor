namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Teardown for the temp trees the doc-pipeline fixtures build, shared by every
/// fixture that owns one.
/// </summary>
internal static class FixtureCleanup
{
    /// <summary>
    /// Deletes a fixture's temp tree, swallowing the failures that mean "something
    /// else is holding this right now" rather than "the test found a bug".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The catch list is the whole point of this existing. Three fixtures each
    /// carried their own copy catching only <see cref="global::System.IO.IOException"/>
    /// under a <c>best effort</c> comment, and
    /// <see cref="global::System.IO.Directory.Delete(string, bool)"/> also throws
    /// <see cref="global::System.UnauthorizedAccessException"/> — for a read-only
    /// file, or a handle held by AV or the search indexer — which does **not**
    /// derive from <c>IOException</c>. Measured, not assumed: deleting a tree
    /// containing one read-only file throws <c>UnauthorizedAccessException</c>,
    /// and <c>catch (IOException)</c> does not catch it.
    /// </para>
    /// <para>
    /// So each copy promised best-effort and delivered a teardown that could fail
    /// the suite for a reason no test in it is about. One implementation means the
    /// next exception type anyone discovers gets added once instead of twice —
    /// three copies of a catch list that just proved incomplete is how the next
    /// one goes unnoticed in two of them.
    /// </para>
    /// </remarks>
    internal static void DeleteTree(string root)
    {
        try
        {
            global::System.IO.Directory.Delete(root, recursive: true);
        }
        catch (global::System.IO.IOException)
        {
            // Locked by another handle — nothing this suite asserts.
        }
        catch (global::System.UnauthorizedAccessException)
        {
            // Read-only entry, or AV/indexer holding it — likewise.
        }
    }
}
