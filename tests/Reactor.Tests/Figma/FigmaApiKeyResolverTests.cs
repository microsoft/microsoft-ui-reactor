using Microsoft.UI.Reactor.Cli.Figma;
using Xunit;

namespace Reactor.Tests.Figma;

public class FigmaApiKeyResolverTests
{
    [Fact]
    public void TryExtract_McpServersFormat_FindsKeyInArgs()
    {
        var json = """
        {
          "mcpServers": {
            "figma": {
              "command": "npx",
              "args": ["-y", "figma-developer-mcp", "--figma-api-key=figd_test_key_123", "--stdio"]
            }
          }
        }
        """;
        var path = WriteTempJson(json);
        try
        {
            var key = FigmaApiKeyResolver.TryExtractFromMcpConfigFile(path);
            Assert.Equal("figd_test_key_123", key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryExtract_ServersFormat_FindsKeyInArgs()
    {
        var json = """
        {
          "servers": {
            "figma": {
              "command": "npx",
              "args": ["--figma-api-key=my_key_456"]
            }
          }
        }
        """;
        var path = WriteTempJson(json);
        try
        {
            var key = FigmaApiKeyResolver.TryExtractFromMcpConfigFile(path);
            Assert.Equal("my_key_456", key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryExtract_FlatFormat_FindsKeyInArgs()
    {
        var json = """
        {
          "figma": {
            "command": "npx",
            "args": ["--figma-api-key=flat_key"]
          }
        }
        """;
        var path = WriteTempJson(json);
        try
        {
            var key = FigmaApiKeyResolver.TryExtractFromMcpConfigFile(path);
            Assert.Equal("flat_key", key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryExtract_EnvBlock_FindsKey()
    {
        var json = """
        {
          "mcpServers": {
            "figma": {
              "command": "npx",
              "args": ["-y", "figma-developer-mcp"],
              "env": { "FIGMA_API_KEY": "env_key_789" }
            }
          }
        }
        """;
        var path = WriteTempJson(json);
        try
        {
            var key = FigmaApiKeyResolver.TryExtractFromMcpConfigFile(path);
            Assert.Equal("env_key_789", key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryExtract_NoFigmaServer_ReturnsNull()
    {
        var json = """
        {
          "mcpServers": {
            "ado": { "command": "agency", "args": ["mcp", "ado"] }
          }
        }
        """;
        var path = WriteTempJson(json);
        try
        {
            var key = FigmaApiKeyResolver.TryExtractFromMcpConfigFile(path);
            Assert.Null(key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryExtract_MalformedJson_ReturnsNull()
    {
        var path = WriteTempJson("{ not valid json }}}");
        try
        {
            var key = FigmaApiKeyResolver.TryExtractFromMcpConfigFile(path);
            Assert.Null(key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryExtract_NonexistentFile_ReturnsNull()
    {
        var key = FigmaApiKeyResolver.TryExtractFromMcpConfigFile(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json"));
        Assert.Null(key);
    }

    [Fact]
    public void TryExtract_ColonSeparator_FindsKey()
    {
        var json = """
        {
          "mcpServers": {
            "figma": { "args": ["--figma-api-key:colon_key"] }
          }
        }
        """;
        var path = WriteTempJson(json);
        try
        {
            var key = FigmaApiKeyResolver.TryExtractFromMcpConfigFile(path);
            Assert.Equal("colon_key", key);
        }
        finally { File.Delete(path); }
    }

    private static string WriteTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-mcp-config-{Guid.NewGuid()}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
