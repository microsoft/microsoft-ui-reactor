#nullable enable

namespace Microsoft.UI.Reactor.Cli.Find;

internal static class Notes
{
    public static string[]? GetNotes(string? notesKey)
    {
        if (notesKey is null) return null;
        return _notes.GetValueOrDefault(notesKey);
    }

    private static readonly Dictionary<string, string[]> _notes = new()
    {
        ["UseState"] =
        [
            "UseState with a List<T> does NOT re-render on `.Add()` / `.Remove()` — same reference. Use UseReducer for collections.",
            "UseState returns (value, setter). The setter is stable across renders — safe to omit from dependency arrays.",
            "Call UseState unconditionally at the top of Render. Hooks track slot identity by call order."
        ],
        ["UseEffect"] =
        [
            "Effects run AFTER render commits. Don't read state set inside the same render unless via UseEffect's cleanup or a deps change.",
            "Return a cleanup lambda when the effect subscribes to anything. The cleanup runs before the next effect AND on unmount.",
            "Empty deps `[]` means 'run once on mount' — but the effect still re-runs if the component remounts due to key change."
        ],
        ["lists"] =
        [
            "Lists produced by `items.Select(...).ToArray()` MUST include `.WithKey(item.Id)` on every element. Without keys, focus, animation, and child state drift across reorders.",
            "`UseState<List<T>>` mutating in place does not re-render. Use `UseReducer<TState, TAction>` or `UseCollection`."
        ],
        ["WithKey"] =
        [
            "Required on every element produced from `.Select(...)` inside a layout container. Without it the analyzer emits `REACTOR_DSL_001` and reordering breaks focus/animation.",
            "Key must be stable across renders. Don't key by index for reorderable lists — that defeats the purpose."
        ],
        ["ContentDialog"] =
        [
            "ContentDialog is non-routed — show it via `UseDialog().Show(...)` from a hook, not by mounting it as a child element.",
            "Primary/secondary/close buttons map to the three result branches. For yes/no/cancel, provide all three texts."
        ]
    };
}
