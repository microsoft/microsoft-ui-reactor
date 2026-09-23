using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Structural assertions on <c>.github/workflows/docs.yml</c>, covering the
/// wiring that turns a stranded Pages deployment from silent into red
/// (issue #1268).
///
/// The bug these guard against is not "the gate reports the wrong answer" — the
/// node suite in <see cref="DocsDeploymentVerifierTests"/> covers that — it is
/// "the gate is still present but no longer connected to anything". Each seam
/// below fails open if it is quietly removed: without the stamp the verifier
/// compares a version list that a <c>main</c> push never changes; without
/// <c>needs: deploy</c> the verifier reads the site before the deployment; and
/// without <c>github.run_id</c> as the expected value, the comparison it makes
/// is a tautology. A workflow file is not covered by any compiler, so these are
/// asserted here rather than assumed.
/// </summary>
public sealed class DocsDeployWiringTests
{
    private static readonly YamlMappingNode Workflow = LoadWorkflow();
    private static readonly YamlMappingNode Jobs = Map(Workflow, "jobs");

    [Fact]
    public void Publish_exposes_the_deploy_decision_and_the_published_version_list()
    {
        var outputs = Map(Map(Jobs, "publish"), "outputs");

        Assert.Equal("${{ steps.deploy-gate.outputs.deploy }}", Scalar(outputs, "deploy"));
        Assert.Equal("${{ steps.artifact.outputs.versions }}", Scalar(outputs, "versions"));
    }

    [Fact]
    public void Pending_runs_queue_instead_of_evicting_each_other()
    {
        var concurrency = Map(Workflow, "concurrency");

        // Paired with the stand-down below, and unsafe without it. Under the
        // default `queue: single` a later docs push evicts a still-pending tag
        // run, so `publish` would defer the deployment to a run that never
        // happens and the release would be lost with nothing going red.
        Assert.Equal("max", Scalar(concurrency, "queue"));

        // `queue: max` plus `cancel-in-progress: true` is a workflow validation
        // error, which would take the whole workflow offline rather than fail
        // one job.
        Assert.NotEqual("true", Scalar(concurrency, "cancel-in-progress"));
    }

    [Fact]
    public void Publish_decides_whether_this_run_owns_the_deployment()
    {
        var run = StepRun("publish", "deploy-gate");

        // The whole point of the step: a merge commit that already carries a
        // release tag must leave the deployment to the tag run, so the two
        // cannot collide under one pages_build_version.
        Assert.Contains("git tag --points-at", run, StringComparison.Ordinal);
        Assert.Contains("deploy=false", run, StringComparison.Ordinal);
        Assert.Contains("deploy=true", run, StringComparison.Ordinal);
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
        var run = Scalar(step, "run")!;

        // The artifact step hard-fails on an empty list, so a step that stops
        // recording turns into a red run rather than a quieter gate — but only
        // if every step records in the first place.
        Assert.Contains("$RUNNER_TEMP/$PUBLISHED_VERSIONS_FILE", run, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_stands_down_when_publish_says_the_tag_run_owns_it()
    {
        var deploy = Map(Jobs, "deploy");

        Assert.Equal("needs.publish.outputs.deploy == 'true'", Scalar(deploy, "if"));
        Assert.Equal("${{ steps.deployment.outputs.page_url }}", Scalar(Map(deploy, "outputs"), "page_url"));
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
