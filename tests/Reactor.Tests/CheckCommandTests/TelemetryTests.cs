// Phase 1.7 — telemetry tests. Spec 038 §10.

using System.Text.Json;
using Microsoft.UI.Reactor.Cli.Check;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class TelemetryTests
{
    [Fact]
    public void IsEnabled_reads_opt_in_env_var()
    {
        var prior = Environment.GetEnvironmentVariable(Telemetry.OptInEnv);
        try
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, null);
            Assert.False(Telemetry.IsEnabled);

            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, "1");
            Assert.True(Telemetry.IsEnabled);
        }
        finally { Environment.SetEnvironmentVariable(Telemetry.OptInEnv, prior); }
    }

    [Fact]
    public void Disabled_telemetry_writes_nothing()
    {
        var prior = Environment.GetEnvironmentVariable(Telemetry.OptInEnv);
        var dir = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "mur-tel-disabled-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, null);
            Telemetry.OnSuggestionEmitted("CS1061", new Suggestion("x", 0.9, "ev", "S"), dir);
            Assert.False(global::System.IO.Directory.Exists(dir));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, prior);
            if (global::System.IO.Directory.Exists(dir)) global::System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Enabled_telemetry_appends_a_row_per_emission()
    {
        var prior = Environment.GetEnvironmentVariable(Telemetry.OptInEnv);
        var dir = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "mur-tel-enabled-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, "1");
            var s = new Suggestion("Button(label, onClick: x)", 0.91, "factory has Action onClick parameter", "SymbolSuggester");
            Telemetry.OnSuggestionEmitted("CS1061", s, dir);
            Telemetry.OnSuggestionEmitted("CS0103", s, dir);

            var files = global::System.IO.Directory.GetFiles(dir, "*.jsonl");
            Assert.Single(files);
            var lines = global::System.IO.File.ReadAllLines(files[0]);
            Assert.Equal(2, lines.Length);
            using var doc = JsonDocument.Parse(lines[0]);
            Assert.Equal("CS1061", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal("SymbolSuggester", doc.RootElement.GetProperty("suggester").GetString());
            Assert.True(doc.RootElement.GetProperty("confidence").GetDouble() > 0.9);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, prior);
            if (global::System.IO.Directory.Exists(dir)) global::System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void No_field_exceeds_256_bytes()
    {
        var prior = Environment.GetEnvironmentVariable(Telemetry.OptInEnv);
        var dir = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "mur-tel-bytes-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, "1");
            var huge = new string('z', 4096);
            // The suggestion text isn't logged (it could carry source-shaped content); we
            // only log code / suggester / confidence / evidence_short. Verify each
            // field of the row is bounded.
            var s = new Suggestion(huge, 0.9, huge, huge);
            Telemetry.OnSuggestionEmitted(huge, s, dir);

            var line = global::System.IO.File.ReadAllLines(global::System.IO.Directory.GetFiles(dir).Single()).Single();
            using var doc = JsonDocument.Parse(line);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var v = prop.Value.GetString()!;
                var bytes = global::System.Text.Encoding.UTF8.GetByteCount(v);
                Assert.True(bytes <= Telemetry.MaxFieldBytes,
                    $"field '{prop.Name}' utf8-bytes={bytes} exceeds MaxFieldBytes={Telemetry.MaxFieldBytes}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, prior);
            if (global::System.IO.Directory.Exists(dir)) global::System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Truncate_bounds_non_ASCII_by_byte_count()
    {
        // Pure non-ASCII: each glyph is 3 bytes in UTF-8, so a 200-char string
        // would be 600 bytes — must shrink past the 256-byte cap.
        var prior = Environment.GetEnvironmentVariable(Telemetry.OptInEnv);
        var dir = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "mur-tel-utf8-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, "1");
            var nonAscii = new string('字', 200); // 3 bytes/char × 200 = 600 bytes
            var s = new Suggestion(nonAscii, 0.9, nonAscii, nonAscii);
            Telemetry.OnSuggestionEmitted(nonAscii, s, dir);

            var line = global::System.IO.File.ReadAllLines(global::System.IO.Directory.GetFiles(dir).Single()).Single();
            using var doc = JsonDocument.Parse(line);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var v = prop.Value.GetString()!;
                var bytes = global::System.Text.Encoding.UTF8.GetByteCount(v);
                Assert.True(bytes <= Telemetry.MaxFieldBytes,
                    $"field '{prop.Name}' utf8-bytes={bytes} exceeds MaxFieldBytes={Telemetry.MaxFieldBytes}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Telemetry.OptInEnv, prior);
            if (global::System.IO.Directory.Exists(dir)) global::System.IO.Directory.Delete(dir, recursive: true);
        }
    }
}
