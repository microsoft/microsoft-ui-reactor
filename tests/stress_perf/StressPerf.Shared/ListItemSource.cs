using System.Runtime.CompilerServices;

namespace StressPerf.Shared;

/// <summary>
/// Deterministic feed item generator for the virtualizing-list stress demo.
/// Mirrored byte-for-byte by the React Native port at
/// <c>tests/stress_perf_rn/VirtualList/ListItemSource.ts</c> — keep the pools
/// and formulas in sync so both apps render identical content.
/// </summary>
public sealed class ListItemSource
{
    public static readonly string[] Names = new[]
    {
        "Alex Carter", "Bailey Nguyen", "Casey Wu", "Devon Patel",
        "Erin Sato", "Finley Brooks", "Gray Romero", "Harper Lin",
        "Indra Khan", "Jules Vega", "Kai Holm", "Lane Park",
        "Morgan Diaz", "Nico Tran", "Owen Reyes", "Parker Yates",
    };

    public static readonly string[] Categories = new[]
    {
        "Engineering", "Design", "Marketing", "Sales",
        "Support", "Operations", "Research", "Finance",
    };

    public static readonly string[] Adjectives = new[]
    {
        "quick", "lazy", "eager", "calm", "bright", "rough",
        "smooth", "sharp", "dim", "bold", "shy", "warm",
    };

    public static readonly string[] Nouns = new[]
    {
        "report", "thread", "ticket", "review", "draft", "sync",
        "build", "deploy", "spike", "demo", "pitch", "audit",
    };

    public static readonly string[] Tags = new[]
    {
        "ux", "perf", "infra", "client", "api", "css",
        "shipit", "frontend", "backend", "release", "hotfix", "wip",
    };

    public const int RowHeight = 76;
    public const int AvatarSize = 48;

    /// <summary>2026-01-01T09:00:00Z. Mirrored in TS.</summary>
    public static readonly DateTime BaseDate = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    public readonly record struct ListItem(
        int Id,
        string Name,
        string Category,
        string Message,
        string Timestamp,
        string Tag,
        int Likes,
        int AvatarHue,
        char Initial);

    /// <summary>Generate exactly <paramref name="count"/> deterministic items.</summary>
    public static ListItem[] Generate(int count)
    {
        var items = new ListItem[count];
        for (int i = 0; i < count; i++)
            items[i] = ItemAt(i);
        return items;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ListItem ItemAt(int i)
    {
        string name = Names[Mod(i, Names.Length)];
        string category = Categories[Mod(i / 7, Categories.Length)];
        string adjective = Adjectives[Mod(i * 3, Adjectives.Length)];
        string noun = Nouns[Mod(i * 5 + 2, Nouns.Length)];
        string message = $"{adjective} {noun} #{i}";
        var dt = BaseDate.AddMinutes(i);
        string timestamp = dt.ToString("MMM dd HH:mm");
        string tag = Tags[Mod(i * 31, Tags.Length)];
        // Linear congruential generator for a stable per-row likes count.
        // (Same constants in TS — Knuth/Numerical Recipes.)
        uint h = (uint)((i * 1664525u) + 1013904223u);
        int likes = (int)(h % 999u);
        int hue = Mod(i * 137, 360);
        char initial = name[0];
        return new ListItem(i, name, category, message, timestamp, tag, likes, hue, initial);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Mod(int x, int n)
    {
        int r = x % n;
        return r < 0 ? r + n : r;
    }
}
