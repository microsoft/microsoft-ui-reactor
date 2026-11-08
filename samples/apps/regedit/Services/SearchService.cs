using ReactorRegedit.Core;
using ReactorRegedit.Models;

namespace ReactorRegedit.Services;

/// <summary>A Find hit: the key, and the value when the match was a value name or data.</summary>
internal sealed record SearchHit(string KeyPath, string? ValueName);

/// <summary>
/// regedit-style Find Next: walks the registry in tree order starting just after the
/// current position (key + optional value) and returns the first match.
/// Unreadable keys are skipped silently, like regedit.
/// </summary>
internal sealed class SearchService(string text, FindFlags flags, CancellationToken ct, Action<string>? progress = null)
{
    private readonly bool _whole = flags.HasFlag(FindFlags.WholeStringOnly);
    private long _visited;

    public static Task<SearchHit?> FindNextAsync(string startKey, string? startValue, string text, FindFlags flags,
        CancellationToken ct, Action<string>? progress = null) =>
        Task.Run(() => new SearchService(text, flags, ct, progress).FindNext(startKey, startValue), ct);

    public SearchHit? FindNext(string startKey, string? startValue)
    {
        if (startKey.Length == 0)
        {
            foreach (var hive in RegPath.RootNames)
            {
                if (Search(hive, checkName: true) is { } hit) return hit;
            }
            return null;
        }

        // Rest of the current key's values, then its subtree.
        if (SearchValues(startKey, startValue) is { } v) return v;
        if (SearchChildren(startKey) is { } c) return c;

        // Then later siblings of every ancestor, bottom-up.
        var cur = startKey;
        while (!RegPath.IsHive(cur))
        {
            var parent = RegPath.Parent(cur);
            var leaf = RegPath.Leaf(cur);
            foreach (var sibling in SubKeys(parent).SkipWhile(n => !n.Equals(leaf, StringComparison.OrdinalIgnoreCase)).Skip(1))
            {
                if (Search(parent + "\\" + sibling, checkName: true) is { } hit) return hit;
            }
            cur = parent;
        }

        // Then the hives after this one.
        var hiveIndex = Array.IndexOf(RegPath.RootNames, RegPath.Hive(startKey));
        for (int i = hiveIndex + 1; i < RegPath.RootNames.Length && hiveIndex >= 0; i++)
        {
            if (Search(RegPath.RootNames[i], checkName: true) is { } hit) return hit;
        }
        return null;
    }

    private SearchHit? Search(string path, bool checkName)
    {
        ct.ThrowIfCancellationRequested();
        if (++_visited % 200 == 0) progress?.Invoke(path);
        if (checkName && flags.HasFlag(FindFlags.Keys) && Matches(RegPath.Leaf(path))) return new SearchHit(path, null);
        return SearchValues(path, null) ?? SearchChildren(path);
    }

    private SearchHit? SearchChildren(string path)
    {
        foreach (var name in SubKeys(path))
        {
            if (Search(path + "\\" + name, checkName: true) is { } hit) return hit;
        }
        return null;
    }

    private SearchHit? SearchValues(string path, string? after)
    {
        if (!flags.HasFlag(FindFlags.Values) && !flags.HasFlag(FindFlags.Data)) return null;
        List<RegValue> values;
        try
        {
            using var key = RegistryService.OpenKey(path);
            values = RegistryService.ReadAllValues(key, path);
        }
        catch (RegistryOperationException) { return null; }

        var ordered = ValueSort.Sort(values, ValueSortColumn.Name, false);
        bool past = after is null;
        foreach (var v in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (!past)
            {
                if (v.Name.Equals(after, StringComparison.OrdinalIgnoreCase)) past = true;
                continue;
            }
            if (flags.HasFlag(FindFlags.Values) && v.Name.Length > 0 && Matches(v.Name)) return new SearchHit(path, v.Name);
            if (flags.HasFlag(FindFlags.Data) && DataMatches(v)) return new SearchHit(path, v.Name);
        }
        return null;
    }

    private bool DataMatches(RegValue v) => v.Type switch
    {
        RegType.Sz or RegType.ExpandSz => Matches(ValueFormat.DecodeString(v.Data)),
        RegType.MultiSz => ValueFormat.DecodeMultiString(v.Data).Any(Matches),
        _ => false,
    };

    private bool Matches(string candidate) => _whole
        ? candidate.Equals(text, StringComparison.OrdinalIgnoreCase)
        : candidate.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SubKeys(string path)
    {
        try
        {
            using var key = RegistryService.OpenKey(path);
            var names = key.GetSubKeyNames();
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }
        catch (Exception ex) when (ex is RegistryOperationException or System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
