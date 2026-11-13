using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Structural assertions on <c>.github/workflows/docs.yml</c>, covering the
/// wiring that keeps release docs from being silently stranded on Pages
/// (issue #1268).
///
/// Two halves. First, a release tag must never deploy: Pages accepts a tag-ref
/// deployment of a commit it already serves, reports success, and keeps
/// serving the previous artifact, so a tag run hands its deployment to a run it
/// dispatches on <c>main</c>. Second, every deployment that does happen is
/// verified against the live site.
///
/// The bug these guard against is not "the gate reports the wrong answer" — the
/// node suite in <see cref="DocsDeploymentVerifierTests"/> covers that — it is
/// "the gate is still present but no longer connected to anything". Each seam
/// below fails open if it is quietly removed: without the stamp the verifier
/// compares a version list that a <c>main</c> push never changes; without
/// <c>needs: deploy</c> the verifier reads the site before the deployment;
/// without <c>github.run_id</c> as the expected value, the comparison it makes
/// is a tautology; and without the handoff, a release is published to
/// <c>gh-pages</c> and never served. A workflow file is not covered by any
/// compiler, so these are asserted here rather than assumed.
/// </summary>
public sealed class DocsDeployWiringTests
{
    private static readonly YamlMappingNode Workflow = LoadWorkflow();
    private static readonly YamlMappingNode Jobs = Map(Workflow, "jobs");

    [Fact]
    public void Publish_exposes_the_published_version_list()
    {
        var outputs = Map(Map(Jobs, "publish"), "outputs");

        Assert.Equal("${{ steps.artifact.outputs.versions }}", Scalar(outputs, "versions"));
        Assert.Equal("${{ steps.artifact.outputs.published }}", Scalar(outputs, "published"));
        Assert.Equal("${{ steps.artifact.outputs.run_attempt }}", Scalar(outputs, "run_attempt"));
    }

    [Fact]
    public void A_tag_run_hands_its_deployment_off_and_every_other_run_deploys()
    {
        var deploy = Map(Jobs, "deploy");
        var handoff = Map(Jobs, "handoff");

        // A tag-ref deployment of a commit Pages already serves is accepted,
        // reported successful, and never served — on every release from
        // preview.16 to preview.18. So a tag run must not deploy...
        Assert.Equal("github.ref_type != 'tag'", Scalar(deploy, "if"));

        // ...and must hand off instead. Exact complements, so every run either
        // deploys (and is verified) or hands off. Narrowing either side leaves
        // some ref publishing to gh-pages with nothing serving it.
        Assert.Equal("github.ref_type == 'tag'", Scalar(handoff, "if"));

        // After publish, so the version is already on gh-pages when the
        // dispatched run assembles its artifact from the whole branch.
        Assert.Equal("publish", Scalar(handoff, "needs"));
        Assert.Equal("write", Scalar(Map(handoff, "permissions"), "actions"));

        Assert.Equal("${{ steps.deployment.outputs.page_url }}", Scalar(Map(deploy, "outputs"), "page_url"));
    }

    [Fact]
    public void The_handoff_dispatches_this_workflow_on_a_branch_with_what_the_tag_run_published()
    {
        // A tag here would hand the deployment to another tag-ref run: the very
        // deployment Pages drops. It must be a branch this workflow publishes.
        var deployRef = Scalar(Map(Workflow, "env"), "PAGES_DEPLOY_REF");
        var on = Map(Workflow, "on");
        var branches = ((YamlSequenceNode)Map(on, "push").Children["branches"]).Children
            .Select(n => ((YamlScalarNode)n).Value)
            .ToArray();
        Assert.Contains(deployRef, branches);

        var step = Steps("handoff").Single();
        var run = Code(Scalar(step, "run")!);

        Assert.Equal("${{ needs.publish.outputs.published }}", Scalar(Map(step, "env"), "PUBLISHED"));
        Assert.Contains("/dispatches", run, StringComparison.Ordinal);
        Assert.Matches(@"--arg ref ""\$PAGES_DEPLOY_REF""", run);

        // The versions themselves, not just the input's name: a request that
        // sent `handoff_versions: ""` would keep every name check green while
        // the dispatched run verified only `main`.
        Assert.Contains(@"<<< ""${PUBLISHED:-null}""", run, StringComparison.Ordinal);
        Assert.Matches(@"--arg versions ""\$versions""", run);
        Assert.Matches(@"handoff_versions:\s*\$versions\b", run);

        // The dispatched run's workflow must declare every input the request
        // names. The API rejects an undeclared one with a 422, which fails the
        // job loudly — but only on release day. Read from the jq request rather
        // than hard-coded, so renaming an input on one side alone goes red here.
        var request = Regex.Match(run, @"inputs:\s*\{(?<body>[^}]*)\}");
        Assert.True(request.Success, "The handoff request names no workflow_dispatch inputs.");
        var sent = Regex.Matches(request.Groups["body"].Value, @"(?<name>\w+)\s*:")
            .Select(m => m.Groups["name"].Value)
            .ToArray();
        Assert.Contains("handoff_versions", sent);

        var declared = Map(Map(on, "workflow_dispatch"), "inputs");
        foreach (var name in sent)
        {
            Assert.True(
                declared.Children.ContainsKey(new YamlScalarNode(name)),
                $"The handoff dispatches input '{name}', which workflow_dispatch does not declare.");
        }
    }

