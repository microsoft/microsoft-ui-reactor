using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace WidgetCreator.Services;

/// <summary>
/// Compiled-in SHA-256 pins for the vendored MXC runtime binaries (threat-model
/// C-3). MXC is the entire runtime security bar, so the binaries that enforce it
/// must not be silently swapped for a no-op or a weakened build. These expected
/// hashes live in the app's own (trusted, compiled) code — an attacker who
/// replaces a vendored <c>.exe</c> in the output directory cannot also change the
/// pin without recompiling Widget Creator itself.
///
/// <para><b>Refreshing:</b> when the vendored binaries under <c>tools/mxc/&lt;rid&gt;/</c>
/// change (see <c>tools/mxc/README.md</c>), recompute and update the hashes below in
/// the same change, e.g.:
/// <code>Get-FileHash tools\mxc\win-arm64\*.exe -Algorithm SHA256</code></para>
/// </summary>
public static class MxcBinaryManifest
{
    static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Expected =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["win-arm64"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winhttp-proxy-shim.exe"] = "aca7cd110b09a1045c53a5c9bd73089f63e6c66f0b3d2627a5bdfb55bb524fdc",
                ["wxc-exec.exe"] = "e430d0e4f44f616e91db684f8d825a6dc93e06a1262b8d00bcaac7522a317aab",
                ["wxc-host-prep.exe"] = "3ef702332286a39153fc259310b5021e3de3c191751d7522684f6475f73af5ef",
                ["wxc-test-proxy.exe"] = "1d1a5821a65c9b4aceb2f1788ca54b08d06b92b784daa1926ab978f4a49f1f00",
                ["wxc-windows-sandbox-daemon.exe"] = "fc8079bddf5db77ee4ecea91d7f22a543fbec3618945a7ab97269dcfef3f66b1",
                ["wxc-windows-sandbox-guest.exe"] = "69c972ce4a65d337f15d828e40b92fcb2f89665d32f2cb598606b45c76adfde3",
            },
            ["win-x64"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winhttp-proxy-shim.exe"] = "008ba8532fcef858b44fb97c805f67bfcbbcaa0750a72002d1be1766a21f54e4",
                ["wxc-exec.exe"] = "db0a3422be9e1b396cc1b2547c70ff16b27412438a31c10a45abf370cac86ae2",
                ["wxc-host-prep.exe"] = "531fb3cdb4b0c964908fd71b71d40961417afb399cbab72f92a25e95309a6416",
                ["wxc-test-proxy.exe"] = "35357427059c06cdbd1287543ef0482c49d2bb3676427ecb80bd37f0b41ca22e",
                ["wxc-windows-sandbox-daemon.exe"] = "36cef4598466935ce71916a96b2eba4712508208ba09a2cc95bddc7714534d74",
                ["wxc-windows-sandbox-guest.exe"] = "ae726a4af9200a20df8d80c5d2eef5f31bc2e832f32922374fcfeef579936847",
            },
        };

    /// <summary>True when this RID has a pin set (an unknown RID cannot be verified).</summary>
    public static bool HasManifest(string rid) => Expected.ContainsKey(rid);

    /// <summary>
    /// Verify every pinned binary for <paramref name="rid"/> exists in
    /// <paramref name="dir"/> and matches its expected SHA-256. Returns
    /// <c>null</c> on success, or a human-readable reason on the first failure.
    /// </summary>
    public static string? Verify(string rid, string dir)
    {
        if (!Expected.TryGetValue(rid, out var files))
            return $"no integrity manifest for RID '{rid}'";

        foreach (var (name, expected) in files)
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
                return $"vendored sandbox binary missing: {name}";

            var actual = Sha256(path);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return $"vendored sandbox binary '{name}' failed its integrity pin " +
                       $"(expected {Short(expected)}…, got {Short(actual)}…)";
        }
        return null;
    }

    static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    static string Short(string h) => h.Length <= 12 ? h : h[..12];
}
