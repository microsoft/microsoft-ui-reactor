#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    public sealed class EmbedClientTests
    {
        [Fact]
        public async Task Ack_SendsCorrectJson()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"ok\":true}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                Assert.True(await client.AckEmbedAsync(new IntPtr(0x1234), 640, 480, 7));
            }

            using (var doc = JsonDocument.Parse(handler.LastBody!))
            {
                Assert.Equal("0x1234", doc.RootElement.GetProperty("parent").GetString());
                Assert.Equal(640, doc.RootElement.GetProperty("w").GetInt32());
                Assert.Equal(480, doc.RootElement.GetProperty("h").GetInt32());
                Assert.Equal(7, doc.RootElement.GetProperty("generation").GetInt32());
            }
        }

        [Fact]
        public async Task Ack_AddsBearerHeader()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"ok\":true}"));
            using (var client = new EmbedClient(4444, "secret-token", handler))
            {
                await client.AckEmbedAsync(IntPtr.Zero, 1, 2, 3);
            }

            Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
            Assert.Equal("secret-token", handler.LastRequest.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task Ack_SetsHostHeader()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"ok\":true}"));
            using (var client = new EmbedClient(5123, "tok", handler))
            {
                await client.AckEmbedAsync(IntPtr.Zero, 1, 2, 3);
            }

            Assert.Equal("127.0.0.1:5123", handler.LastRequest!.Headers.Host);
        }

        [Fact]
        public async Task Ack_GenerationMismatch_RaisesEvent()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.Conflict, "{\"expected\":2,\"got\":1}"));
            EmbedGenerationMismatchEventArgs? observed = null;
            bool result;
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                client.GenerationMismatch += (_, args) => observed = args;
                result = await client.AckEmbedAsync(IntPtr.Zero, 1, 2, 1);
            }

            Assert.False(result);
            Assert.NotNull(observed);
            Assert.Equal(2, observed!.Expected);
            Assert.Equal(1, observed.Got);
        }

        [Fact]
        public async Task Ack_DpiMismatch_Throws412()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.PreconditionFailed, "{}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                await Assert.ThrowsAsync<EmbedDpiMismatchException>(() => client.AckEmbedAsync(IntPtr.Zero, 1, 2, 3));
            }
        }

        [Fact]
        public async Task Ack_EmbedNotReady_ReturnsFalse()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.ServiceUnavailable, "{}"));
            var raised = false;
            bool result;
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                client.GenerationMismatch += (_, __) => raised = true;
                result = await client.AckEmbedAsync(IntPtr.Zero, 1, 2, 3);
            }

            Assert.False(result);
            Assert.False(raised);
        }

        [Fact]
        public async Task Status_MissingProtocol_ThrowsMismatch()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"building\":false,\"fps\":10,\"port\":4444,\"generation\":1}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                var ex = await Assert.ThrowsAsync<EmbedProtocolMismatchException>(() => client.StatusAsync());
                Assert.Contains("expected protocol 'embed-v1'", ex.Message);
            }
        }

        [Fact]
        public async Task Status_WrongProtocol_ThrowsMismatch()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"building\":false,\"fps\":10,\"port\":4444,\"protocol\":\"embed-v2\",\"generation\":1}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                var ex = await Assert.ThrowsAsync<EmbedProtocolMismatchException>(() => client.StatusAsync());
                Assert.Contains("got 'embed-v2'", ex.Message);
            }
        }

        [Fact]
        public async Task Resize_PostsCorrectBody()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                await client.ResizeAsync(800, 600);
            }

            using (var doc = JsonDocument.Parse(handler.LastBody!))
            {
                Assert.Equal(800, doc.RootElement.GetProperty("w").GetInt32());
                Assert.Equal(600, doc.RootElement.GetProperty("h").GetInt32());
            }
        }

        [Fact]
        public async Task Move_PostsCorrectBody()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                await client.MoveAsync(12, 34);
            }

            using (var doc = JsonDocument.Parse(handler.LastBody!))
            {
                Assert.Equal(12, doc.RootElement.GetProperty("x").GetInt32());
                Assert.Equal(34, doc.RootElement.GetProperty("y").GetInt32());
            }
        }

        [Fact]
        public async Task Release_PostsCorrectBody()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                await client.ReleaseAsync();
            }

            Assert.Equal("{}", handler.LastBody);
        }

        [Fact]
        public async Task Preview_PostsCorrectBody()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                Assert.True(await client.PreviewAsync("Counter"));
            }

            using (var doc = JsonDocument.Parse(handler.LastBody!))
            {
                Assert.Equal("Counter", doc.RootElement.GetProperty("component").GetString());
            }
        }

        [Fact]
        public async Task Status_DeserializesPayload()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"building\":true,\"fps\":30,\"port\":4444,\"protocol\":\"embed-v1\",\"generation\":9}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                var status = await client.StatusAsync();
                Assert.True(status.Building);
                Assert.Equal(30, status.Fps);
                Assert.Equal(4444, status.Port);
                Assert.Equal("embed-v1", status.Protocol);
                Assert.Equal(9, status.Generation);
            }
        }

        [Fact]
        public async Task Components_DeserializesPayload()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"components\":[\"A\",\"B\"],\"current\":\"A\"}"));
            EmbedComponentsResponse response;
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                response = await client.GetComponentsAsync();
            }

            Assert.Equal(new[] { "A", "B" }, response.Components.ToArray());
            Assert.Equal("A", response.Current);
        }

        [Fact]
        public async Task Hwnd_ParsesHexEncoded()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"hwnd\":\"0x12345\",\"generation\":1}"));
            using (var client = new EmbedClient(4444, "tok", handler))
            {
                var result = await client.GetHwndAsync();
                Assert.Equal(new IntPtr(0x12345), result.Hwnd);
                Assert.Equal(1, result.Generation);
            }
        }

        [Fact]
        public async Task Dispose_CancelsPendingRequests()
        {
            var entered = new ManualResetEventSlim(false);
            var cancelled = new TaskCompletionSource<bool>();
            var pending = new TaskCompletionSource<HttpResponseMessage>();
            var handler = new StubHandler((_, ct) =>
            {
                entered.Set();
                ct.Register(() =>
                {
                    cancelled.TrySetResult(true);
                    pending.TrySetCanceled();
                });
                return pending.Task;
            });

            var client = new EmbedClient(4444, "tok", handler);
            var task = client.StatusAsync();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));

            client.Dispose();

            Assert.True(await cancelled.Task);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
                : this((request, _) => Task.FromResult(handler(request)))
            {
            }

            public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            public HttpRequestMessage? LastRequest { get; private set; }

            public string? LastBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                if (request.Content != null)
                {
                    LastBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                return await _handler(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
