using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Issue #1068, wiring tier: the selection rule and the staleness warning as
/// <c>compile</c> actually uses them, rather than as helpers called
/// directly.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReactorXmlSelectionTests"/> pins the rules themselves. It cannot
/// pin that Phase 5.7 <em>applies</em> them: deleting the
/// <c>staleFinding</c> concat that merges the warning into
/// <c>ReferenceGenResult.Findings</c>, or dropping the selection call, leaves
/// every one of those tests green. That is the gap this file closes — it drives
/// <see cref="CompileCommand.Run"/> end to end and reads stdout.
/// </para>
/// <para>
/// The severity claim is the other half. <c>REACTOR_DOC_REFGEN_W002</c> is a
/// warning specifically so <c>--ci</c> keeps passing, and that is a property of
/// the Phase 5.7 gate (<c>if (f.Severity == TierLintSeverity.Error) hasErrors =
/// true;</c>), not of the finding. Asserting the exit code with the warning
/// present is what would catch someone "promoting" it later.
/// </para>
/// <para>
/// Every warning assertion here is paired with a run of the same fixture that
/// must <em>not</em> produce it, so a test that always passes — because the
/// pipeline never reached Phase 5.7 at all, say — shows up as the pair
/// disagreeing rather than as a comforting green.
/// </para>
/// <para>
/// <see cref="CompileCommand.Run"/> resolves the repo root from the process
/// working directory and writes to <see cref="Console"/>, both process-global,
/// so this class shares the repo's console-isolation collection.
/// </para>
/// </remarks>
[Collection("ConsoleTests")]
public class ReferenceStalenessWiringTests
{
    private static readonly DateTime Base = new(2026, 8, 1, 19, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The residue case, end to end: nothing was rebuilt after the edit, so the
    /// pages are generated from an XML that no longer matches source. The
    /// warning must reach stdout, and it must not fail <c>--ci</c>.
    /// </summary>
    [Fact]
    public void Stale_xml_warns_through_the_pipeline_and_still_passes_ci()
    {
        using var repo = new FakeRepo();
        repo.PlantXml("x64/Release/net10.0-windows10.0.22621.0", Base);
        repo.PlantSource("Core/Widget.cs", Base.AddMinutes(30));

        var (exitCode, output) = repo.CompileWithReference();

        Assert.Contains("Phase 5.7: Reference", output);
        Assert.Contains("REACTOR_DOC_REFGEN_W002", output);
        Assert.Contains("src/Reactor/Core/Widget.cs", output);
        // The whole point of Warning severity: --ci must still pass.
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// The paired control. Same fixture, same flags, only the timestamps
    /// reversed — so the assertion above is measuring the comparison and not
    /// merely observing that the pipeline prints warnings.
    /// </summary>
    [Fact]
    public void Fresh_xml_produces_no_staleness_warning()
    {
        using var repo = new FakeRepo();
        repo.PlantXml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(30));
        repo.PlantSource("Core/Widget.cs", Base);

        var (exitCode, output) = repo.CompileWithReference();

        Assert.Contains("Phase 5.7: Reference", output);
        Assert.DoesNotContain("REACTOR_DOC_REFGEN_W002", output);
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// The fix itself, through the command rather than the helper: with a stale
    /// Debug build beside a newer Release one — the exact shape reported in
    /// #1068 — the phase must announce the Release file.
    /// </summary>
    /// <remarks>
    /// Asserting on the printed line is what makes this a test of the pipeline's
    /// choice. It also pins the diagnostic line itself, which is the only thing
    /// an author has to go on when a regenerated page looks wrong.
    /// </remarks>
    [Fact]
    public void The_phase_announces_the_newest_candidate_it_selected()
    {
        using var repo = new FakeRepo();
        repo.PlantXml("x64/Debug/net10.0-windows10.0.22621.0", Base);
        repo.PlantXml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(28));
        repo.PlantSource("Core/Widget.cs", Base.AddMinutes(-5));

        var (exitCode, output) = repo.CompileWithReference();

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "XML: src/Reactor/bin/x64/Release/net10.0-windows10.0.22621.0/Microsoft.UI.Reactor.xml " +
            "(2026-08-01T19:28:00Z, newest of 2 candidate(s))",
            output);
        Assert.DoesNotContain("bin/x64/Debug/net10.0-windows10.0.22621.0/Microsoft.UI.Reactor.xml", output);
    }

