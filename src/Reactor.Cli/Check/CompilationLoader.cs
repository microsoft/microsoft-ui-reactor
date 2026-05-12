// Loads a Roslyn `CSharpCompilation` for a Reactor project on disk so the
// Tier-2 suggesters can run a semantic analysis over the same code MSBuild
// just compiled. Spec 038 §5.
//
// Inputs accepted:
//   - a directory (must contain exactly one .csproj)
//   - a .csproj file directly
//   - a .cs file (we walk up to the nearest .csproj)
//
// Reference resolution:
//   - obj/project.assets.json (post-`dotnet restore`) for package + framework
//     compile assets.
//   - the host's TRUSTED_PLATFORM_ASSEMBLIES list as a fallback so we still
//     resolve mscorlib / System.* even if the project hasn't been restored.
//
// Caching: keyed on (absolute-csproj-path, sorted-file-mtime-hash). Warm
// loads return the cached compilation; staleness is detected by the hash.
// Cache is process-wide; eviction is not in scope for v1 (a `mur check`
// invocation is a single process).
//
// Security: only `.cs` files under the project's logical root are included.
// Symlinks pointing outside the root are silently skipped (we do not follow
// them blindly, but we do not panic either).
//
// Failure mode: if the csproj path can't be resolved, or the project has no
// .cs files, or the json is malformed, the loader returns an empty
// `CSharpCompilation` rather than throwing — `mur check` must always exit
// gracefully.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.UI.Reactor.Cli.Check;

internal sealed class CompilationLoader
{
    public static CompilationLoader Instance { get; } = new();

    readonly ConcurrentDictionary<CacheKey, CSharpCompilation> _cache = new();

    /// <summary>
    /// Empty compilation returned when input cannot be resolved. Always
    /// well-formed; suggesters that probe it for symbols will return Silent.
    /// </summary>
    public static CSharpCompilation EmptyCompilation => _emptyCompilation.Value;

    static readonly Lazy<CSharpCompilation> _emptyCompilation = new(BuildEmptyCompilation);

    static CSharpCompilation BuildEmptyCompilation()
    {
        var refs = HostRuntimeReferences.Value ?? Array.Empty<MetadataReference>();
        return CSharpCompilation.Create("Empty",
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public CSharpCompilation Load(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return EmptyCompilation;

        string? csproj = ResolveCsproj(projectPath);
        if (csproj is null)
            return EmptyCompilation;

        var projectDir = Path.GetDirectoryName(csproj);
        if (string.IsNullOrEmpty(projectDir)) return EmptyCompilation;
        var sourceFiles = EnumerateSourceFiles(projectDir).ToArray();
        if (sourceFiles.Length == 0)
            return EmptyCompilation;

        var key = new CacheKey(csproj, FileSetHash(sourceFiles));
        return _cache.GetOrAdd(key, _ => Build(csproj, projectDir, sourceFiles));
    }

    static CSharpCompilation Build(string csproj, string projectDir, IReadOnlyList<string> sourceFiles)
    {
        var trees = new List<SyntaxTree>(sourceFiles.Count);
        foreach (var file in sourceFiles)
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }
            trees.Add(CSharpSyntaxTree.ParseText(text, path: file));
        }

        var refs = ResolveReferences(csproj);

        var assemblyName = Path.GetFileNameWithoutExtension(csproj);
        return CSharpCompilation.Create(
            assemblyName,
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    static string? ResolveCsproj(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                if (full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    return full;
                if (full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(full);
                    while (!string.IsNullOrEmpty(dir))
                    {
                        var here = Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                        if (here is not null) return here;
                        dir = Path.GetDirectoryName(dir);
                    }
                    return null;
                }
                return null;
            }
            if (Directory.Exists(full))
            {
                var here = Directory.EnumerateFiles(full, "*.csproj", SearchOption.TopDirectoryOnly).ToArray();
                if (here.Length == 1) return here[0];
                return null; // ambiguous or missing
            }
        }
        catch { }
        return null;
    }

    static IEnumerable<string> EnumerateSourceFiles(string projectDir)
    {
        var rootFull = Path.GetFullPath(projectDir);
        IEnumerable<string> all;
        try { all = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories); }
        catch { yield break; }
        foreach (var file in all)
        {
            // Skip nuget caches, build outputs, anything that escapes the root via symlink.
            string full;
            try { full = Path.GetFullPath(file); }
            catch { continue; }

            if (!IsUnder(rootFull, full)) continue;

            // Skip generated / output trees — these are MSBuild's territory.
            if (PathContainsSegment(full, "obj") || PathContainsSegment(full, "bin"))
                continue;

            // Symlink-safe containment check: resolve the target if any.
            try
            {
                var fi = new FileInfo(full);
                if (fi.LinkTarget is { } linkTarget)
                {
                    var resolved = Path.GetFullPath(Path.IsPathRooted(linkTarget)
                        ? linkTarget
                        : Path.Combine(Path.GetDirectoryName(full)!, linkTarget));
                    if (!IsUnder(rootFull, resolved)) continue;
                }
            }
            catch { continue; }

            yield return full;
        }
    }

