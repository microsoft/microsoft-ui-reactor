using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Kind of structural list diagnostic surfaced through
/// <see cref="ReactorDiagnostics"/>. Tracked separately so the dev tools
/// overlay can filter and color by severity-of-cause without parsing the
/// message string.
/// </summary>
public enum KeyedListDiagnosticKind
{
    /// <summary>A <c>KeySelector</c> / <c>IReactorKeyed.Key</c> returned <c>null</c>.</summary>
    NullKey,

    /// <summary>Two or more items in the new list returned the same key.</summary>
    DuplicateKey,
}

/// <summary>
/// One captured keyed-list key diagnostic: a null key, or duplicate keys in a keyed list or
/// among a panel's <c>.WithKey</c> siblings. A list with duplicate keys is rebuilt in full when
/// it changes (a bailout); a null-keyed item is matched by position and the list keeps diffing
/// incrementally. Reported at mount as well as on update. Immutable record so the dev tools
/// overlay can display an arbitrary subset without locking the producer.
/// </summary>
/// <param name="TimestampUtc">When the diagnostic was first observed.</param>
/// <param name="ControlContext">Human-readable label — the host control's
/// type name. May be <c>null</c> when the diff ran outside a control
/// context (e.g. direct unit-test invocation of <c>KeyedListDiff.Apply</c>).</param>
/// <param name="Kind">Which class of key problem was found (null key vs. duplicate
/// keys).</param>
/// <param name="SampleKeys">A small, bounded set of representative offending
/// keys (duplicates that collided, or the literal token <c>"&lt;null&gt;"</c>
/// for the null-key case). Capped to keep large lists from blowing the
/// record's footprint.</param>
/// <param name="Count">Number of times this same (control, kind, sample-set)
/// has fired since process start, including the first. Lets the dev overlay
/// show "fired 1× / 12× / 134×" without spamming a fresh entry per render.</param>
public sealed record KeyedListDiagnostic(
    DateTime TimestampUtc,
    string? ControlContext,
    KeyedListDiagnosticKind Kind,
    IReadOnlyList<string> SampleKeys,
    int Count)
{
    /// <summary>
    /// The component whose render produced the list or panel, when known. Reactor knows it
    /// while an inspector is attached (<c>REACTOR_DIAGNOSTICS=1</c>); otherwise <c>null</c>.
    /// </summary>
    public string? ComponentName { get; init; }

    /// <summary>
    /// The C# call site of the list factory, or of the first duplicated child for sibling
    /// keys, when the source map recorded one (Debug builds by default); otherwise <c>null</c>.
    /// </summary>
    public SourceLocation? CallSite { get; init; }
}

/// <summary>
/// Process-wide diagnostic collector for structural problems the reconciler
/// can detect at runtime (today: keyed-list bailouts). Read-only surface for
/// agents, tests, and the in-app dev overlay; the producer side is
/// <c>internal</c>.
/// </summary>
/// <remarks>
/// <para>
/// The collector deduplicates by (control instance, kind, hashed sample
/// keys). The first occurrence is logged in full; subsequent occurrences
/// of the same triple bump a counter rather than appending a fresh entry.
/// This matches the spec 042 §9 Q1 resolution ("warn-and-bailout via a
/// one-shot diagnostic") and avoids drowning the dev overlay during a
/// run-away render loop.
/// </para>
/// <para>
/// The buffer is bounded to <see cref="MaxRecentEntries"/>; once full,
/// the oldest entry is dropped. This is intentionally generous — keyed-list
/// bailouts are a per-app rarity, not a per-frame event.
/// </para>
/// <para>
/// Reads are lock-free against a snapshot; writes take a short lock.
/// </para>
/// </remarks>
public static partial class ReactorDiagnostics
{
    /// <summary>Maximum number of distinct (control, kind, sample-set) entries
    /// retained. New entries beyond this drop the oldest.</summary>
    public const int MaxRecentEntries = 64;

    /// <summary>Maximum number of representative keys captured per entry.
    /// Larger duplicate sets are truncated and an ellipsis is appended.</summary>
    public const int MaxSampleKeys = 8;

    private static readonly Lock _gate = new();
    private static readonly LinkedList<Slot> _recent = new();

    // A buffered entry plus what it was recorded for: the ledger it belongs to (per control,
    // or the contextual one) and its dedup key. A repeat updates only its own entry, never
    // another control's entry that happens to share the label, kind and samples. Holding the
    // ledger does not keep the control alive: the weak table's value never references its key.
    private sealed class Slot(KeyedListDiagnostic entry, ConcurrentDictionary<long, int> ledger, long dedupKey)
    {
        public KeyedListDiagnostic Entry = entry;
        public readonly ConcurrentDictionary<long, int> Ledger = ledger;
        public readonly long DedupKey = dedupKey;
    }

