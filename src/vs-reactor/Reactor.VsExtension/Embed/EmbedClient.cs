#nullable enable
#pragma warning disable IL2026, IL3050

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.UI.Reactor.VsExtension.Embed
{
    internal sealed record EmbedStatus(bool Building, int Fps, int Port, string Protocol, int Generation);

    internal sealed record EmbedComponentsResponse(IReadOnlyList<string> Components, string? Current);

    internal sealed class EmbedGenerationMismatchEventArgs : EventArgs
    {
        public EmbedGenerationMismatchEventArgs(int expected, int got)
        {
            Expected = expected;
            Got = got;
        }

        public int Expected { get; }

        public int Got { get; }
    }

    internal interface IEmbedClient : IDisposable
    {
        event EventHandler<EmbedGenerationMismatchEventArgs>? GenerationMismatch;

        Task<EmbedStatus> StatusAsync(CancellationToken ct = default);

        Task<(IntPtr Hwnd, int Generation)> GetHwndAsync(CancellationToken ct = default);

        Task<bool> AckEmbedAsync(IntPtr parent, int width, int height, int generation, CancellationToken ct = default);

        Task ResizeAsync(int width, int height, CancellationToken ct = default);

        Task MoveAsync(int x, int y, CancellationToken ct = default);

        Task ReleaseAsync(CancellationToken ct = default);

        Task<EmbedComponentsResponse> GetComponentsAsync(CancellationToken ct = default);

        Task<bool> PreviewAsync(string componentName, CancellationToken ct = default);
    }

    internal sealed class EmbedClient : IEmbedClient
    {
        private const string ExpectedProtocol = "embed-v1";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly HttpClient _http;
        private readonly int _port;
        private readonly string _token;
        private bool _disposed;

        public EmbedClient(int port, string token, HttpMessageHandler? handler = null)
        {
            _port = port;
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Host = $"127.0.0.1:{port}";
            _http.BaseAddress = new Uri($"http://127.0.0.1:{port}/");
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        public int Port => _port;

        public string Token => _token;

        public event EventHandler<EmbedGenerationMismatchEventArgs>? GenerationMismatch;

        public async Task<EmbedStatus> StatusAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using (var response = await _http.GetAsync("status", ct).ConfigureAwait(false))
            {
                var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateRequestException(response, body);
                }

                using (var document = JsonDocument.Parse(body))
                {
                    if (!document.RootElement.TryGetProperty("protocol", out var protocolElement) || protocolElement.ValueKind != JsonValueKind.String)
                    {
                        throw ProtocolMismatch("<missing>");
                    }

                    var protocol = protocolElement.GetString();
                    if (!string.Equals(protocol, ExpectedProtocol, StringComparison.Ordinal))
                    {
                        throw ProtocolMismatch(protocol ?? "<null>");
                    }
                }

                var status = JsonSerializer.Deserialize<EmbedStatus>(body, JsonOptions);
                if (status == null)
                {
                    throw new InvalidOperationException("The Reactor /status response was empty or invalid.");
                }

                return status;
            }
        }

        public async Task<(IntPtr Hwnd, int Generation)> GetHwndAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using (var response = await _http.GetAsync("hwnd", ct).ConfigureAwait(false))
            {
                var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateRequestException(response, body);
                }

                var payload = JsonSerializer.Deserialize<HwndResponse>(body, JsonOptions);
                if (payload == null || string.IsNullOrWhiteSpace(payload.Hwnd))
                {
                    throw new InvalidOperationException("The Reactor /hwnd response did not include an HWND.");
                }

                return (ParseHwnd(payload.Hwnd!), payload.Generation);
            }
        }

        public async Task<bool> AckEmbedAsync(IntPtr parent, int width, int height, int generation, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var request = new
            {
                parent = ToHwndString(parent),
                w = width,
                h = height,
                generation,
            };

            using (var response = await PostJsonAsync("embed/ack", request, ct).ConfigureAwait(false))
            {
                var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    var mismatch = JsonSerializer.Deserialize<GenerationMismatchResponse>(body, JsonOptions) ?? new GenerationMismatchResponse();
                    GenerationMismatch?.Invoke(this, new EmbedGenerationMismatchEventArgs(mismatch.Expected, mismatch.Got));
                    return false;
                }

                if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    throw new EmbedDpiMismatchException("Reactor embed DPI mismatch. Fall back to owner mode for this preview session.");
                }

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    return false;
                }

                throw CreateRequestException(response, body);
            }
        }

        public Task ResizeAsync(int width, int height, CancellationToken ct = default)
        {
            return PostAndRequireSuccessAsync("embed/resize", new { w = width, h = height }, ct);
        }

        public Task MoveAsync(int x, int y, CancellationToken ct = default)
        {
            return PostAndRequireSuccessAsync("embed/move", new { x, y }, ct);
        }

        public Task ReleaseAsync(CancellationToken ct = default)
        {
            return PostAndRequireSuccessAsync("embed/release", new { }, ct);
        }

        public async Task<EmbedComponentsResponse> GetComponentsAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using (var response = await _http.GetAsync("components", ct).ConfigureAwait(false))
            {
                var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateRequestException(response, body);
                }

                var payload = JsonSerializer.Deserialize<EmbedComponentsResponse>(body, JsonOptions);
                if (payload == null)
                {
                    throw new InvalidOperationException("The Reactor /components response was empty or invalid.");
                }

                return payload;
            }
        }

        public async Task<bool> PreviewAsync(string componentName, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (componentName == null)
            {
                throw new ArgumentNullException(nameof(componentName));
            }

            using (var response = await PostJsonAsync("preview", new { component = componentName }, ct).ConfigureAwait(false))
            {
                var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    return false;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw CreateRequestException(response, body);
                }

                return true;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _http.CancelPendingRequests();
            }
            catch
            {
            }

            _http.Dispose();
        }

        private async Task PostAndRequireSuccessAsync(string relativeUri, object payload, CancellationToken ct)
        {
            ThrowIfDisposed();
            using (var response = await PostJsonAsync(relativeUri, payload, ct).ConfigureAwait(false))
            {
                var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateRequestException(response, body);
                }
            }
        }

        private async Task<HttpResponseMessage> PostJsonAsync(string relativeUri, object payload, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            {
                return await _http.PostAsync(relativeUri, content, ct).ConfigureAwait(false);
            }
        }

        private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var body = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return body;
        }

        private static EmbedRequestException CreateRequestException(HttpResponseMessage response, string body)
        {
            var message = $"Reactor embed request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()}).";
            if (!string.IsNullOrWhiteSpace(body))
            {
                message += " Response body: " + body;
            }

            return new EmbedRequestException(response.StatusCode, message, body);
        }

        private static EmbedProtocolMismatchException ProtocolMismatch(string got)
        {
            return new EmbedProtocolMismatchException($"Reactor version mismatch — expected protocol 'embed-v1', got '{got}'. Please update the Reactor package.");
        }

        private static IntPtr ParseHwnd(string text)
        {
            var trimmed = text.Trim();
            long value;
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = Convert.ToInt64(trimmed.Substring(2), 16);
            }
            else
            {
                value = long.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            return new IntPtr(value);
        }

        private static string ToHwndString(IntPtr hwnd)
        {
            return "0x" + hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EmbedClient));
            }
        }

        private sealed class HwndResponse
        {
            public string? Hwnd { get; set; }

            public int Generation { get; set; }
        }

        private sealed class GenerationMismatchResponse
        {
            public int Expected { get; set; }

            public int Got { get; set; }
        }
    }
}
