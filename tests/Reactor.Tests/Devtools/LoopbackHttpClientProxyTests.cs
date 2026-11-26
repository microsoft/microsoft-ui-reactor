using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.UI.Reactor.Cli.Devtools;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;
using CliLockfileEntry = Microsoft.UI.Reactor.Cli.Devtools.LockfileEntry;
using DevtoolsLockfileEntry = Microsoft.UI.Reactor.Hosting.Devtools.LockfileEntry;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Issue #1718: devtools traffic is always to <c>127.0.0.1</c>, so it must never go
/// through the system proxy. Each test installs a <see cref="HttpClient.DefaultProxy"/>
/// that claims every URI (as a WPAD/PAC proxy can) and answers 502, then checks that
/// the probe or client reaches the loopback server without consulting it.
/// </summary>
[Collection("HttpDefaultProxy")]
public sealed class LoopbackHttpClientProxyTests : IDisposable
{
    private readonly IWebProxy _originalProxy = HttpClient.DefaultProxy;
    private readonly CannedHttpServer _proxyServer = new("502 Bad Gateway", "proxy");
    private readonly RecordingProxy _proxy;

    public LoopbackHttpClientProxyTests()
    {
        _proxy = new RecordingProxy(new Uri($"http://127.0.0.1:{_proxyServer.Port}/"));
        HttpClient.DefaultProxy = _proxy;
    }

    public void Dispose()
    {
        HttpClient.DefaultProxy = _originalProxy;
        _proxyServer.Dispose();
    }

    [Fact]
    public void DevtoolsLockfileProbe_BypassesSystemProxy()
    {
        using var server = new CannedHttpServer("200 OK", $"{{\"schema\":\"{LockfileRegistry.McpSchemaTag}\"}}");
        var entry = new DevtoolsLockfileEntry
        {
            Pid = Environment.ProcessId,
            Transport = "http",
            Port = server.Port,
            Endpoint = $"http://127.0.0.1:{server.Port}/mcp",
        };

        Assert.True(LockfileRegistry.IsLive(entry));
        Assert.Equal(0, _proxy.Calls);
        Assert.Equal(0, _proxyServer.Requests);
    }

    [Fact]
    public void CliLockfileProbe_BypassesSystemProxy()
    {
        using var server = new CannedHttpServer("200 OK", $"{{\"schema\":\"{LockfileReader.McpSchemaTag}\"}}");
        var entry = new CliLockfileEntry
        {
            Pid = Environment.ProcessId,
            Transport = "http",
            Port = server.Port,
            Endpoint = $"http://127.0.0.1:{server.Port}/mcp",
            Token = "token",
        };

        Assert.True(LockfileReader.IsLive(entry));
        Assert.Equal(0, _proxy.Calls);
        Assert.Equal(0, _proxyServer.Requests);
    }

    [Fact]
    public void CliMcpClient_BypassesSystemProxy()
    {
        using var server = new CannedHttpServer("200 OK", "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"build\":\"x\"}}");
        using var client = new McpCliClient($"http://127.0.0.1:{server.Port}/mcp", TimeSpan.FromSeconds(10), token: "token");

        using var doc = client.InvokeTool("version", null);

        Assert.Equal("x", doc.RootElement.GetProperty("result").GetProperty("build").GetString());
        Assert.Equal(0, _proxy.Calls);
        Assert.Equal(0, _proxyServer.Requests);
    }

    private sealed class RecordingProxy(Uri proxyUri) : IWebProxy
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination)
        {
            Interlocked.Increment(ref _calls);
            return proxyUri;
        }
        public bool IsBypassed(Uri host) => false;
    }

    /// <summary>
    /// Answers every request with one fixed status and body. A raw <see cref="TcpListener"/>,
    /// not <see cref="HttpListener"/>, so this process owns the port, as the CLI probe's
    /// port-ownership check requires.
    /// </summary>
    private sealed class CannedHttpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly byte[] _response;
        private int _requests;

        public CannedHttpServer(string status, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            _response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n")
                .Concat(bodyBytes).ToArray();
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }
        public int Requests => Volatile.Read(ref _requests);

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient tcp;
                try { tcp = await _listener.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { return; }
                _ = Task.Run(() => ServeAsync(tcp));
            }
        }

        private async Task ServeAsync(TcpClient tcp)
        {
            using (tcp)
            {
                try
                {
                    var stream = tcp.GetStream();
                    var buffer = new byte[16 * 1024];
                    var received = new StringBuilder();
                    while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var n = await stream.ReadAsync(buffer);
                        if (n == 0) return;
                        received.Append(Encoding.ASCII.GetString(buffer, 0, n));
                    }
                    // Drain a request body so the client isn't reset mid-send.
                    var head = received.ToString();
                    var headerEnd = head.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
                    var length = ContentLength(head[..headerEnd]);
                    var bodyRead = Encoding.ASCII.GetByteCount(head[headerEnd..]);
                    while (bodyRead < length)
                    {
                        var n = await stream.ReadAsync(buffer);
                        if (n == 0) break;
                        bodyRead += n;
                    }
                    Interlocked.Increment(ref _requests);
                    await stream.WriteAsync(_response);
                }
                catch (IOException) { }
                catch (SocketException) { }
            }
        }

        private static int ContentLength(string headers)
        {
            foreach (var line in headers.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line["Content-Length:".Length..].Trim(), out var n))
                {
                    return n;
                }
            }
            return 0;
        }

        public void Dispose() => _listener.Stop();
    }
}
