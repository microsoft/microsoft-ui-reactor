using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Pins the catch list of the shared fixture teardown.
/// </summary>
public class FixtureCleanupTests
{
    /// <summary>
    /// A read-only entry makes <c>Directory.Delete</c> throw
    /// <c>UnauthorizedAccessException</c>, which is not an <c>IOException</c> — so
    /// this is the case the three original per-fixture copies silently let escape,
    /// turning a teardown detail into a suite failure about nothing.
    /// </summary>
    /// <remarks>
    /// Non-vacuous by construction: removing the
    /// <c>UnauthorizedAccessException</c> arm from
    /// <see cref="FixtureCleanup.DeleteTree"/> makes this fail. The
    /// <c>Assert.Throws</c> above the call is the control — without it a future
    /// platform change that stopped throwing would leave the test passing while
    /// exercising nothing, which is the same shape this PR exists to close.
    /// </remarks>
    [Fact]
    public void Teardown_survives_a_read_only_entry()
    {
        var root = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(),
            "reactor-cleanup-" + global::System.Guid.NewGuid().ToString("N"));
        global::System.IO.Directory.CreateDirectory(root);
        var file = global::System.IO.Path.Join(root, "locked.txt");
        global::System.IO.File.WriteAllText(file, "x");
        global::System.IO.File.SetAttributes(file, global::System.IO.FileAttributes.ReadOnly);

        try
        {
            // Control: the tree really is in the state that used to escape. If this
            // stops throwing, the case below is no longer being exercised and the
            // test would otherwise pass for the wrong reason.
            Assert.Throws<global::System.UnauthorizedAccessException>(
                () => global::System.IO.Directory.Delete(root, recursive: true));

            FixtureCleanup.DeleteTree(root);
        }
        finally
        {
            global::System.IO.File.SetAttributes(file, global::System.IO.FileAttributes.Normal);
            global::System.IO.Directory.Delete(root, recursive: true);
        }
    }
}
