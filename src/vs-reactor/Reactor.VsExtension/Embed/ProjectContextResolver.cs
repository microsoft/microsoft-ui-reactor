#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.VsExtension.Embed
{
    internal static class ProjectContextResolver
    {
        private static readonly Regex ComponentClassRegex = new Regex(@"class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*Component(?:<[^>]*>)?\b", RegexOptions.Compiled);
        private static readonly HashSet<string> ExcludedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "obj",
            "bin",
            ".git",
            ".vs",
        };

        /// <summary>
        /// Find the .csproj that contains <paramref name="filePath"/> by walking parent
        /// directories. Returns the first .csproj found (no preference among siblings).
        /// </summary>
        public static string? FindContainingCsproj(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var dir = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
            while (!string.IsNullOrEmpty(dir))
            {
                try
                {
                    var matches = Directory.GetFiles(dir, "*.csproj");
                    if (matches.Length > 0)
                    {
                        return matches[0];
                    }
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        /// <summary>
        /// Parse a C# source file and return the names of all classes that extend
        /// <c>Component</c> (Reactor's component base class). Uses a regex matching the TS
        /// extension's <c>findAllComponentClasses</c>.
        /// </summary>
        public static IReadOnlyList<string> FindComponentClasses(string sourceCode)
        {
            if (string.IsNullOrEmpty(sourceCode))
            {
                return Array.Empty<string>();
            }

            // The regex is the VS Code extension regex ported verbatim:
            // /class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*Component(?:<[^>]*>)?\b/g
            // We strip comments/strings first so line-leading comments do not produce false positives.
            var sanitized = StripCommentsAndStrings(sourceCode);
            var results = new List<string>();
            foreach (Match match in ComponentClassRegex.Matches(sanitized))
            {
                var name = match.Groups[1].Value;
                if (!string.Equals(name, "Component", StringComparison.Ordinal))
                {
                    results.Add(name);
                }
            }

            return results;
        }

        /// <summary>
        /// Find all component classes across all .cs files under the csproj's directory tree.
        /// Excludes obj/bin/.git/.vs.
        /// </summary>
        public static IReadOnlyList<string> FindAllComponentsInProject(string csprojPath)
        {
            if (string.IsNullOrWhiteSpace(csprojPath))
            {
                return Array.Empty<string>();
            }

            var root = Path.GetDirectoryName(csprojPath);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            var results = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var file in SafeEnumerateFiles(dir, "*.cs"))
                {
                    try
                    {
                        results.AddRange(FindComponentClasses(File.ReadAllText(file)));
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                foreach (var child in SafeEnumerateDirectories(dir).Where(d => !ExcludedDirectoryNames.Contains(Path.GetFileName(d))))
                {
                    pending.Push(child);
                }
            }

            return results;
        }

        private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(dir, pattern).ToArray();
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string dir)
        {
            try
            {
                return Directory.EnumerateDirectories(dir).ToArray();
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static string StripCommentsAndStrings(string text)
        {
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (ch == '/' && next == '/')
                {
                    builder.Append(' ');
                    builder.Append(' ');
                    i += 2;
                    while (i < text.Length && text[i] != '\r' && text[i] != '\n')
                    {
                        builder.Append(' ');
                        i++;
                    }

                    if (i < text.Length)
                    {
                        builder.Append(text[i]);
                    }

                    continue;
                }

                if (ch == '/' && next == '*')
                {
                    builder.Append(' ');
                    builder.Append(' ');
                    i += 2;
                    while (i < text.Length)
                    {
                        if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
                        {
                            builder.Append(' ');
                            builder.Append(' ');
                            i++;
                            break;
                        }

                        builder.Append(text[i] == '\r' || text[i] == '\n' ? text[i] : ' ');
                        i++;
                    }

                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    i = StripQuotedLiteral(text, i, builder);
                    continue;
                }

                if (ch == '@' && next == '"')
                {
                    builder.Append(' ');
                    i = StripVerbatimString(text, i + 1, builder);
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static int StripQuotedLiteral(string text, int start, StringBuilder builder)
        {
            var quote = text[start];
            builder.Append(' ');
            for (var i = start + 1; i < text.Length; i++)
            {
                var ch = text[i];
                builder.Append(ch == '\r' || ch == '\n' ? ch : ' ');
                if (ch == '\\' && i + 1 < text.Length)
                {
                    i++;
                    builder.Append(text[i] == '\r' || text[i] == '\n' ? text[i] : ' ');
                    continue;
                }

                if (ch == quote)
                {
                    return i;
                }
            }

            return text.Length - 1;
        }

        private static int StripVerbatimString(string text, int quoteIndex, StringBuilder builder)
        {
            builder.Append(' ');
            for (var i = quoteIndex + 1; i < text.Length; i++)
            {
                var ch = text[i];
                builder.Append(ch == '\r' || ch == '\n' ? ch : ' ');
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i++;
                        builder.Append(' ');
                        continue;
                    }

                    return i;
                }
            }

            return text.Length - 1;
        }
    }
}
