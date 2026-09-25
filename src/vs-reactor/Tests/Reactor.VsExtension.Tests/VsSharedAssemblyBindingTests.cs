#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    /// <summary>
    /// Guards the assembly-version ceiling on framework libraries that Visual Studio ships as
    /// <em>shared assemblies</em> and binds on the extension's behalf.
    /// </summary>
    public sealed class VsSharedAssemblyBindingTests
    {
        // Visual Studio owns the identity of these assemblies inside devenv.exe: it ships one
        // copy under Common7\IDE\SharedAssemblies and binds every extension to it through a
        // devenv.exe.config <bindingRedirect>. VS 18 declares:
        //
        //   <assemblyIdentity name="System.Text.Json" publicKeyToken="cc7b13ffcd2ddd51" />
        //   <bindingRedirect oldVersion="0.0.0.0-10.0.0.10" newVersion="10.0.0.10" />
        //
        // A redirect unifies UPWARD, and only within its oldVersion range. Compile the
        // extension against a *higher* assembly version and the request falls outside the
        // range, so no redirect applies; the VSIX carries no private copy (VS strips
        // assemblies it provides) and the package registers no BindingPath, so the CLR probes
        // devenv's base directory, finds nothing, and throws:
        //
        //   FileNotFoundException: Could not load file or assembly
        //   'System.Text.Json, Version=10.0.0.12, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'
        //
        // That is not hypothetical: the repo-wide CPM pin (System.Text.Json 10.0.12 ->
        // AssemblyVersion 10.0.0.12) flowed into the extension and landed two servicing
        // revisions past VS 18's ceiling, killing every preview session at the first JSON call.
        //
        // The safe ceiling is the version Microsoft.VisualStudio.SDK itself depends on: every
        // VS release that satisfies the SDK redirects at least that high, and building lower is
        // always safe because redirects unify upward. Bump these only together with the
        // Microsoft.VisualStudio.SDK PackageVersion in Directory.Packages.props.
        private static readonly IReadOnlyDictionary<string, Version> Ceilings =
            new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase)
            {
                ["System.Text.Json"] = new Version(9, 0, 0, 0),
                ["System.Text.Encodings.Web"] = new Version(9, 0, 0, 0),
            };

        private static AssemblyName[] ExtensionReferences()
        {
            return typeof(EmbedClient).Assembly.GetReferencedAssemblies();
        }

        /// <summary>
        /// The ceiling above is only half the contract. A shared-assembly reference also has
        /// to be covered by the <em>oldest</em> Visual Studio the VSIX claims to install into,
        /// and that claim lives in source.extension.vsixmanifest — a different file that
        /// nothing otherwise ties to this pin.
        /// </summary>
        /// <remarks>
        /// Microsoft.VisualStudio.SDK baselines System.Text.Json per VS version: 17.8 -> 7.0.3,
        /// 17.9 -> 8.0.0, 17.14 -> 9.0.0. Referencing 9.0.0.0 while advertising 17.8 support
        /// would leave the extension dead on every host below 17.14 in exactly the way this
        /// PR fixes for VS 18, so the manifest minimum must not fall below the SDK whose
        /// baseline we pin to.
        /// </remarks>
        private static readonly Version MinimumSupportedVsHost = new Version(17, 14);

        [Fact]
        public void VsixManifest_DoesNotAdvertiseHostsOlderThanThePinnedBaseline()
        {
            var manifest = FindRepoFile(Path.Combine(
                "src", "vs-reactor", "Reactor.VsExtension", "source.extension.vsixmanifest"));

            var text = File.ReadAllText(manifest);
            var ranges = Regex.Matches(text, @"Version=""\[(?<min>\d+\.\d+)\s*,")
                .Cast<Match>()
                .Select(m => m.Groups["min"].Value)
                .ToArray();

            // Positive control: a manifest we failed to parse must not read as "no violations".
            Assert.NotEmpty(ranges);

            var tooOld = ranges
                .Select(v => new Version(v))
                .Where(v => v < MinimumSupportedVsHost)
                .Select(v => v.ToString())
                .Distinct()
                .ToArray();

            Assert.True(
                tooOld.Length == 0,
                $"source.extension.vsixmanifest advertises Visual Studio {string.Join(", ", tooOld)}, "
                + $"older than the {MinimumSupportedVsHost} baseline the extension's System.Text.Json pin "
                + "requires. Those hosts redirect System.Text.Json below the pinned version, so the "
                + "extension would fail to load there with FileNotFoundException. Raise the manifest "
                + "minimum, or lower the pin and this constant together.");
        }

        private static string FindRepoFile(string relativePath)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}. "
                + "The test cannot pass vacuously — fix the lookup rather than deleting the assertion.");
        }

        /// <summary>
        /// Positive control for <see cref="SharedAssemblyReferences_StayWithinVsBindingRedirectCeiling"/>:
        /// a clean result there is only meaningful if the probe can actually see the reference
        /// it gates. If the extension legitimately stops using System.Text.Json, delete this
        /// whole class rather than relaxing this assertion.
        /// </summary>
        [Fact]
        public void ExtensionReferences_IncludeAGatedSharedAssembly()
        {
            var names = ExtensionReferences().Select(r => r.Name).ToArray();

            Assert.Contains("System.Text.Json", names);
        }

        [Fact]
        public void SharedAssemblyReferences_StayWithinVsBindingRedirectCeiling()
        {
            var violations = ExtensionReferences()
                .Where(r => r.Name != null && r.Version != null)
                .Where(r => Ceilings.TryGetValue(r.Name!, out var ceiling) && r.Version! > ceiling)
                .Select(r => $"{r.Name} {r.Version} (ceiling {Ceilings[r.Name!]})")
                .ToArray();

            Assert.True(
                violations.Length == 0,
                "Reactor.VsExtension references a Visual Studio shared assembly at a higher version than "
                + "VS's binding redirect covers. This does not fail the build — it fails at run time "
                + "inside devenv with FileNotFoundException on the first call that touches the assembly. "
                + "Pin it with a VersionOverride in Reactor.VsExtension.csproj instead of following the "
                + "repo-wide Directory.Packages.props version. Offending references: "
                + string.Join(", ", violations));
        }
    }
}
