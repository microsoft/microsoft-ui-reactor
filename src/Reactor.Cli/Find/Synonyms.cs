#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Find;

internal static partial class Synonyms
{
    private static readonly FrozenDictionary<string, string> _phraseMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["content dialog"] = "contentdialog",
        ["context menu"] = "contextmenu",
        ["dark mode"] = "theme",
        ["data grid"] = "datagrid",
        ["global state"] = "context",
        ["infinite scroll"] = "infinite",
        ["master detail"] = "masterdetail",
        ["pull to refresh"] = "pulltorefresh",
        ["sidebar nav"] = "sidebar",
        ["theme switch"] = "theme",
        ["use context"] = "usecontext",
        ["use effect"] = "useeffect",
        ["use memo"] = "usememo",
        ["use mutation"] = "usemutation",
        ["use reducer"] = "usereducer",
        ["use ref"] = "useref",
        ["use resource"] = "useresource",
        ["use state"] = "usestate"
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string[]> _synonymMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["btn"] = ["button"],
        ["button"] = ["button"],
        ["card"] = ["card", "border"],
        ["div"] = ["flexrow", "flexcolumn", "vstack", "hstack"],
        ["drawer"] = ["navigationview", "splitview"],
        ["dropdown"] = ["combobox"],
        ["flex"] = ["flexrow", "flexcolumn"],
        ["form"] = ["formfield", "usevalidationcontext"],
        ["grid"] = ["grid", "gridview"],
        ["img"] = ["image"],
        ["input"] = ["textfield", "numberbox"],
        ["list"] = ["listview", "foreach"],
        ["loader"] = ["progressring", "progressbar"],
        ["modal"] = ["contentdialog", "dialog"],
        ["nav"] = ["navigationview", "usenavigation"],
        ["popup"] = ["flyout", "contentdialog"],
        ["select"] = ["combobox"],
        ["sidebar"] = ["navigationview", "splitview"],
        ["spinner"] = ["progressring"],
        ["tabs"] = ["tabview", "pivot"],
        ["toast"] = ["infobar"],
        ["txt"] = ["textblock", "textfield"],
        ["usecallback"] = ["usecallback"],
        ["usecontext"] = ["usecontext"],
        ["useeffect"] = ["useeffect"],
        ["usememo"] = ["usememo"],
        ["usereducer"] = ["usereducer"],
        ["useref"] = ["useref"],
        ["usestate"] = ["usestate"]
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string CollapsePhrase(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var collapsed = query.ToLowerInvariant();
        foreach (var (phrase, token) in _phraseMap)
        {
            collapsed = Regex.Replace(
                collapsed,
                $@"\b{Regex.Escape(phrase)}\b",
                token,
                RegexOptions.CultureInvariant);
        }

        return collapsed;
    }

    public static string[] Expand(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        var normalized = term.ToLowerInvariant();
        return _synonymMap.TryGetValue(normalized, out var expanded)
            ? expanded
            : [normalized];
    }

    public static string[] ProcessQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Tokenize(CollapsePhrase(query))
            .Where(term => !StopWords.IsStopWord(term))
            .SelectMany(Expand)
            .Where(term => !StopWords.IsStopWord(term))
            .ToArray();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match match in TokenRegex().Matches(text.ToLowerInvariant()))
        {
            if (match.Value.Length > 0)
            {
                yield return match.Value;
            }
        }
    }

    [GeneratedRegex("[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