    // (control instance) → set of (kind | sample-set hash) keys already
    // logged. ConditionalWeakTable so a control that's torn down doesn't
    // leak its dedup state; a fresh control instance reusing the same type
    // gets a fresh ledger.
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<long, int>> _seenPerControl = new();

    // For diff sites that don't have a control instance (unit tests,
    // standalone invocations), dedupe by a global key composed of context
    // + kind + sample-set hash.
    private static readonly ConcurrentDictionary<long, int> _seenContextual = new();

    /// <summary>
    /// Recent keyed-list key diagnostics (see <see cref="KeyedListDiagnostic"/>), newest first.
    /// Returns an independent snapshot — safe to enumerate while producers continue
    /// running. Empty when no key problem has ever been found (the common case).
    /// </summary>
    public static IReadOnlyList<KeyedListDiagnostic> RecentKeyedListWarnings
    {
        get
        {
            lock (_gate)
            {
                if (_recent.Count == 0) return global::System.Array.Empty<KeyedListDiagnostic>();
                var arr = new KeyedListDiagnostic[_recent.Count];
                int i = 0;
                // LinkedList is ordered oldest → newest; reverse for callers.
                for (var node = _recent.Last; node is not null; node = node.Previous)
                    arr[i++] = node.Value.Entry;
                return arr;
            }
        }
    }

    /// <summary>
    /// Drop every captured entry and forget per-control dedup state.
    /// Tests use this between scenarios; production code never needs to call it.
    /// </summary>
    public static void ClearRecentKeyedListWarnings()
    {
        lock (_gate)
        {
            _recent.Clear();
            _seenContextual.Clear();
            // ConditionalWeakTable has no Clear in netstandard2.0 / .NET 10
            // semantics worth depending on; entries age out naturally with
            // their control instance. For test isolation, swap the field —
            // but doing so during a live run would race the producer. Tests
            // that absolutely need a clean slate construct fresh controls.
        }
    }

    /// <summary>
    /// Record a keyed-list bailout. Dedup'd: the first occurrence of a
    /// (controlInstance, kind, sample-set) triple lands a fresh entry;
    /// subsequent occurrences bump the existing entry's <see cref="KeyedListDiagnostic.Count"/>
    /// in place and refresh the timestamp.
    /// </summary>
    /// <param name="controlInstance">The host control whose diff bailed.
    /// May be <c>null</c> for unit-test / standalone invocations.</param>
    /// <param name="controlContext">Human-readable label for display
    /// (typically <c>controlInstance.GetType().Name</c>). Falls back to
    /// <c>"&lt;unknown&gt;"</c> when null.</param>
    /// <param name="kind">Which class of bailout fired.</param>
    /// <param name="sampleKeys">Representative offending keys. Will be
    /// truncated to <see cref="MaxSampleKeys"/>.</param>
    /// <param name="componentName">Owning component name, when known.</param>
    /// <param name="callSite">Source call site to attribute the entry to, when known.</param>
    /// <returns>The recorded entry — useful for tests asserting capture
    /// shape. The same instance is returned on dedup hits with an
    /// incremented <see cref="KeyedListDiagnostic.Count"/>.</returns>
    internal static KeyedListDiagnostic Record(
        object? controlInstance,
        string? controlContext,
        KeyedListDiagnosticKind kind,
        IReadOnlyList<string> sampleKeys,
        string? componentName = null,
        SourceLocation? callSite = null)
    {
        var truncated = TruncateSamples(sampleKeys);
        var label = controlContext ?? "<unknown>";
        long dedupKey = ComputeDedupKey(kind, sampleKeys, label);

        lock (_gate)
        {
            // Dedup ledger lookup — per-control for live instances, global
            // for context-only callers.
            ConcurrentDictionary<long, int> ledger = controlInstance is not null
                ? _seenPerControl.GetValue(controlInstance, static _ => new ConcurrentDictionary<long, int>())
                : _seenContextual;

            if (ledger.TryGetValue(dedupKey, out var existingCount))
            {
                ledger[dedupKey] = existingCount + 1;

                // Update the in-place entry to reflect the new count + ts: the entry recorded
                // for this same ledger and dedup key, newest first.
                for (var node = _recent.Last; node is not null; node = node.Previous)
                {
                    var slot = node.Value;
                    if (ReferenceEquals(slot.Ledger, ledger) && slot.DedupKey == dedupKey)
                    {
                        var v = slot.Entry;
                        var updated = v with
                        {
                            TimestampUtc = DateTime.UtcNow,
                            Count = existingCount + 1,
                            // Only the first MaxSampleKeys keys feed the dedup key, so the
                            // overflow summary ("…and N more") can differ between repeats.
                            SampleKeys = truncated,
                            // The latest attribution wins: a reused control can be bound by
                            // another call site or under another root since the first report.
                            ComponentName = componentName ?? v.ComponentName,
                            CallSite = callSite ?? v.CallSite,
                        };
                        slot.Entry = updated;
                        return updated;
                    }
                }
                // Fell off the back of the buffer — re-add a fresh entry
                // with the carried-over count so the surface still tells
                // the user "this has happened N times".
                var revived = new KeyedListDiagnostic(DateTime.UtcNow, label, kind, truncated, existingCount + 1)
                {
                    ComponentName = componentName,
                    CallSite = callSite,
                };
                AppendBounded(new Slot(revived, ledger, dedupKey));
                return revived;
            }

            ledger[dedupKey] = 1;
            var fresh = new KeyedListDiagnostic(DateTime.UtcNow, label, kind, truncated, 1)
            {
                ComponentName = componentName,
                CallSite = callSite,
            };
            AppendBounded(new Slot(fresh, ledger, dedupKey));
            return fresh;
        }
    }