    /// <summary>
    /// No build at all is a skip, not a failure — the first-compile case, and
    /// the one the "not found" message exists for. Scoped to a local run: see
    /// the <c>--ci</c> pair below, which must fail on the same fixture.
    /// </summary>
    [Fact]
    public void No_build_at_all_skips_reference_generation_without_failing_locally()
    {
        using var repo = new FakeRepo();
        repo.PlantSource("Core/Widget.cs", Base);

        var (exitCode, output) = repo.CompileWithReferenceLocally();

        Assert.Equal(0, exitCode);
        Assert.Contains("Microsoft.UI.Reactor.xml not found", output);
        Assert.DoesNotContain("REACTOR_DOC_REFGEN_W002", output);
    }

    /// <summary>
    /// Issue #1052. Under <c>--ci</c> the same missing input is a failure, not a
    /// degradation: CI always builds, so no <c>Microsoft.UI.Reactor.xml</c> means the run is
    /// not the run that was asked for, and the pages under
    /// <c>docs/guide/reference/</c> are silently left at whatever was committed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the <c>docs-build</c> freshness gate stands on. That gate
    /// reads a clean <c>git status -- docs/guide</c> as "the committed output
    /// matches a fresh compile" — a reading that is only true if the compile
    /// wrote every page it was supposed to. A skipped Phase 5.7 leaves the tree
    /// clean for the wrong reason, and exiting 0 there made the gate
    /// unfailable for that whole class of drift.
    /// </para>
    /// <para>
    /// The gate used to re-derive this by grepping stdout for
    /// <c>Microsoft.UI.Reactor.xml not found</c>. That failed <em>open</em>: reword the
    /// message in <c>CompileCommand</c> and the grep silently stops matching.
    /// The exit code is the contract now, and this test is what pins it — the
    /// pair with the local case above is what shows it is <c>--ci</c> doing the
    /// work and not the fixture.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_build_at_all_fails_reference_generation_under_ci()
    {
        using var repo = new FakeRepo();
        repo.PlantSource("Core/Widget.cs", Base);

        var (exitCode, output) = repo.CompileWithReference();

        Assert.Equal(1, exitCode);
        Assert.Contains("Microsoft.UI.Reactor.xml not found", output);
        Assert.Contains("Reference generation was skipped for want of an input", output);
        // The compile must not claim success on the way out — that string is
        // the freshness gate's own precondition.
        Assert.DoesNotContain("Documentation compiled successfully.", output);
    }

    /// <summary>
    /// The second missing input Phase 5.7 bails on. Same contract, different
    /// cause — asserting only the <c>Microsoft.UI.Reactor.xml</c> case would leave this one
    /// free to regress back to a silent exit 0.
    /// </summary>
    [Fact]
    public void Missing_reference_map_fails_under_ci_but_not_locally()
    {
        using var repo = new FakeRepo();
        repo.PlantXml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(30));
        repo.PlantSource("Core/Widget.cs", Base);
        repo.RemoveReferenceMap();

        var (ciExit, ciOutput) = repo.CompileWithReference();
        var (localExit, localOutput) = repo.CompileWithReferenceLocally();

        Assert.Contains("reference-map.yaml not found", ciOutput);
        Assert.Equal(1, ciExit);