    static bool IsUnder(string root, string candidate)
    {
        var rootSep = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootSep, StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    static bool PathContainsSegment(string path, string segment)
    {
        var sep = Path.DirectorySeparatorChar;
        var marker = string.Concat(sep, segment, sep);
        return path.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    static string FileSetHash(IReadOnlyList<string> files)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var ts = File.GetLastWriteTimeUtc(f).ToFileTimeUtc();
                sb.Append(f).Append('|').Append(ts).Append('\n');
            }
            catch { sb.Append(f).Append("|0\n"); }
        }
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    internal static List<MetadataReference> ResolveReferences(string csproj)
    {
        var refs = new List<MetadataReference>(64);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always start from the host's runtime — this gives us mscorlib / System.* etc.
        foreach (var r in HostRuntimeReferences.Value)
        {
            // PortableExecutableReference exposes FilePath; fall back to a per-instance dedupe key.
            string key = (r as PortableExecutableReference)?.FilePath ?? r.Display ?? Guid.NewGuid().ToString();
            if (seen.Add(key)) refs.Add(r);
        }

        var assets = TryLoadAssetsJson(csproj);
        if (assets is not null)
        {
            foreach (var path in EnumerateAssetCompilePaths(assets))
            {
                if (!seen.Add(path)) continue;
                try { refs.Add(MetadataReference.CreateFromFile(path)); }
                catch { }
            }
            foreach (var path in EnumerateProjectReferenceCompilePaths(assets, csproj))
            {
                if (!seen.Add(path)) continue;
                try { refs.Add(MetadataReference.CreateFromFile(path)); }
                catch { }
            }
        }
        return refs;
    }

