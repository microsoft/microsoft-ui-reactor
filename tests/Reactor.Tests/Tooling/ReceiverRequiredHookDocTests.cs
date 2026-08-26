using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Gate for a defect class the phantom lint is blind to <b>by construction</b>.
///
/// <para><c>REACTOR_DOC_PHANTOM_001</c> catches names that do not exist. This
/// catches names that exist, are spelled correctly, and still do not compile
/// where the doc puts them: hooks exposed <i>only</i> as extension methods on
/// <c>RenderContext</c>/<c>Component</c>. Most hooks also have a
/// <c>protected</c> wrapper on <c>Component</c>, so <c>UseState(0)</c> binds
/// bare inside <c>Render()</c>; the ones that do not require an explicit
/// <c>this.</c> or <c>ctx.</c> receiver, and a bare call is a compile error.</para>
///
/// <para>Three of these shipped — one in a guide template and two in agent-kit
/// markdown packed into the NuGet, which is what an AI assistant reads when it
/// writes Reactor code. Nothing flagged them: they compile nowhere, but no
/// unchecked doc surface is compiled.</para>
///
/// <para><b>The set is computed from source, never hand-listed.</b> A literal
/// list would silently stop covering a hook the moment one was added or gained
/// a wrapper — the same rot this PR exists to stop.</para>
/// </summary>
public sealed class ReceiverRequiredHookDocTests
{
    readonly ITestOutputHelper _output;

    public ReceiverRequiredHookDocTests(ITestOutputHelper output) => _output = output;

    /// <summary>Hook declared as an extension on RenderContext or Component.</summary>
    static readonly Regex ExtensionHook = new(
        @"public\s+static\s+[^\r\n]*?\b(?<name>Use[A-Za-z0-9_]*)\s*(?:<[^>()]*>)?\s*\(\s*this\s+(?:RenderContext|Component)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// A bare call: not preceded by '.', and not part of a longer identifier.
    /// The negative lookbehind is the whole rule — <c>ctx.UseElementRef</c> and
    /// <c>this.UseFocusTrap</c> are exactly what we want to allow.
    /// </summary>
    static Regex BareCall(string name) => new(
        $@"(?<![A-Za-z0-9_.])(?<!\.)\b{Regex.Escape(name)}\s*(?:<[^<>()]*>)?\s*\(",
        RegexOptions.Compiled);

    static readonly string[] DocRoots =
    [
        "plugins", "skills",
        global::System.IO.Path.Combine("docs", "_pipeline", "templates"),
    ];

    [Fact]
    public void AgentKitAndTemplates_CallReceiverRequiredHooksWithAReceiver()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.False(string.IsNullOrEmpty(root), "repo root not found — the sweep is broken, not clean.");

        var receiverRequired = ComputeReceiverRequiredHooks(root!);

        // Instrument check. If this set is empty the sweep below can never fire,
        // and a green result would mean nothing.
        Assert.True(receiverRequired.Count > 0,
            "No receiver-required hooks discovered — the source scan is broken, not clean.");
        _output.WriteLine($"receiver-required hooks ({receiverRequired.Count}): {string.Join(", ", receiverRequired)}");

        var patterns = receiverRequired.ToDictionary(n => n, BareCall);
        var findings = new List<string>();
        int files = 0;

        foreach (var rel in DocRoots)
        {
            var dir = global::System.IO.Path.Combine(root!, rel);
            if (!global::System.IO.Directory.Exists(dir)) continue;

            foreach (var file in global::System.IO.Directory.EnumerateFiles(dir, "*.md*", global::System.IO.SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".md", global::System.StringComparison.Ordinal) &&
                    !file.EndsWith(".md.dt", global::System.StringComparison.Ordinal)) continue;

                files++;
                var relPath = global::System.IO.Path.GetRelativePath(root!, file).Replace('\\', '/');
                var lines = global::System.IO.File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');

                // Only fenced code blocks. Prose naming `UseFocusTrap(isActive)`
                // to describe the signature is not a call and must not fire.
                var inFence = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("```", global::System.StringComparison.Ordinal))
                    {
                        inFence = !inFence;
                        continue;
                    }
                    if (!inFence) continue;

                    foreach (var (name, rx) in patterns.Select(kv => (kv.Key, kv.Value)))
                        if (rx.IsMatch(lines[i]))
                            findings.Add($"  {relPath}:{i + 1} '{name}' needs an explicit receiver " +
                                         $"(this.{name}(...) or ctx.{name}(...)) — it is an extension method " +
                                         $"with no protected Component wrapper, so this does not compile.");
                }
            }
        }

        _output.WriteLine($"scanned {files} markdown file(s)");
        Assert.True(files > 0, "No markdown scanned — the sweep is broken, not clean.");

        Assert.True(findings.Count == 0,
            "A documented snippet calls an extension-only hook without a receiver, so it cannot compile.\n" +
            "These surfaces are never built, and agent-kit markdown is read by assistants writing\n" +
            "Reactor code, so the defect propagates into generated apps.\n" +
            string.Join("\n", findings));
    }

    /// <summary>
    /// Extension-only hooks: declared as <c>this RenderContext</c>/<c>this
    /// Component</c> extensions and <i>not</i> mirrored by a <c>protected</c>
    /// member on <c>Component</c>.
    /// </summary>
    static List<string> ComputeReceiverRequiredHooks(string root)
    {
        var src = global::System.IO.Path.Combine(root, "src", "Reactor");
        var extensions = new HashSet<string>(global::System.StringComparer.Ordinal);

        foreach (var file in global::System.IO.Directory.EnumerateFiles(src, "*.cs", global::System.IO.SearchOption.AllDirectories))
            foreach (Match m in ExtensionHook.Matches(global::System.IO.File.ReadAllText(file)))
                extensions.Add(m.Groups["name"].Value);

        var componentPath = global::System.IO.Path.Combine(src, "Core", "Component.cs");
        var component = global::System.IO.File.Exists(componentPath)
            ? global::System.IO.File.ReadAllText(componentPath)
            : "";

        // Matching the whole `protected ... Name(` line keeps tuple return types
        // like `(T, Action<T>) UseState<T>(` in scope; a type-shaped pattern
        // misses those and would wrongly call UseState receiver-required.
        return extensions
            .Where(n => !Regex.IsMatch(component, $@"protected[^\r\n]*\b{Regex.Escape(n)}\s*[<(]"))
            .OrderBy(n => n, global::System.StringComparer.Ordinal)
            .ToList();
    }
}
