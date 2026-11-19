namespace ToolkitCompatibility;

/// <summary>A render or effect error reported by Reactor's render error handler during a run.</summary>
internal sealed record PendingRenderError(Exception Exception, string Source);

/// <summary>
/// Headless checks of the result protocol's render-error handling. Runs without a window, so it
/// can gate harness changes before a UI slot exists.
/// </summary>
internal static class ProtocolSelfTest
{
    internal static async Task<int> Run()
    {
        var failures = new List<string>();
        void Expect(bool condition, string name)
        {
            Console.WriteLine($"# {(condition ? "PASS" : "FAIL")} {name}");
            if (!condition) failures.Add(name);
        }

        string path = Path.Combine(Path.GetTempPath(), $"wct-protocol-{Guid.NewGuid():N}.jsonl");
        var writer = new ResultWriter(path);
        try
        {
            Program.LastRenderError = null;
            await writer.RunAsync("Fixture", "clean", () => Task.CompletedTask);
            Expect(writer.Failed == 0, "a clean scenario passes");

            Program.LastRenderError = new(new InvalidOperationException("earlier scenario"), "Render");
            await writer.RunAsync("Fixture", "earlier-error", () => Task.CompletedTask);
            Expect(writer.Failed == 0, "an error left by an earlier scenario does not fail a later one");

            await writer.RunAsync("Fixture", "error-during-pass", () =>
            {
                Program.LastRenderError = new(new InvalidOperationException("during scenario"), "Effect");
                return Task.CompletedTask;
            });
            Expect(writer.Failed == 1, "an error reported during a satisfied scenario fails it");

            Program.LastRenderError = new(new InvalidOperationException("during wait"), "Render");
            bool threw = false;
            try { await CompatibilityRoot.WaitAsync(() => true, "already satisfied"); }
            catch (InvalidOperationException) { threw = true; }
            Expect(threw, "a satisfied wait still reports a pending render error");

            Program.LastRenderError = null;
            threw = false;
            try { await CompatibilityRoot.WaitAsync(() => true, "already satisfied"); }
            catch (InvalidOperationException) { threw = true; }
            Expect(!threw, "a satisfied wait with no error succeeds");
        }
        finally
        {
            Program.LastRenderError = null;
            writer.Complete();
            File.Delete(path);
        }
        Console.WriteLine(failures.Count == 0 ? "# PROTOCOL SELF-TEST PASSED" : "# PROTOCOL SELF-TEST FAILED");
        return failures.Count == 0 ? 0 : 1;
    }
}