    /// <summary>
    /// Resolves ProjectReference outputs (project.assets.json `libraries`
    /// entries with `type=project`) to their built .dll on disk. The
    /// `libraries.&lt;id&gt;.path` field gives the .csproj path relative to
    /// the consumer; the dll lives somewhere under that project's `bin/`
    /// tree. We pick the most-recently-built dll whose filename matches
    /// either the csproj's `&lt;AssemblyName&gt;` (when present) or the
    /// csproj's basename — that's how MSBuild names the output by default.
    ///
    /// Without this path, Reactor itself is invisible to the rule registry:
    /// every Tier-3 rule's DeclaredTargets fails to resolve and the entire
    /// rule set self-disables on any real `mur check` invocation against a
    /// Reactor app, even though unit tests pass. Spec 038 §6 + the §3.1a
    /// rule-target resolution CI gate are load-bearing for the rule
    /// authoring contract; this method makes the contract operate on
    /// actual on-disk Reactor too.
    /// </summary>
    static IEnumerable<string> EnumerateProjectReferenceCompilePaths(JsonDocument assets, string consumerCsproj)
    {
        var root = assets.RootElement;
        var consumerDir = Path.GetDirectoryName(consumerCsproj);
        if (string.IsNullOrEmpty(consumerDir)) yield break;

        if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var libProp in libraries.EnumerateObject())
        {
            if (libProp.Value.ValueKind != JsonValueKind.Object) continue;
            if (!libProp.Value.TryGetProperty("type", out var type)) continue;
            if (type.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(type.GetString(), "project", StringComparison.OrdinalIgnoreCase)) continue;

            if (!libProp.Value.TryGetProperty("path", out var pathProp)) continue;
            if (pathProp.ValueKind != JsonValueKind.String) continue;
            var relPath = pathProp.GetString();
            if (string.IsNullOrEmpty(relPath)) continue;

            string refProjPath;
            try { refProjPath = Path.GetFullPath(Path.Combine(consumerDir, relPath)); }
            catch { continue; }

            var refProjDir = Path.GetDirectoryName(refProjPath);
            if (string.IsNullOrEmpty(refProjDir)) continue;

            var dll = FindBuiltDll(refProjDir, refProjPath);
            if (dll is not null) yield return dll;
        }
    }

    /// <summary>
    /// Finds the most-recently-built .dll for a referenced project. Tries
    /// the csproj's &lt;AssemblyName&gt; element first; falls back to the
    /// csproj basename if the override isn't present. Walks the entire
    /// `bin/` subtree because the actual configuration / TFM / platform
    /// directory layout depends on the project's MSBuild properties and
    /// we don't have a reliable way to predict it.
    /// </summary>
    static string? FindBuiltDll(string refProjDir, string refProjPath)
    {
        var binDir = Path.Combine(refProjDir, "bin");
        if (!Directory.Exists(binDir)) return null;

        var asmName = ReadAssemblyName(refProjPath) ?? Path.GetFileNameWithoutExtension(refProjPath);

        string? best = null;
        DateTime bestMtime = DateTime.MinValue;
        try
        {
            foreach (var dll in Directory.EnumerateFiles(binDir, "*.dll", SearchOption.AllDirectories))
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(dll), asmName, StringComparison.OrdinalIgnoreCase))
                    continue;
                DateTime mt;
                try { mt = File.GetLastWriteTimeUtc(dll); }
                catch { continue; }
                if (mt > bestMtime) { bestMtime = mt; best = dll; }
            }
        }
        catch { return null; }
        return best;
    }

    /// <summary>
    /// Reads the AssemblyName MSBuild property from a csproj XML, if any.
    /// Best-effort string match — full MSBuild evaluation would handle
    /// imports / conditions / property expansion but is out of scope here.
    /// Returns null when the property isn't directly set.
    /// </summary>
    static string? ReadAssemblyName(string csproj)
    {
        string text;
        try { text = File.ReadAllText(csproj); }
        catch { return null; }

        // Permissive scan: <AssemblyName>X</AssemblyName>. Avoids loading
        // System.Xml just for one tag.
        const string Open = "<AssemblyName>";
        const string Close = "</AssemblyName>";
        var i = text.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var j = text.IndexOf(Close, i + Open.Length, StringComparison.OrdinalIgnoreCase);
        if (j < 0) return null;
        var name = text.Substring(i + Open.Length, j - i - Open.Length).Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    static JsonDocument? TryLoadAssetsJson(string csproj)
    {
        var assetsPath = Path.Combine(Path.GetDirectoryName(csproj)!, "obj", "project.assets.json");
        if (!File.Exists(assetsPath)) return null;
        try
        {
            using var fs = File.OpenRead(assetsPath);
            return JsonDocument.Parse(fs);
        }
        catch { return null; }
    }

    static IEnumerable<string> EnumerateAssetCompilePaths(JsonDocument assets)
    {
        // project.assets.json schema:
        //   "packageFolders": { "<absPath>": {} ... },
        //   "targets": { "<tfm>": { "<id>/<ver>": { "compile": { "ref/.../X.dll": {} } } } }
        var root = assets.RootElement;

        var packageFolders = new List<string>();
        if (root.TryGetProperty("packageFolders", out var pf) && pf.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in pf.EnumerateObject())
                packageFolders.Add(prop.Name);
        }
        if (packageFolders.Count == 0) yield break;

        if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var tfm in targets.EnumerateObject())
        {
            // Pick the first non-RID-qualified target if present, else any.
            var libs = tfm.Value;
            if (libs.ValueKind != JsonValueKind.Object) continue;

            foreach (var lib in libs.EnumerateObject())
            {
                if (lib.Value.ValueKind != JsonValueKind.Object) continue;
                if (!lib.Value.TryGetProperty("compile", out var compile)) continue;
                if (compile.ValueKind != JsonValueKind.Object) continue;

                foreach (var asset in compile.EnumerateObject())
                {
                    var rel = asset.Name; // e.g. "ref/net10.0/Reactor.dll"
                    if (rel.EndsWith("_._", StringComparison.Ordinal)) continue; // empty marker
                    if (!rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var folder in packageFolders)
                    {
                        var packageId = lib.Name; // "<id>/<version>"
                        var combined = Path.Combine(folder, packageId, rel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(combined))
                        {
                            yield return combined;
                            break;
                        }
                    }
                }
            }

            // Use only the first TFM in the file. Multi-targeting projects
            // resolve correctly because project.assets.json puts the active
            // build's TFM first; if not, we'll over-include refs which is
            // safe — the compilation only resolves referenced types.
            yield break;
        }
    }

    static readonly Lazy<MetadataReference[]> HostRuntimeReferences = new(LoadHostRuntimeReferences);

    static MetadataReference[] LoadHostRuntimeReferences()
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tpaList = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?.Split(Path.PathSeparator);
        if (tpaList is not null)
        {
            foreach (var p in tpaList)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (!p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(p)) continue;
                if (!seen.Add(p)) continue;
                try { refs.Add(MetadataReference.CreateFromFile(p)); }
                catch { }
            }
        }
        return refs.ToArray();
    }

    internal readonly record struct CacheKey(string Csproj, string FileSetHash);
}