        Assert.Contains("reference-map.yaml not found", localOutput);
        Assert.Equal(0, localExit);
    }

    /// <summary>
    /// The non-vacuity control for the two <c>--ci</c> failures above: with both
    /// inputs present, <c>--ci</c> passes. Without this, those tests would look
    /// identical against a build that failed <c>--ci</c> for any reason at all.
    /// </summary>
    [Fact]
    public void Both_inputs_present_passes_under_ci()
    {
        using var repo = new FakeRepo();
        repo.PlantXml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(30));
        repo.PlantSource("Core/Widget.cs", Base);

        var (exitCode, output) = repo.CompileWithReference();

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Reference generation was skipped for want of an input", output);
        Assert.Contains("Documentation compiled successfully.", output);
    }

    /// <summary>
    /// The assembly is the other half of the input. The XML file alone cannot
    /// tell element records from other records, so a run without the DLL must
    /// fail before it rewrites or prunes anything, locally as well as in CI.
    /// </summary>
    [Fact]
    public void Missing_dll_fails_and_leaves_the_reference_pages_alone()
    {
        using var repo = new FakeRepo();
        repo.PlantXml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(30), withDll: false);
        repo.PlantSource("Core/Widget.cs", Base);
        var committed = repo.PlantReferencePage("elements/buttons.md", "# Buttons\n");

        var (ciExit, ciOutput) = repo.CompileWithReference();
        var (localExit, localOutput) = repo.CompileWithReferenceLocally();

        Assert.Equal(1, ciExit);
        Assert.Contains("REACTOR_DOC_REFGEN_003", ciOutput);
        Assert.Contains("Microsoft.UI.Reactor.dll not found", ciOutput);
        Assert.Contains("dotnet build src/Reactor", ciOutput);
        Assert.Contains("REACTOR_DOC_REFGEN_003", localOutput);
        Assert.Equal(0, localExit);
        // Neither run may prune: this page is not in the plan, so a run that
        // carried on without the DLL would delete it (see the control below).
        Assert.True(File.Exists(committed));
        Assert.Equal("# Buttons\n", File.ReadAllText(committed));
    }

    /// <summary>
    /// The control for the test above: with the DLL present, the same stray
    /// page is pruned. Without it, "the page is still there" could just mean
    /// the prune never runs on this fixture.
    /// </summary>
    [Fact]
    public void With_the_dll_present_a_page_outside_the_plan_is_pruned()
    {
        using var repo = new FakeRepo();
        repo.PlantXml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(30));
        repo.PlantSource("Core/Widget.cs", Base);
        var stray = repo.PlantReferencePage("elements/buttons.md", "# Buttons\n");

        var (exitCode, output) = repo.CompileWithReference();

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("REACTOR_DOC_REFGEN_003", output);
        Assert.False(File.Exists(stray));
    }

    /// <summary>
    /// Issue #1721: the reference covers Microsoft.UI.Reactor.Advanced too, so a
    /// missing Advanced build skips the phase exactly like a missing core one.
    /// Generating core alone would prune every Advanced page as stale.
    /// </summary>
    [Fact]
    public void Missing_advanced_build_fails_under_ci_and_leaves_pages_alone()
    {
        using var repo = new FakeRepo();
        repo.PlantXml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(30), withAdvanced: false);
        repo.PlantSource("Core/Widget.cs", Base);
        var committed = repo.PlantReferencePage("elements/docking.md", "# Docking\n");

        var (exitCode, output) = repo.CompileWithReference();

        Assert.Equal(1, exitCode);
        Assert.Contains("Microsoft.UI.Reactor.Advanced.xml not found", output);
        Assert.Contains("dotnet build src/Reactor.Advanced", output);
        Assert.True(File.Exists(committed));
    }

    /// <summary>
    /// Minimal repo the doc compiler accepts, plus the two trees issue #1068 is
    /// about: <c>src/Reactor/bin</c> (candidates) and <c>src/Reactor</c>
    /// (sources). Modelled on the fixture in
    /// <see cref="CompileCaptureSkipTests"/>.
    /// </summary>
    private sealed class FakeRepo : IDisposable
    {
        private readonly string _root;
        private readonly string _originalCwd;

        public FakeRepo()
        {
            _originalCwd = Directory.GetCurrentDirectory();
            _root = Path.Join(Path.GetTempPath(),
                "reactor-refgen-stale-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Join(_root, ".git"));
            Directory.CreateDirectory(Path.Join(_root, "docs", "guide", "images"));
            File.WriteAllText(
                Path.Join(_root, "Directory.Build.props"),
                "<Project>\n  <PropertyGroup>\n    <ReactorPublicVersion>0.1.0-test</ReactorPublicVersion>\n  </PropertyGroup>\n</Project>\n");

            // DiscoverApps requires at least one .cs file beside the manifest.
            var appDir = Path.Join(_root, "docs", "_pipeline", "apps", "demo");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Join(appDir, "App.cs"), "// Fixture marker.\n");
            File.WriteAllText(Path.Join(appDir, "doc-manifest.yaml"),
                """
                app:
                  title: "Demo"
                  width: 400
                  height: 300
                  startup-delay: 0

                screenshots: []

                """);

            var templatesDir = Path.Join(_root, "docs", "_pipeline", "templates");
            Directory.CreateDirectory(templatesDir);
            File.WriteAllText(Path.Join(templatesDir, "demo.md.dt"),
                """
                ---
                title: "Demo"
                app: demo
                order: 1
                audience: beginner
                goal: |
                  Fixture template for the reference-staleness wiring tests.
                tier: stub
                ---

                # Demo

                Placeholder body.

                """);

            // Phase 5.7 returns early without this, so the tests would pass on a
            // pipeline that never reached the selection code at all.
            File.WriteAllText(
                Path.Join(_root, "docs", "_pipeline", "reference-map.yaml"),
                """
                defaults:
                  - match: "Microsoft.UI.Reactor.Hooks.*"
                    category: hooks
                    guide-pages: [demo]
                """);
        }

        /// <summary>
        /// Writes a routable <c>Microsoft.UI.Reactor.xml</c> at
        /// <c>src/Reactor/bin/&lt;layout&gt;/</c>, plus the assembly beside it
        /// unless <paramref name="withDll"/> is false. The member is under
        /// <c>Hooks</c> because that is the only category the fixture map
        /// declares, so anything else would produce zero pages and exercise less.
        /// </summary>
        public string PlantXml(string layout, DateTime writeUtc, bool withDll = true, bool withAdvanced = true)
        {
            // Phase 5.7 needs every documented assembly (issue #1721); Advanced
            // is planted beside core unless a test is about its absence.
            if (withAdvanced)
                PlantAssembly("Reactor.Advanced", "Microsoft.UI.Reactor.Advanced", "Microsoft.UI.Reactor.Hooks.UseDraw", layout, writeUtc, withDll: true);
            return PlantAssembly("Reactor", "Microsoft.UI.Reactor", "Microsoft.UI.Reactor.Hooks.UseState", layout, writeUtc, withDll);
        }

        private string PlantAssembly(string project, string assemblyName, string typeName, string layout, DateTime writeUtc, bool withDll)
        {
            var dir = Path.Join("src", project, "bin", Native(layout));
            if (withDll) PlantDll(Path.Join(_root, dir, assemblyName + ".dll"), assemblyName, typeName, writeUtc);
            return Write(dir, assemblyName + ".xml",
                $"""
                <?xml version="1.0"?>
                <doc>
                  <assembly><name>{assemblyName}</name></assembly>
                  <members>
                    <member name="T:{typeName}">
                      <summary>State hook.</summary>
                    </member>
                  </members>
                </doc>
                """,
                writeUtc);
        }

        /// <summary>
        /// Emits a real assembly holding the one public type the XML documents,
        /// so the reference phase reads the same surface it would from a build.
        /// </summary>
        private static void PlantDll(string path, string assemblyName, string typeName, DateTime writeUtc)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var assembly = new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(assemblyName), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule(assemblyName);
            module.DefineType(
                typeName,
                System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class |
                System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed).CreateType();
            assembly.Save(path);
            File.SetLastWriteTimeUtc(path, writeUtc);
        }

        /// <summary>Writes a page under <c>docs/guide/reference/</c>, as a previous compile would have.</summary>
        public string PlantReferencePage(string relativePath, string content) =>
            Write(Path.Join("docs", "guide", "reference", Path.GetDirectoryName(Native(relativePath)) ?? string.Empty),
                Path.GetFileName(relativePath), content, Base);

        /// <summary>Writes a C# file at <c>src/Reactor/&lt;relativePath&gt;</c>.</summary>
        public string PlantSource(string relativePath, DateTime writeUtc)
        {
            var native = Native(relativePath);
            return Write(
                Path.Join("src", "Reactor", Path.GetDirectoryName(native) ?? string.Empty),
                Path.GetFileName(native),
                "// fixture\n",
                writeUtc);
        }

        private string Write(string relativeDir, string fileName, string content, DateTime writeUtc)
        {
            var dir = Path.Join(_root, relativeDir);
            Directory.CreateDirectory(dir);
            var path = Path.Join(dir, fileName);
            File.WriteAllText(path, content);
            // After the write: writing stamps the file with "now".
            File.SetLastWriteTimeUtc(path, writeUtc);
            return path;
        }

        private static string Native(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar);

        /// <summary>
        /// Runs the compile with reference generation ON (no
        /// <c>--skip-reference</c>) and <c>--ci</c>, which is the combination CI
        /// uses. Everything requiring a build, a desktop, a network or a
        /// mermaid-cli is switched off.
        /// </summary>
        public (int ExitCode, string Output) CompileWithReference() =>
            Compile("--no-screenshots", "--no-build", "--skip-diagrams", "--no-ai", "--ci");

        /// <summary>
        /// Same, minus <c>--ci</c>. The pair is what shows a missing-input
        /// failure is scoped to CI rather than being a blanket hard error.
        /// </summary>
        public (int ExitCode, string Output) CompileWithReferenceLocally() =>
            Compile("--no-screenshots", "--no-build", "--skip-diagrams", "--no-ai");

        /// <summary>
        /// Removes the fixture's <c>reference-map.yaml</c>, the other input
        /// whose absence makes Phase 5.7 bail.
        /// </summary>
        public void RemoveReferenceMap() =>
            File.Delete(Path.Join(_root, "docs", "_pipeline", "reference-map.yaml"));

        private (int ExitCode, string Output) Compile(params string[] args)
        {
            var stdout = Console.Out;
            var stderr = Console.Error;
            using var buffer = new StringWriter();
            try
            {
                Directory.SetCurrentDirectory(_root);
                Console.SetOut(buffer);
                Console.SetError(buffer);
                var exit = CompileCommand.Run(args);
                return (exit, buffer.ToString());
            }
            finally
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                Directory.SetCurrentDirectory(_originalCwd);
            }
        }

        public void Dispose() => FixtureCleanup.DeleteTree(_root);
    }
}