    [Fact]
    public void The_handoff_fails_when_the_dispatched_run_never_got_its_turn()
    {
        var run = Code(Scalar(Steps("handoff").Single(), "run")!);

        // `queue: max` still cancels a run that arrives at a full queue (100
        // pending), and the dispatch API's 2xx says nothing about that. This
        // run holds `pages`, so a healthy dispatched run cannot have finished
        // yet; one that has never got in. Without this check the release sits
        // on gh-pages unserved while the tag run is green.
        Assert.Contains(".workflow_run_id", run, StringComparison.Ordinal);
        Assert.Contains("actions/runs/$run_id", run, StringComparison.Ordinal);
        Assert.Matches(@"=\s*completed\s*\];\s*then\s*\n(?:[^\n]*\n){0,3}?\s*exit 1\b", run);
    }

    [Fact]
    public void A_run_handed_a_deployment_verifies_the_handed_off_versions()
    {
        var steps = Steps("publish").ToList();
        var adopt = steps.FindIndex(s => Scalar(s, "if") == "inputs.handoff_versions");
        var artifact = steps.FindIndex(s => Scalar(s, "id") == "artifact");

        // Without this the dispatched run proves only `main`, and the release
        // directory it was handed would pass on its versions.json entry alone.
        Assert.True(adopt >= 0, "No publish step adopts the versions a release-tag run hands off.");
        Assert.Equal("${{ inputs.handoff_versions }}", Scalar(Map(steps[adopt], "env"), "HANDOFF_VERSIONS"));

        // The parsed list is what gets appended. Matching the destination alone
        // stays green if the parse is removed and an empty array is appended.
        var code = Code(Scalar(steps[adopt], "run")!);
        var parse = Regex.Match(code, @"read -r -a (?<list>\w+) <<< ""\$HANDOFF_VERSIONS""");
        Assert.True(parse.Success, "The adopt step no longer parses HANDOFF_VERSIONS into a list.");
        Assert.Contains(
            $"\"${{{parse.Groups["list"].Value}[@]}}\" >> \"$RUNNER_TEMP/$PUBLISHED_VERSIONS_FILE\"",
            code,
            StringComparison.Ordinal);

        // Recorded before the artifact step turns the list into the `published`
        // output, and cross-checks it against gh-pages' versions.json.
        Assert.True(adopt < artifact, "Handed-off versions are recorded after the artifact step has already read the list.");
    }

    [Fact]
    public void Pending_runs_queue_instead_of_evicting_each_other()
    {
        var concurrency = Map(Workflow, "concurrency");

        // Under the Actions default (`queue: single`) a later docs push evicts
        // a still-pending tag run, so that run never publishes its version to
        // `gh-pages` at all. The release is then lost outright with no job
        // failing anywhere, which is why this setting is asserted rather than
        // left to a comment.
        Assert.Equal("max", Scalar(concurrency, "queue"));

        // `queue: max` plus `cancel-in-progress: true` is a workflow validation
        // error, which would take the whole workflow offline rather than fail
        // one job.
        Assert.NotEqual("true", Scalar(concurrency, "cancel-in-progress"));
    }

    [Fact]
    public void The_artifact_carries_a_stamp_identifying_the_run_that_built_it()
    {
        var run = StepRun("publish", "artifact");

        // Without the stamp the gate falls back to comparing versions.json,
        // which a `main` push never changes — so the check would pass whether
        // or not this run's deployment ever landed.
        //
        // Matched as a redirect, not as a bare mention: the step also cats the
        // file back out for the log, and a substring check alone stays green
        // when the write itself is removed.
        Assert.Matches(@">\s*site/deploy-stamp\.json", run);
        Assert.Contains("$GITHUB_RUN_ID", run, StringComparison.Ordinal);

        // Named without a leading `.` or `_`: Pages has historically excluded
        // those, and an unservable stamp would fail every run.
        Assert.DoesNotContain("site/.deploy-stamp", run, StringComparison.Ordinal);
        Assert.DoesNotContain("site/_deploy-stamp", run, StringComparison.Ordinal);

        Assert.Contains("versions=", run, StringComparison.Ordinal);
        Assert.Contains("$GITHUB_OUTPUT", run, StringComparison.Ordinal);

        // The versions this run published drive which directories verify
        // probes. Without them the probe silently narrows to the `latest`
        // holder, which never covers a backported tag or a `main` push.
        Assert.Matches(@"published=", run);
        Assert.Contains("PUBLISHED_VERSIONS_FILE", run, StringComparison.Ordinal);

        // The recorded list and mike's versions.json are produced
        // independently. Without this cross-check a divergence would quietly
        // shrink what verify probes instead of failing.
        Assert.Contains("site/versions.json", run, StringComparison.Ordinal);
        Assert.Matches(@"missing\b", run);
    }

