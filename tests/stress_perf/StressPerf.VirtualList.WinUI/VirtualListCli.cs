namespace StressPerf.VirtualList.WinUI;

/// <summary>
/// CLI matches <c>StressPerf.VirtualList.Reactor</c> byte-for-byte so the
/// paired headless runs (spec 042 perf gate) read the same flags.
/// </summary>
public sealed class VirtualListCli
{
    public bool Headless { get; set; }
    public int Count { get; set; } = 5000;
    public int DurationSeconds { get; set; } = 5;

    /// <summary>Interleave random insert / remove ops with the scroll tween.</summary>
    public bool WithEdits { get; set; }

    /// <summary>Ops per second when <see cref="WithEdits"/> is on.</summary>
    public int EditsPerSecond { get; set; } = 4;

    public static VirtualListCli Parse(string[] args)
    {
        var o = new VirtualListCli();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless": o.Headless = true; break;
                case "--count" when i + 1 < args.Length: o.Count = int.Parse(args[++i]); break;
                case "--duration" when i + 1 < args.Length: o.DurationSeconds = int.Parse(args[++i]); break;
                case "--with-edits": o.WithEdits = true; break;
                case "--edits-per-second" when i + 1 < args.Length: o.EditsPerSecond = int.Parse(args[++i]); break;
            }
        }
        return o;
    }
}
