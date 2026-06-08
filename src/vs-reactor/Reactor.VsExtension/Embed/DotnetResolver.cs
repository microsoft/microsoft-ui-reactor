#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.UI.Reactor.VsExtension.Embed
{
    internal static class DotnetResolver
    {
        private static readonly string[] CandidateNames =
        {
            "dotnet.exe",
            "dotnet.cmd",
            "dotnet.bat",
            "dotnet.com",
        };

        internal sealed record Result(string Path, string Source);

        public static Result? Resolve(string workspaceRoot, IReadOnlyDictionary<string, string>? envOverride = null)
        {
            if (workspaceRoot == null)
            {
                throw new ArgumentNullException(nameof(workspaceRoot));
            }

            var workspaceFullPath = NormalizeDirectoryForCompare(workspaceRoot);

            var dotnetHostPath = GetEnvironmentValue("DOTNET_HOST_PATH", envOverride);
            if (!string.IsNullOrWhiteSpace(dotnetHostPath))
            {
                var hostResult = TryResolveCandidate(dotnetHostPath!, "DOTNET_HOST_PATH", workspaceFullPath);
                if (hostResult != null)
                {
                    return hostResult;
                }
            }

            var pathEnv = GetEnvironmentValue("PATH", envOverride) ?? GetEnvironmentValue("Path", envOverride) ?? GetEnvironmentValue("path", envOverride);
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var rawEntry in pathEnv!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var entry = rawEntry.Trim().Trim('"');
                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    string directoryFullPath;
                    try
                    {
                        directoryFullPath = NormalizeDirectoryForCompare(Environment.ExpandEnvironmentVariables(entry));
                    }
                    catch (Exception) when (IsPathException())
                    {
                        continue;
                    }

                    if (IsInsideOrSame(directoryFullPath, workspaceFullPath))
                    {
                        continue;
                    }

                    foreach (var candidateName in CandidateNames)
                    {
                        var candidate = System.IO.Path.Combine(directoryFullPath, candidateName);
                        var result = TryResolveCandidate(candidate, "PATH", workspaceFullPath);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }

            var programFiles = GetEnvironmentValue("ProgramFiles", envOverride);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            }

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                var candidate = System.IO.Path.Combine(programFiles!, "dotnet", "dotnet.exe");
                var result = TryResolveCandidate(candidate, "ProgramFiles", workspaceFullPath);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static Result? TryResolveCandidate(string candidate, string source, string workspaceFullPath)
        {
            string candidateFullPath;
            try
            {
                candidateFullPath = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
            }
            catch (Exception) when (IsPathException())
            {
                return null;
            }

            if (!File.Exists(candidateFullPath))
            {
                return null;
            }

            var realPath = GetFinalPath(candidateFullPath);
            var realComparePath = NormalizeFileForCompare(realPath);
            if (IsInsideOrSame(realComparePath, workspaceFullPath))
            {
                return null;
            }

            return new Result(candidateFullPath, source);
        }

        private static string? GetEnvironmentValue(string name, IReadOnlyDictionary<string, string>? envOverride)
        {
            if (envOverride == null)
            {
                return Environment.GetEnvironmentVariable(name);
            }

            if (envOverride.TryGetValue(name, out var value))
            {
                return value;
            }

            var pair = envOverride.FirstOrDefault(item => string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrEmpty(pair.Key) ? null : pair.Value;
        }

        private static string GetFinalPath(string path)
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            using (var handle = EmbedNativeMethods.CreateFileW(
                fullPath,
                0,
                EmbedNativeMethods.FILE_SHARE_READ | EmbedNativeMethods.FILE_SHARE_WRITE | EmbedNativeMethods.FILE_SHARE_DELETE,
                IntPtr.Zero,
                EmbedNativeMethods.OPEN_EXISTING,
                EmbedNativeMethods.FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero))
            {
                if (handle == null || handle.IsInvalid)
                {
                    return fullPath;
                }

                var capacity = 512;
                while (capacity <= 32768)
                {
                    var builder = new StringBuilder(capacity);
                    var length = EmbedNativeMethods.GetFinalPathNameByHandleW(handle, builder, (uint)builder.Capacity, EmbedNativeMethods.VOLUME_NAME_DOS);
                    if (length == 0)
                    {
                        return fullPath;
                    }

                    if (length < builder.Capacity)
                    {
                        return NormalizeFinalPath(builder.ToString());
                    }

                    capacity = checked((int)length + 1);
                }
            }

            return fullPath;
        }

        private static string NormalizeFinalPath(string path)
        {
            const string extendedPrefix = @"\\?\";
            const string uncPrefix = @"\\?\UNC\";

            if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + path.Substring(uncPrefix.Length);
            }

            if (path.StartsWith(extendedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(extendedPrefix.Length);
            }

            return path;
        }

        private static string NormalizeDirectoryForCompare(string path)
        {
            var full = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            return TrimTrailingSeparators(full);
        }

        private static string NormalizeFileForCompare(string path)
        {
            var full = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            return TrimTrailingSeparators(full);
        }

        private static string TrimTrailingSeparators(string path)
        {
            var root = System.IO.Path.GetPathRoot(path);
            while (path.Length > root.Length && (path[path.Length - 1] == System.IO.Path.DirectorySeparatorChar || path[path.Length - 1] == System.IO.Path.AltDirectorySeparatorChar))
            {
                path = path.Substring(0, path.Length - 1);
            }

            return path;
        }

        private static bool IsInsideOrSame(string candidateFullPath, string workspaceFullPath)
        {
            if (string.Equals(candidateFullPath, workspaceFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return candidateFullPath.StartsWith(workspaceFullPath + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || candidateFullPath.StartsWith(workspaceFullPath + System.IO.Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathException()
        {
            return true;
        }
    }
}
