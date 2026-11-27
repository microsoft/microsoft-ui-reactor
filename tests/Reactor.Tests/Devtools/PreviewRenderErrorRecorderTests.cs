using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// The docs screenshot guard as the preview host wires it: render errors reach the recorder
/// through <see cref="RenderErrorDispatch.Observer"/> and come out on <c>/status</c>.
/// </summary>
[Collection("RenderErrorGlobals")]
public sealed class PreviewRenderErrorRecorderTests
{
    private const string Token = "test-token";

    private sealed record ProbeElement(string Text) : Element;

    private static RenderError Error(string message, string? component = "Alert") =>
        new(new NullReferenceException(message), RenderErrorSource.ComponentRender, component, isHostLevel: false);

    [Fact]
    public void A_Switch_Is_Pending_Until_It_Renders_And_The_First_Error_Wins()
    {
        var recorder = new PreviewRenderErrorRecorder();
        Assert.Null(recorder.Current);

        // Pending from the moment the switch starts; a render of the outgoing component
        // (before Arm) neither settles it nor records its errors.
        var first = recorder.Reset();
        recorder.Settle();
        recorder.Record(Error("outgoing"));
        Assert.Equal(PreviewRenderErrorRecorder.PendingDescription, recorder.Current);

        // Once armed, the next completed render settles it.
        recorder.Arm(first);
        recorder.Settle();
        Assert.Null(recorder.Current);

        // An error after arming wins over pending, and later errors and renders keep it.
        var second = recorder.Reset();
        recorder.Arm(second);
        recorder.Record(Error("first"));
        recorder.Record(Error("second", component: null));
        recorder.Settle();
        Assert.Equal("ComponentRender in Alert: NullReferenceException: first", recorder.Current);

        // Two switches whose UI callbacks run in reverse order: the newer arms and mounts,
        // the superseded one is told not to mount, and only the newer one's render settles.
        var third = recorder.Reset();
        var fourth = recorder.Reset();
        Assert.True(recorder.Arm(fourth));
        Assert.False(recorder.Arm(third));
        Assert.Equal(PreviewRenderErrorRecorder.PendingDescription, recorder.Current);
        recorder.Settle();
        Assert.Null(recorder.Current);

        // A superseded callback that runs first is rejected and arms nothing.
        var fifth = recorder.Reset();
        var sixth = recorder.Reset();
        Assert.False(recorder.Arm(fifth));
        recorder.Settle();
        Assert.Equal(PreviewRenderErrorRecorder.PendingDescription, recorder.Current);
        Assert.True(recorder.Arm(sixth));
        recorder.Settle();
        Assert.Null(recorder.Current);
    }

    [Fact]
    public async Task Installed_Recorder_Reports_A_Dispatched_Error_On_Status()
    {
        var previous = RenderErrorDispatch.Observer;
#pragma warning disable IL2026
        using var server = PreviewCaptureServer.CreateForTests(Token);
#pragma warning restore IL2026
        try
        {
            var recorder = PreviewRenderErrorRecorder.Install();
            recorder.AttachTo(server);
            server.Start();
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

            Assert.Equal(JsonValueKind.Null, (await RenderError(client)).ValueKind);

            RenderErrorDispatch.BuildInTreeFallback(_ => new ProbeElement("fallback"), Error("boom"), logger: null);

            Assert.Equal("ComponentRender in Alert: NullReferenceException: boom", (await RenderError(client)).GetString());
        }
        finally
        {
            RenderErrorDispatch.Observer = previous;
        }
    }

    private static async Task<JsonElement> RenderError(HttpClient client)
    {
        using var response = await client.GetAsync("/status", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("renderError").Clone();
    }
}
