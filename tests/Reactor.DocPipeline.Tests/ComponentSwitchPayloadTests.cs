using System.Text.Json;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Covers <see cref="ScreenshotCapture.BuildComponentSwitchPayload"/>.
/// </summary>
/// <remarks>
/// <para>
/// The component name is manifest-authored, the same trust level as the
/// screenshot id that reaches the filesystem a few lines later. That id is
/// contained by <c>DocPaths.ResolveContained</c>; this is the same value class
/// flowing into a different structured sink, and until now it was concatenated
/// into JSON rather than encoded into it.
/// </para>
/// <para>
/// The reason it matters here specifically: every other guard added for issue
/// #989 asks whether the captured frame was <em>painted</em>. None of them asks
/// whether it is the frame that was <em>requested</em>. A switch that silently
/// targets the wrong component yields a real, content-bearing screenshot that
/// sails through all of them and overwrites a committed asset — the exact
/// outcome this change exists to prevent, reached by the one route the blank
/// gates cannot see.
/// </para>
/// </remarks>
public class ComponentSwitchPayloadTests
{
    [Theory]
    [InlineData("CounterDemo")]
    [InlineData("My\"Thing")]
    [InlineData("Back\\Slash")]
    [InlineData("Two\nLines")]
    [InlineData("A\", \"component\": \"B")]
    [InlineData("Ünïcødé")]
    public void Payload_round_trips_the_component_name_exactly(string component)
    {
        var payload = ScreenshotCapture.BuildComponentSwitchPayload(component);

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(component, doc.RootElement.GetProperty("component").GetString());

        // One property, not two: the injection case is only interesting because
        // it can *add* a key, and a value-equality check alone would not notice
        // a second one riding along.
        Assert.Equal(1, CountRootProperties(doc));
    }

    /// <summary>
    /// Pins the hazard rather than this file's model of it, by reproducing the
    /// expression that was replaced and showing it fails on the same inputs.
    /// </summary>
    /// <remarks>
    /// Without this the theory above would pass against any encoder that
    /// happened to be correct, and would keep passing if the production call
    /// site were reverted to interpolation — because nothing here would still
    /// be looking at interpolation.
    /// </remarks>
    [Fact]
    public void The_old_interpolation_is_the_hazard_this_pins()
    {
        // Loud arm: a bare quote produces a body the server cannot parse, so it
        // answers 400 and CaptureAsync counts a failure. Bad, but visible.
        Assert.ThrowsAny<JsonException>(
            () => JsonDocument.Parse(InterpolateAsTheOldCodeDid("My\"Thing")));

        // Quiet arm, and the one that motivates the fix: this shape is *valid*
        // JSON, so the switch succeeds — against a component the manifest never
        // named — and capture proceeds normally from there.
        const string injected = "A\", \"component\": \"B";
        using var stale = JsonDocument.Parse(InterpolateAsTheOldCodeDid(injected));
        Assert.NotEqual(injected, stale.RootElement.GetProperty("component").GetString());

        // The replacement is not merely different from the old one, it is right.
        using var now = JsonDocument.Parse(
            ScreenshotCapture.BuildComponentSwitchPayload(injected));
        Assert.Equal(injected, now.RootElement.GetProperty("component").GetString());
    }

    /// <summary>
    /// The replaced expression, verbatim. Kept as a private helper so the
    /// analyser has nothing to "fix" at a live call site, and so a reader can
    /// see exactly what changed without consulting history.
    /// </summary>
    private static string InterpolateAsTheOldCodeDid(string component) =>
        $"{{\"component\":\"{component}\"}}";

    private static int CountRootProperties(JsonDocument doc)
    {
        var n = 0;
        foreach (var _ in doc.RootElement.EnumerateObject()) n++;
        return n;
    }
}