    [Theory]
    [InlineData("Publish the main version")]
    [InlineData("Publish the release version")]
    [InlineData("Backfill release versions")]
    public void Every_publishing_step_records_what_it_published(string stepName)
    {
        var step = Steps("publish").Single(s => Scalar(s, "name") == stepName);

        // Comments stripped first: these steps explain themselves in prose that
        // names the very commands being located, and a comment mentioning
        // `mike deploy` after the write would invert the ordering check.
        var run = Code(Scalar(step, "run")!);

        // The artifact step hard-fails on an empty list, so a step that stops
        // recording turns into a red run rather than a quieter gate — but only
        // if every step records in the first place.
        var write = run.LastIndexOf("$RUNNER_TEMP/$PUBLISHED_VERSIONS_FILE", StringComparison.Ordinal);
        Assert.True(write >= 0, $"'{stepName}' records nothing, so verify cannot know what to probe.");

        // And only if it records *after* publishing. Recording first means a
        // failed `mike deploy` still names a version as published, so verify
        // would go looking for a directory that was never written and blame the
        // deployment for it. Asserting the mention alone would not notice the
        // write moving back above the deploy.
        var deployed = run.LastIndexOf("mike deploy", StringComparison.Ordinal);
        Assert.True(deployed >= 0, $"'{stepName}' does not deploy anything.");
        Assert.True(
            write > deployed,
            $"'{stepName}' records the version it published before `mike deploy` runs, so a failed "
                + "publish would still be reported to the verify job as a directory to probe.");
    }

    [Fact]
    public void A_verify_job_runs_after_the_deployment_and_reads_the_live_site()
    {
        var verify = Map(Jobs, "verify");

        var needs = ((YamlSequenceNode)verify.Children["needs"]).Children
            .Select(n => ((YamlScalarNode)n).Value)
            .ToArray();
        Assert.Contains("publish", needs);
        Assert.Contains("deploy", needs);

        var step = Steps("verify").Single(s => Scalar(s, "run")?.Contains("verify-docs-deployment.mjs", StringComparison.Ordinal) == true);
        var env = Map(step, "env");

        Assert.Equal("${{ needs.deploy.outputs.page_url }}", Scalar(env, "DOCS_BASE_URL"));
        Assert.Equal("${{ github.run_id }}", Scalar(env, "DOCS_EXPECTED_RUN_ID"));

        // The attempt that built the artifact, not this job's own counter:
        // re-running only `deploy` reuses the artifact, so `github.run_attempt`
        // here would disagree with the stamp on every partial re-run.
        Assert.Equal("${{ needs.publish.outputs.run_attempt }}", Scalar(env, "DOCS_EXPECTED_RUN_ATTEMPT"));
        Assert.Equal("${{ needs.publish.outputs.versions }}", Scalar(env, "DOCS_EXPECTED_VERSIONS"));
        Assert.Equal("${{ needs.publish.outputs.published }}", Scalar(env, "DOCS_PUBLISHED_VERSIONS"));
    }

    [Fact]
    public void The_verifier_and_its_regression_suite_are_present()
    {
        var repoRoot = FindRepoRoot();
        var paths = new[] { "verify-docs-deployment.mjs", "verify-docs-deployment.test.mjs" }
            .Select(name => Path.Join(repoRoot, ".github", "scripts", name));

        foreach (var path in paths)
        {
            Assert.True(File.Exists(path), $"Missing {path}, which docs.yml runs after every Pages deployment.");
        }
    }

    private static IEnumerable<YamlMappingNode> Steps(string job) =>
        ((YamlSequenceNode)Map(Jobs, job).Children["steps"]).Children.Cast<YamlMappingNode>();

    /// <summary>
    /// A run block without its comment lines, so prose naming a command or a
    /// path cannot satisfy an assertion the code itself no longer does.
    /// </summary>
    private static string Code(string run) =>
        string.Join(
            "\n",
            run.Split('\n').Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal)));

    private static string StepRun(string job, string stepId)
    {
        var step = Steps(job).SingleOrDefault(s => Scalar(s, "id") == stepId);
        Assert.NotNull(step);
        var run = Scalar(step!, "run");
        Assert.False(string.IsNullOrWhiteSpace(run), $"Step '{stepId}' in job '{job}' has no run block.");
        return run!;
    }

    private static YamlMappingNode Map(YamlMappingNode parent, string key)
    {
        var found = parent.Children.TryGetValue(new YamlScalarNode(key), out var value);
        Assert.True(found, $"Expected a '{key}' mapping.");
        return (YamlMappingNode)value!;
    }

    private static string? Scalar(YamlMappingNode parent, string key) =>
        parent.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;

    private static YamlMappingNode LoadWorkflow()
    {
        var path = Path.Join(FindRepoRoot(), ".github", "workflows", "docs.yml");
        var stream = new YamlStream();
        using var reader = new StringReader(File.ReadAllText(path));
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx")) || Directory.Exists(Path.Join(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Reactor repo root not found from test cwd.");
    }
}
