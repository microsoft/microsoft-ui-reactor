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
            static void Report(string message, string source) =>
                Program.ReportRenderError(new InvalidOperationException(message), source);

            await writer.RunAsync("Fixture", "clean", () => Task.CompletedTask);
            Expect(writer.Failed == 0, "a clean scenario passes");

            Report("before any scenario", "Render");
            await writer.RunAsync("Fixture", "earlier-error", () => Task.CompletedTask);
            Expect(writer.Failed == 0, "an error reported before a scenario does not fail it");

            await writer.RunAsync("Fixture", "error-during-pass", () =>
            {
                Report("during scenario", "Effect");
                return Task.CompletedTask;
            });
            Expect(writer.Failed == 1, "an error reported during a satisfied scenario fails it");

            // ShowAsync ends with the same satisfied wait and error check, so this is the
            // headless shape of "error, then another render" within one scenario.
            await writer.RunAsync("Fixture", "error-then-render", async () =>
            {
                Report("before the next render", "Effect");
                await CompatibilityRoot.WaitAsync(() => true, "next render revision");
                CompatibilityRoot.ThrowRenderError();
            });
            Expect(writer.Failed == 2, "a later render step in the same scenario does not erase an earlier error");

            bool threw = false;
            try { await CompatibilityRoot.WaitAsync(() => true, "cleanup after a failed scenario"); }
            catch (InvalidOperationException) { threw = true; }
            Expect(!threw, "an error already charged to a finished scenario does not fail later cleanup");

            Program.ScenarioErrorBaseline = Program.RenderErrorCount;
            Report("during wait", "Render");
            threw = false;
            try { await CompatibilityRoot.WaitAsync(() => true, "already satisfied"); }
            catch (InvalidOperationException) { threw = true; }
            Expect(threw, "a satisfied wait still reports a pending render error");
        }
        finally
        {
            Program.ScenarioErrorBaseline = Program.RenderErrorCount;
            writer.Complete();
            File.Delete(path);
        }
        Console.WriteLine(failures.Count == 0 ? "# PROTOCOL SELF-TEST PASSED" : "# PROTOCOL SELF-TEST FAILED");
        return failures.Count == 0 ? 0 : 1;
    }
}