    /// <summary>
    /// True if the (controlInstance, controlContext, kind, sample-set)
    /// triple has not yet been recorded. Used by producers to gate
    /// expensive logging side effects on the first occurrence only.
    /// <paramref name="controlContext"/> only contributes to the dedup key
    /// in the global ledger (<paramref name="controlInstance"/> is
    /// <see langword="null"/>); for live control instances, the per-control
    /// ledger already isolates by instance.
    /// </summary>
    internal static bool IsFirstOccurrence(
        object? controlInstance,
        KeyedListDiagnosticKind kind,
        IReadOnlyList<string> sampleKeys,
        string? controlContext = null)
    {
        var label = controlContext ?? "<unknown>";
        long dedupKey = ComputeDedupKey(kind, sampleKeys, label);

        ConcurrentDictionary<long, int> ledger = controlInstance is not null
            ? _seenPerControl.GetValue(controlInstance, static _ => new ConcurrentDictionary<long, int>())
            : _seenContextual;

        return !ledger.ContainsKey(dedupKey);
    }

    // Dedup key = kind ⊕ samples-hash ⊕ context-label hash. Folding the
    // label in is what makes the global `_seenContextual` ledger distinguish
    // two different controlContext values that happen to share the same
    // (kind, sample-set). For per-control callers the label is redundant
    // (the ledger choice already isolates by instance) but harmless.
    private static long ComputeDedupKey(KeyedListDiagnosticKind kind, IReadOnlyList<string> sampleKeys, string label)
    {
        long samplesHash = HashSampleKeys(sampleKeys);
        long labelHash = global::System.StringComparer.Ordinal.GetHashCode(label);
        return unchecked(((long)(int)kind * 397) ^ samplesHash ^ (labelHash * 31));
    }

    private static void AppendBounded(Slot entry)
    {
        _recent.AddLast(entry);
        while (_recent.Count > MaxRecentEntries) _recent.RemoveFirst();
    }

    private static IReadOnlyList<string> TruncateSamples(IReadOnlyList<string> sampleKeys)
    {
        if (sampleKeys.Count <= MaxSampleKeys) return sampleKeys;
        var arr = new string[MaxSampleKeys + 1];
        for (int i = 0; i < MaxSampleKeys; i++) arr[i] = sampleKeys[i] ?? "<null>";
        arr[MaxSampleKeys] = $"…and {sampleKeys.Count - MaxSampleKeys} more";
        return arr;
    }

    private static long HashSampleKeys(IReadOnlyList<string> keys)
    {
        // FNV-1a over the ordinal hash of each key. Order-sensitive on purpose;
        // duplicate detection sees the same key list in the same order across
        // calls, so order matters for de-dup stability.
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < keys.Count && i < MaxSampleKeys; i++)
            {
                int kh = keys[i] is { } k
                    ? global::System.StringComparer.Ordinal.GetHashCode(k)
                    : 0;
                hash ^= (uint)kh;
                hash *= 1099511628211UL;
            }
            return (long)hash;
        }
    }
}
