using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

[Collection("ConsoleTests")]
public sealed class PreviewCaptureServer_EmbedTests
{
    private const string Token = "test-token";

    [Fact]
    public async Task Status_ReportsEmbedV1Protocol()
    {
        using var h = ServerHarness.Start();

        using var response = await h.Client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJson(response);
        Assert.False(json.RootElement.GetProperty("building").GetBoolean());
        Assert.Equal(10, json.RootElement.GetProperty("fps").GetInt32());
        Assert.Equal(h.Server.Port, json.RootElement.GetProperty("port").GetInt32());
        Assert.Equal("embed-v1", json.RootElement.GetProperty("protocol").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("generation").GetInt32());
    }

    [Fact]
    public async Task Hwnd_RequiresBearerToken()
    {
        using var h = ServerHarness.Start();
        using var client = new HttpClient { BaseAddress = h.Client.BaseAddress };

        using var response = await client.GetAsync("/hwnd");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hwnd_RejectsExternalHostHeader()
    {
        using var h = ServerHarness.Start();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/hwnd");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Host = $"evil.com:{h.Server.Port}";

        using var response = await h.Client.SendAsync(request);

        Assert.Equal((HttpStatusCode)421, response.StatusCode);
    }

    [Fact]
    public async Task Hwnd_ReturnsZeroBeforeWindowReady()
    {
        using var h = ServerHarness.Start();

        using var response = await h.Client.GetAsync("/hwnd");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJson(response);
        Assert.Equal("0x0", json.RootElement.GetProperty("hwnd").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("generation").GetInt32());
    }

    [Fact]
    public async Task Hwnd_ReturnsHexEncodedHandle()
    {
        using var h = ServerHarness.Start();
        h.Server.GetHwnd = () => (IntPtr)0x12345;

        using var response = await h.Client.GetAsync("/hwnd");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJson(response);
        Assert.Equal("0x12345", json.RootElement.GetProperty("hwnd").GetString());
    }

    [Fact]
    public async Task Hwnd_IncludesGeneration()
    {
        using var h = ServerHarness.Start();
        h.Server.GetHwnd = () => (IntPtr)0x12345;
        h.Server.Generation = 7;

        using var response = await h.Client.GetAsync("/hwnd");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJson(response);
        Assert.Equal(7, json.RootElement.GetProperty("generation").GetInt32());
    }

    [Fact]
    public async Task EmbedAck_RequiresPost()
    {
        using var h = ServerHarness.Start();

        using var response = await h.Client.GetAsync("/embed/ack");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task EmbedAck_RequiresJsonContentType()
    {
        using var h = ServerHarness.Start();

        using var response = await h.Client.PostAsync("/embed/ack", new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task EmbedAck_RejectsBodyOver4Kb()
    {
        using var h = ServerHarness.Start();
        var body = new string('x', PreviewCaptureServer.EmbedMaxBodyBytes + 1);

        using var response = await h.Client.PostAsync("/embed/ack", Json(body));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task EmbedAck_RejectsMissingParent()
    {
        using var h = ServerHarness.Start();

        using var response = await h.Client.PostAsync("/embed/ack", Json("{\"w\":100,\"h\":100,\"generation\":1}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmbedAck_RejectsGenerationMismatch_409()
    {
        using var h = ServerHarness.Start();
        h.Server.Generation = 3;
        h.Server.AckEmbed = (_, _, _, _) => EmbedAckResult.GenerationMismatch;

        using var response = await h.Client.PostAsync("/embed/ack", Json("{\"parent\":\"0x123\",\"w\":100,\"h\":100,\"generation\":2}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ReadJson(response);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("generation-mismatch", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("expected").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("got").GetInt32());
    }

    [Fact]
    public async Task EmbedAck_NoCallback_Returns503EmbedNotReady()
    {
        using var h = ServerHarness.Start();
        h.Server.AckEmbed = null;

        using var response = await h.Client.PostAsync("/embed/ack", Json("{\"parent\":\"0x123\",\"w\":100,\"h\":100,\"generation\":1}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        using var json = await ReadJson(response);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("embed-not-ready", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task EmbedAck_DpiMismatch_Returns412()
    {
        using var h = ServerHarness.Start();
        h.Server.AckEmbed = (_, _, _, _) => EmbedAckResult.DpiMismatch;

        using var response = await h.Client.PostAsync("/embed/ack", Json("{\"parent\":\"0x123\",\"w\":100,\"h\":100,\"generation\":1}"));

        Assert.Equal((HttpStatusCode)412, response.StatusCode);
        using var json = await ReadJson(response);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("dpi-mismatch", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task EmbedAck_InvokesCallbackOnce()
    {
        using var h = ServerHarness.Start();
        var calls = 0;
        IntPtr observedParent = IntPtr.Zero;
        h.Server.AckEmbed = (parent, w, height, generation) =>
        {
            calls++;
            observedParent = parent;
            Assert.Equal(100, w);
            Assert.Equal(200, height);
            Assert.Equal(1, generation);
            return EmbedAckResult.Success;
        };

        using var response = await h.Client.PostAsync("/embed/ack", Json("{\"parent\":\"0x12345\",\"w\":100,\"h\":200,\"generation\":1}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal((IntPtr)0x12345, observedParent);
        using var json = await ReadJson(response);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task EmbedResize_PostsToCallback()
    {
        using var h = ServerHarness.Start();
        var calls = new List<(int W, int H)>();
        h.Server.ResizeEmbed = (w, height) => calls.Add((w, height));

        for (int i = 0; i < 5; i++)
        {
            using var response = await h.Client.PostAsync("/embed/resize", Json($"{{\"w\":{i},\"h\":{i + 10}}}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(5, calls.Count);
        Assert.Equal((4, 14), calls[^1]);
    }

    [Fact]
    public async Task EmbedMove_OwnerModeOnly_Returns400InChildMode()
    {
        using var h = ServerHarness.Start();

        using var childResponse = await h.Client.PostAsync("/embed/move", Json("{\"x\":1,\"y\":2}"));
        Assert.Equal(HttpStatusCode.BadRequest, childResponse.StatusCode);

        var calls = 0;
        h.Server.MoveEmbed = (x, y) =>
        {
            calls++;
            Assert.Equal(1, x);
            Assert.Equal(2, y);
        };
        using var ownerResponse = await h.Client.PostAsync("/embed/move", Json("{\"x\":1,\"y\":2}"));

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EmbedRelease_InvokesCallbackAndAcks()
    {
        using var h = ServerHarness.Start();
        var calls = 0;
        h.Server.ReleaseEmbed = () => calls++;

        using var response = await h.Client.PostAsync("/embed/release", Json("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, calls);
        using var json = await ReadJson(response);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task EmbedEndpoints_RejectInNonEmbedSession()
    {
        using var nonEmbed = ServerHarness.Start(embedMode: false);
        nonEmbed.Server.ReleaseEmbed = () => { };

        using var rejected = await nonEmbed.Client.PostAsync("/embed/release", Json("{}"));
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);

        using var embed = ServerHarness.Start(embedMode: true);
        embed.Server.ReleaseEmbed = () => { };
        using var routed = await embed.Client.PostAsync("/embed/release", Json("{}"));
        Assert.Equal(HttpStatusCode.OK, routed.StatusCode);
    }


    [Fact]
    public async Task Frame_Returns404InEmbedMode()
    {
        using var h = ServerHarness.Start(embedMode: true);

        using var response = await h.Client.GetAsync("/frame");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, h.Server.ActiveReaders);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class ServerHarness : IDisposable
    {
        private ServerHarness(PreviewCaptureServer server, HttpClient client)
        {
            Server = server;
            Client = client;
        }

        public PreviewCaptureServer Server { get; }
        public HttpClient Client { get; }

        public static ServerHarness Start(bool embedMode = true)
        {
            var port = GetFreePort();
#pragma warning disable IL2026
            var server = PreviewCaptureServer.CreateForTests(port, Token);
#pragma warning restore IL2026
            server.EmbedMode = embedMode;
            server.Start();

            var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            return new ServerHarness(server, client);
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
