namespace Microsoft.UI.Reactor.Controls.Validation;

/// <summary>
/// Collects, queries, and manages validation messages for a form or section of a component tree.
/// Thread-safe: all mutation and query methods are synchronized.
/// </summary>
public sealed class ValidationContext
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<ValidationMessage>> _messages = new();
    private readonly Dictionary<string, List<ValidationMessage>> _externalMessages = new();
    // field -> producer -> the exact instances that producer last contributed, so each
    // producer can retract its own messages without disturbing the others on that field.
    private readonly Dictionary<string, Dictionary<string, List<ValidationMessage>>> _owned = new();
    // field -> newest async pass token; older passes that resolve late are discarded.
    private readonly Dictionary<string, Dictionary<string, int>> _asyncGeneration = new();
    // Context-wide token source. Never reset, so a token retired by a clear can never be
    // handed out again while the pass holding it is still in flight.
    private int _asyncTicket;
    private readonly HashSet<string> _registeredFields = new();
    private readonly HashSet<string> _touchedFields = new();
    private readonly Dictionary<string, object?> _initialValues = new();
    private readonly Dictionary<string, object?> _currentValues = new();

    private int _version;

    /// <summary>
    /// Raised after any mutation that actually changed observable state — a message
    /// appearing or disappearing, a field becoming touched, a reset.
    /// <para>
    /// <c>UseValidationContext()</c> subscribes to this so that mutating the context
    /// from an event handler (the canonical case being <see cref="MarkAllTouched"/> on a
    /// submit attempt) repaints the form. Without it, an invalid submit changes nothing
    /// the user can see.
    /// </para>
    /// <para>
    /// Two rules keep this from driving a render loop. First, re-running the same
    /// validators over an unchanged value is silent: results are applied with a
    /// structural diff, so an idempotent re-validation raises nothing. Second,
    /// mutations made while a render is in flight are not announced — the rendering
    /// component reads the new state later in the same pass, and notifying would
    /// re-enter the reconciler's re-render path from inside <c>Render()</c>.
    /// </para>
    /// </summary>
    public event Action? Changed
    {
        add
        {
            if (value is null) return;
            bool deliverNow;
            lock (_lock)
            {
                _changed += value;
                deliverNow = _notificationPending;
                _notificationPending = false;
            }

            // A deferred notification that found no subscriber is held rather than
            // dropped: during an initial root render the reconciler flushes the
            // deferral before root effects run, so a parent that is about to
            // subscribe would otherwise never hear that a child invalidated the
            // shared context, and its rendered IsValid()/summary would stay stale.
            if (deliverNow) value();
        }
        remove
        {
            if (value is null) return;
            lock (_lock) _changed -= value;
        }
    }

    private Action? _changed;
    private bool _notificationPending;

    /// <summary>
    /// Monotonically increasing version number, bumped on every mutation.
    /// Useful for change detection in hooks/memos.
    /// </summary>
    public int Version
    {
        get { lock (_lock) return _version; }
    }

    /// <summary>
    /// Announces a real change. Must be called *outside* <see cref="_lock"/> — a
    /// subscriber re-entering the context (for example a re-render that immediately
    /// re-reads messages) would otherwise take the lock recursively from the handler.
    /// <para>
    /// A change made while a render is in flight is deferred rather than dropped. The
    /// component doing the rendering needs no notification — it observes the new state
    /// later in the same pass — but other subscribers do: a parent that renders
    /// <c>ctx.IsValid()</c> and provides the context would otherwise never learn that a
    /// child's eager <c>.Validate()</c> invalidated it, leaving its summary or submit
    /// state stale.
    /// </para>
    /// </summary>
    private void RaiseChanged(bool messagesOnly = false)
    {
        if (ValidationRenderScope.InRender)
        {
            if (!messagesOnly)
            {
                lock (_lock) _frameTouchedNonMessageState = true;
            }
            ValidationRenderScope.DeferNotification(this);
            return;
        }

        // Outside a render the snapshot can no longer be trusted as "what subscribers
        // were last told", so stop suppressing against it.
        lock (_lock) _lastNotifiedMessages = null;
        _changed?.Invoke();
    }

    /// <summary>
    /// Delivers a notification that was deferred because it happened mid-render.
    /// Posted through the UI dispatcher when one is available so it lands after the
    /// in-flight reconcile rather than re-entering it; falls back to an inline raise in
    /// headless hosts, which keeps unit tests deterministic.
    /// <para>
    /// The subscriber list is read when the callback runs, not when it is queued. A
    /// host flushes root effects after reconciliation, so a parent's
    /// <c>UseValidationContext()</c> subscription may not exist yet at queue time;
    /// snapshotting there dropped the notification the parent was waiting for. If
    /// there is still no subscriber at delivery time the notification is held for the
    /// first one to arrive.
    /// </para>
    /// </summary>
    internal void NotifyDeferred()
    {
        var dispatcher = global::Microsoft.UI.Reactor.ReactorApp.UIDispatcher;
        if (dispatcher is not null && dispatcher.TryEnqueue(DeliverDeferred))
            return;

        DeliverDeferred();
    }

    private void DeliverDeferred()
    {
        Action? handler;
        lock (_lock)
        {
            // A render can churn a field's messages and land exactly where it started:
            // chaining two value overloads applies the first call's partial verdict and
            // then the second call's full one, both under the sync producer. Each write
            // is a real change, so the frame defers a notification, which repaints, which
            // churns again — an endless loop from a net-zero pass. Announce only when the
            // messages actually ended up different from what subscribers were last told.
            var snapshot = MessageSnapshotLocked();
            if (!_frameTouchedNonMessageState
                && _lastNotifiedMessages is not null
                && string.Equals(_lastNotifiedMessages, snapshot, StringComparison.Ordinal))
            {
                _frameTouchedNonMessageState = false;
                _frameVersionPending = false;
                return;
            }

            if (_frameVersionPending)
            {
                _version++;
                _frameVersionPending = false;
            }

            _lastNotifiedMessages = snapshot;
            _frameTouchedNonMessageState = false;

            handler = _changed;
            if (handler is null)
            {
                _notificationPending = true;
                return;
            }
            _notificationPending = false;
        }
        handler.Invoke();
    }

    /// <summary>
    /// A deterministic rendering of every message the context currently holds, used to
    /// tell a net-zero render pass from a real one. Field names are sorted so dictionary
    /// iteration order cannot matter, but each field's list keeps its own order:
    /// <see cref="GetMessages"/> exposes that order and callers read the first message,
    /// so a reordering is a real change subscribers have to hear about.
    /// </summary>
    private string MessageSnapshotLocked()
    {
        var fields = new List<string>(_messages.Keys);
        fields.AddRange(_externalMessages.Keys.Where(field => !_messages.ContainsKey(field)));
        fields.Sort(StringComparer.Ordinal);

        var sb = new global::System.Text.StringBuilder();
        foreach (var field in fields)
        {
            sb.Append(field).Append('\u0002');
            if (_messages.TryGetValue(field, out var owned))
            {
                foreach (var m in owned)
                    sb.Append('i').Append('\u0001').Append(m.Severity).Append('\u0001')
                      .Append(m.Code).Append('\u0001').Append(m.Text).Append('\u0003');
            }
            if (_externalMessages.TryGetValue(field, out var external))
            {
                foreach (var m in external)
                    sb.Append('e').Append('\u0001').Append(m.Severity).Append('\u0001')
                      .Append(m.Code).Append('\u0001').Append(m.Text).Append('\u0003');
            }
            sb.Append('\u0004');
        }
        return sb.ToString();
    }

    private string? _lastNotifiedMessages;
    private bool _frameTouchedNonMessageState;
    private bool _frameVersionPending;

    /// <summary>
    /// Bumps <see cref="Version"/>, except for a message-only change made while a render
    /// is in flight: those are held until the frame closes and bumped once, and only if
    /// the frame ended with different messages than it started with.
    /// <para>
    /// Bumping eagerly made a net-zero pass — the chained value overloads above —
    /// increment <c>Version</c> on every render forever, so a <c>UseMemo</c> or
    /// <c>UseEffect</c> keyed on it re-ran for a context that had not actually moved.
    /// </para>
    /// </summary>
    private void BumpVersionLocked(bool messagesOnly)
    {
        if (messagesOnly && ValidationRenderScope.InRender)
        {
            _frameVersionPending = true;
            return;
        }
        _version++;
    }

    // ════════════════════════════════════════════════════════════════
    //  Field registration
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Registers a field with the context. Called automatically by .Validate()
    /// or manually for fields that need dirty/touched tracking.
    /// </summary>
    public void RegisterField(string field)
    {
        lock (_lock)
        {
            _registeredFields.Add(field);
        }
    }

    /// <summary>
    /// Returns all registered field names.
    /// </summary>
    public IReadOnlySet<string> RegisteredFields
    {
        get { lock (_lock) return new HashSet<string>(_registeredFields); }
    }

    // ════════════════════════════════════════════════════════════════
    //  Message collection
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a validation message for the specified field.
    /// </summary>
    public void Add(string field, string text, Severity severity = Severity.Error)
    {
        Add(new ValidationMessage(field, text, severity));
    }

    /// <summary>
    /// Adds a validation message.
    /// </summary>
    public void Add(ValidationMessage message)
    {
        lock (_lock)
        {
            if (!_messages.TryGetValue(message.Field, out var list))
            {
                list = new List<ValidationMessage>();
                _messages[message.Field] = list;
            }
            list.Add(message);
            BumpVersionLocked(messagesOnly: true);
        }
        RaiseChanged(messagesOnly: true);
    }

    /// <summary>
    /// Adds an externally-sourced validation message (e.g., server-side error).
    /// External messages persist until explicitly cleared or the field value changes.
    /// </summary>
    public void AddExternal(string field, string text, Severity severity = Severity.Error)
    {
        AddExternal(new ValidationMessage(field, text, severity));
    }

    /// <summary>
    /// Adds an externally-sourced validation message.
    /// </summary>
    public void AddExternal(ValidationMessage message)
    {
        lock (_lock)
        {
            if (!_externalMessages.TryGetValue(message.Field, out var list))
            {
                list = new List<ValidationMessage>();
                _externalMessages[message.Field] = list;
            }
            list.Add(message);
            BumpVersionLocked(messagesOnly: true);
        }
        RaiseChanged(messagesOnly: true);
    }

    // ════════════════════════════════════════════════════════════════
    //  Message clearing
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clears all validation messages (both internal and external) for the specified field.
    /// </summary>
    public void Clear(string field)
    {
        bool changed;
        lock (_lock)
        {
            changed = false;
            if (_messages.Remove(field)) changed = true;
            if (_externalMessages.Remove(field)) changed = true;
            _owned.Remove(field);
            // An async pass still in flight would otherwise repopulate what this just
            // cleared: dropping the token makes its result stale on arrival.
            _asyncGeneration.Remove(field);
            if (changed) BumpVersionLocked(messagesOnly: true);
        }
        if (changed) RaiseChanged(messagesOnly: true);
    }

    /// <summary>
    /// Clears only internal (validator-produced) messages for the specified field.
    /// External messages are preserved.
    /// </summary>
    internal void ClearInternal(string field)
    {
        bool changed;
        lock (_lock)
        {
            changed = _messages.Remove(field);
            _owned.Remove(field);
            // As in Clear: a pending async pass must not repopulate what this dropped.
            _asyncGeneration.Remove(field);
            if (changed) BumpVersionLocked(messagesOnly: true);
        }
        if (changed) RaiseChanged(messagesOnly: true);
    }


    /// <summary>
    /// Installs one producer's contribution to a field's internal messages, leaving
    /// every other producer's messages on that field untouched.
    /// <para>
    /// A field is written by several independent producers: the synchronous
    /// <c>.Validate()</c> chain, each cross-field <c>ValidationRule</c>, and the async
    /// validator pass. Replacing the whole field — which the original clear-then-add
    /// did — means the last writer wins, so a passing rule could erase a required-field
    /// error and make
    /// <see cref="IsValid"/> true. Each producer now retracts only the exact instances
    /// it contributed last time.
    /// </para>
    /// <para>
    /// Replacements happen in place rather than by removing and appending, which keeps
    /// message order stable across passes. Appending would let two producers on the same
    /// field swap positions every render — a structural difference on every pass, and so
    /// a notification on every pass, which is precisely the loop this design exists to
    /// avoid.
    /// </para>
    /// </summary>
    internal void ApplyOwned(string field, string producer, List<ValidationMessage> messages)
    {
        bool changed;
        lock (_lock)
        {
            changed = ApplyOwnedLocked(field, producer, messages);
            if (changed) BumpVersionLocked(messagesOnly: true);
        }
        if (changed) RaiseChanged(messagesOnly: true);
    }

    private bool ApplyOwnedLocked(string field, string producer, List<ValidationMessage> messages)
    {
        _messages.TryGetValue(field, out var current);
        _owned.TryGetValue(field, out var byProducer);

        List<ValidationMessage>? previous = null;
        if (byProducer is not null)
            byProducer.TryGetValue(producer, out previous);

        var next = new List<ValidationMessage>(
            (current?.Count ?? 0) + messages.Count);
        var taken = 0;

        if (current is not null)
        {
            foreach (var message in current)
            {
                if (previous is not null && ContainsReference(previous, message))
                {
                    // Substitute this producer's next message at the same position.
                    if (taken < messages.Count) next.Add(messages[taken++]);
                    continue;
                }
                next.Add(message);
            }
        }

        for (; taken < messages.Count; taken++)
            next.Add(messages[taken]);

        var changed = !SameMessages(current, next);
        if (changed)
        {
            if (next.Count == 0)
                _messages.Remove(field);
            else
                _messages[field] = next;
        }

        // Ownership must name instances that are actually installed. When the diff came
        // back unchanged the stored list is still in _messages, so keeping it is not a
        // micro-optimisation: overwriting it with the freshly allocated (equal but
        // distinct) instances would leave nothing to retract next time, and the pass
        // after that would append a duplicate.
        if (changed)
        {
            if (messages.Count == 0)
            {
                if (byProducer is not null)
                {
                    byProducer.Remove(producer);
                    if (byProducer.Count == 0) _owned.Remove(field);
                }
            }
            else
            {
                byProducer ??= _owned[field] = new Dictionary<string, List<ValidationMessage>>(StringComparer.Ordinal);
                byProducer[producer] = messages;
            }
        }
        else if (messages.Count == 0 && byProducer is not null)
        {
            byProducer.Remove(producer);
            if (byProducer.Count == 0) _owned.Remove(field);
        }

        return changed;
    }

    private static bool SameMessages(List<ValidationMessage>? a, List<ValidationMessage> b)
    {
        var countA = a?.Count ?? 0;
        if (countA != b.Count) return false;
        for (var i = 0; i < b.Count; i++)
        {
            if (a![i] != b[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// Installs the complete result of an async validation pass for a field in one step.
    /// <para>
    /// <paramref name="generation"/> is the token handed out by
    /// <see cref="BeginAsyncValidation"/> when the pass started. Passes for successive
    /// values race — an older value's checks can resolve after a newer value's — so a
    /// result that is no longer the newest is discarded rather than overwriting the
    /// current verdict with a stale one.
    /// </para>
    /// </summary>
    internal void ApplyAsyncValidation(string field, int generation, List<ValidationMessage> messages)
        => ApplyAsyncOwned(field, AsyncProducer, generation, messages);

    /// <summary>
    /// Installs an async producer's result for a field, but only if it is still the
    /// newest pass that producer opened. Generations are tracked per producer because
    /// several can write the same field — an async <c>ValidationRule</c> alongside the
    /// field's own <c>.ValidateAsync(...)</c> — and a shared token would let whichever
    /// finished last cancel the other.
    /// </summary>
    internal void ApplyAsyncOwned(string field, string producer, int generation, List<ValidationMessage> messages)
    {
        bool changed;
        lock (_lock)
        {
            if (!_asyncGeneration.TryGetValue(field, out var byProducer)
                || !byProducer.TryGetValue(producer, out var newest)
                || newest != generation)
                return;

            changed = ApplyOwnedLocked(field, producer, messages);
            if (changed) BumpVersionLocked(messagesOnly: true);
        }
        if (changed) RaiseChanged(messagesOnly: true);
    }

    /// <summary>
    /// Opens an async validation pass for a field and returns the token that identifies
    /// it. Only the most recently opened pass is allowed to install a result.
    /// <para>
    /// Tokens come from a context-wide counter rather than a per-field one. The clearing
    /// operations drop a field's entry so a pending pass can't repopulate what they just
    /// removed — with a per-field counter that also reset the sequence, so a pass opened
    /// after a clear could be handed the same number an older in-flight pass was still
    /// holding, and the stale result would pass the equality check.
    /// </para>
    /// </summary>
    internal int BeginAsyncValidation(string field) => BeginAsyncProducer(field, AsyncProducer);

    /// <summary>
    /// Opens an async pass for one producer on a field. Overlapping evaluations of the
    /// same producer are ordered by this token: an older one that resolves last is
    /// discarded instead of reinstating a verdict about a value or predicate input that
    /// has already been superseded.
    /// </summary>
    internal int BeginAsyncProducer(string field, string producer)
    {
        lock (_lock)
        {
            var token = unchecked(++_asyncTicket);
            if (!_asyncGeneration.TryGetValue(field, out var byProducer))
                _asyncGeneration[field] = byProducer = new Dictionary<string, int>(StringComparer.Ordinal);
            byProducer[producer] = token;
            return token;
        }
    }

    internal const string SyncProducer = "sync";
    internal const string AsyncProducer = "async";

    private static bool ContainsReference(List<ValidationMessage> list, ValidationMessage message)
    {
        foreach (var candidate in list)
        {
            if (ReferenceEquals(candidate, message)) return true;
        }
        return false;
    }

    /// <summary>
    /// Registers a field, records its value, and installs its validator results as one
    /// atomic step — a single lock, a single version bump, and at most one
    /// <see cref="Changed"/> notification raised only after everything is in place.
    /// <para>
    /// Doing this as three calls let a subscriber observe the context mid-update: on a
    /// first mount, <see cref="NotifyValueChanged"/> would see an unknown field, raise,
    /// and synchronously drive a re-render that read the *previous* pass's messages
    /// because the replacement had not happened yet. The reconcile-time <c>FormField</c>
    /// path reaches this code after the render scope has closed, so that notification
    /// was not suppressed.
    /// </para>
    /// </summary>
    internal void ApplyValidation(string field, object? value, List<ValidationMessage> messages)
    {
        bool changed;
        bool valueChangedForNotify;
        lock (_lock)
        {
            var newField = _registeredFields.Add(field);

            var known = _currentValues.TryGetValue(field, out var previous);
            var valueChanged = !known || !Equals(previous, value);
            var messagesChanged = false;

            if (valueChanged)
            {
                _currentValues[field] = value;
                // A server verdict about the old value says nothing about the new one.
                _externalMessages.Remove(field);

                // Neither does an async verdict. Retire the in-flight pass so its result
                // is discarded on arrival, and withdraw whatever the last one installed —
                // otherwise an error computed for a value the user has already replaced
                // stays on screen indefinitely.
                _asyncGeneration.Remove(field);
                if (ApplyOwnedLocked(field, AsyncProducer, [])) messagesChanged = true;
            }

            // Owned rather than wholesale: a cross-field ValidationRule may also be
            // writing this field, and it must survive the sync pass.
            if (ApplyOwnedLocked(field, SyncProducer, messages)) messagesChanged = true;

            changed = valueChanged || messagesChanged;
            valueChangedForNotify = valueChanged || newField;
            if (changed) BumpVersionLocked(messagesOnly: !valueChangedForNotify);
        }
        if (changed) RaiseChanged(messagesOnly: !valueChangedForNotify);
    }

    /// <summary>
    /// Clears only external messages for the specified field.
    /// </summary>
    public void ClearExternal(string field)
    {
        bool changed;
        lock (_lock)
        {
            changed = _externalMessages.Remove(field);
            if (changed) BumpVersionLocked(messagesOnly: true);
        }
        if (changed) RaiseChanged(messagesOnly: true);
    }

    /// <summary>
    /// Clears all messages (internal and external) for all fields.
    /// </summary>
    public void ClearAll()
    {
        bool changed;
        lock (_lock)
        {
            changed = _messages.Count > 0 || _externalMessages.Count > 0;
            _messages.Clear();
            _externalMessages.Clear();
            _owned.Clear();
            _asyncGeneration.Clear();
            if (changed) BumpVersionLocked(messagesOnly: true);
        }
        if (changed) RaiseChanged(messagesOnly: true);
    }

    // ════════════════════════════════════════════════════════════════
    //  Query methods
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns all messages (internal + external) for the specified field.
    /// </summary>
    public IReadOnlyList<ValidationMessage> GetMessages(string field)
    {
        lock (_lock)
        {
            var result = new List<ValidationMessage>();
            if (_messages.TryGetValue(field, out var internal_))
                result.AddRange(internal_);
            if (_externalMessages.TryGetValue(field, out var external))
                result.AddRange(external);
            return result;
        }
    }

    /// <summary>
    /// Returns all messages (internal + external) across all fields.
    /// </summary>
    public IReadOnlyList<ValidationMessage> GetAllMessages()
    {
        lock (_lock)
        {
            var result = new List<ValidationMessage>();
            foreach (var list in _messages.Values)
                result.AddRange(list);
            foreach (var list in _externalMessages.Values)
                result.AddRange(list);
            return result;
        }
    }

    /// <summary>
    /// Returns true if the specified field has any Error-severity messages.
    /// </summary>
    public bool HasError(string field)
    {
        lock (_lock)
        {
            return HasSeverity(field, Severity.Error);
        }
    }

    /// <summary>
    /// Returns true if the specified field has any messages of any severity.
    /// </summary>
    public bool HasMessages(string field)
    {
        lock (_lock)
        {
            if (_messages.TryGetValue(field, out var internal_) && internal_.Count > 0)
                return true;
            if (_externalMessages.TryGetValue(field, out var external) && external.Count > 0)
                return true;
            return false;
        }
    }

    /// <summary>
    /// Returns the highest severity among all messages for the specified field, or null if none.
    /// </summary>
    public Severity? HighestSeverity(string field)
    {
        lock (_lock)
        {
            Severity? highest = null;
            CheckSeverity(field, _messages, ref highest);
            CheckSeverity(field, _externalMessages, ref highest);
            return highest;
        }
    }

    /// <summary>
    /// Returns true when there are no Error-severity messages across all fields.
    /// </summary>
    public bool IsValid()
    {
        lock (_lock)
        {
            foreach (var list in _messages.Values)
                foreach (var msg in list)
                    if (msg.Severity == Severity.Error)
                        return false;
            foreach (var list in _externalMessages.Values)
                foreach (var msg in list)
                    if (msg.Severity == Severity.Error)
                        return false;
            return true;
        }
    }

    /// <summary>
    /// Returns the names of all fields that have at least one Error-severity message.
    /// </summary>
    public IReadOnlyList<string> InvalidFields
    {
        get
        {
            lock (_lock)
            {
                var fields = new HashSet<string>();
                foreach (var (fieldName, list) in _messages)
                    foreach (var msg in list)
                        if (msg.Severity == Severity.Error)
                        { fields.Add(fieldName); break; }
                foreach (var (fieldName, list) in _externalMessages)
                    foreach (var msg in list)
                        if (msg.Severity == Severity.Error)
                        { fields.Add(fieldName); break; }
                return fields.ToList();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Touched state
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if the field has been focused and then blurred.
    /// </summary>
    public bool IsTouched(string field)
    {
        lock (_lock) return _touchedFields.Contains(field);
    }

    /// <summary>
    /// Marks a field as touched (e.g., after focus+blur).
    /// </summary>
    public void MarkTouched(string field)
    {
        bool changed;
        lock (_lock)
        {
            changed = _touchedFields.Add(field);
            if (changed) _version++;
        }
        if (changed) RaiseChanged();
    }

    /// <summary>
    /// Marks all registered fields as touched. Typically called on form submit.
    /// </summary>
    public void MarkAllTouched()
    {
        bool changed;
        lock (_lock)
        {
            // Add unconditionally and compare the set size rather than branching per
            // field: HashSet.Add already de-duplicates, so a filtered loop would only
            // add a second hash lookup per field (and, via LINQ, an allocation) inside
            // this lock to reach the same answer.
            var touchedBefore = _touchedFields.Count;
            foreach (var field in _registeredFields)
                _touchedFields.Add(field);

            changed = _touchedFields.Count != touchedBefore;
            if (changed) _version++;
        }
        if (changed) RaiseChanged();
    }

    // ════════════════════════════════════════════════════════════════
    //  Dirty state
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stores the initial value for a field. Called at field registration time.
    /// <para>
    /// The current value is seeded only the first time the field is seen. Re-seeding it
    /// on every call would rewind whatever the user has since typed — harmless while
    /// nothing watched the context, but with change notification it becomes a permanent
    /// repaint loop for the common <c>SetInitialValue(...)</c> +
    /// <c>NotifyValueChanged(...)</c> pair that components run on each render: the
    /// rewind and the re-notify would take turns forever. Use <see cref="Reset(string)"/>
    /// to deliberately return a field to its baseline.
    /// </para>
    /// </summary>
    public void SetInitialValue(string field, object? value)
    {
        bool changed;
        lock (_lock)
        {
            var wasDirty = IsDirtyLocked(field);
            _initialValues[field] = value;
            if (!_currentValues.ContainsKey(field))
                _currentValues[field] = value;

            // Re-baselining an edited field flips IsDirty without touching messages or
            // touched state, so subscribers have to hear about it too.
            changed = IsDirtyLocked(field) != wasDirty;
            if (changed) _version++;
        }
        if (changed) RaiseChanged();
    }

    private bool IsDirtyLocked(string field)
    {
        if (!_initialValues.TryGetValue(field, out var initial)) return false;
        if (!_currentValues.TryGetValue(field, out var current)) return false;
        return !Equals(initial, current);
    }

    /// <summary>
    /// Notifies the context of a field value change. Clears external messages for the
    /// field, because a server-side verdict about the old value says nothing about the
    /// new one.
    /// <para>
    /// The clear is conditional on the value actually having moved. It used to be
    /// unconditional, which was survivable only while validation ran rarely: now that
    /// validators run on every render, an unconditional clear here would destroy any
    /// <see cref="AddExternal(string, string, Severity)"/> message on the very next
    /// repaint, before the user could read it.
    /// </para>
    /// <para>
    /// An async verdict is retired for the same reason, mirroring
    /// <see cref="ApplyValidation"/>. A field validated only through
    /// <c>.ValidateAsync(...)</c> never reaches that method, so without this an error
    /// computed for a value the user has already replaced would stay on screen, and a
    /// pass opened against the old value could still install its result afterwards.
    /// </para>
    /// </summary>
    public void NotifyValueChanged(string field, object? value)
    {
        bool changed;
        lock (_lock)
        {
            var known = _currentValues.TryGetValue(field, out var previous);
            changed = !known || !Equals(previous, value);
            if (!changed) return;

            _currentValues[field] = value;
            _externalMessages.Remove(field);
            _asyncGeneration.Remove(field);
            ApplyOwnedLocked(field, AsyncProducer, []);
            _version++;
        }
        RaiseChanged();
    }

    /// <summary>
    /// Returns true if the field's current value differs from its initial value.
    /// </summary>
    public bool IsDirty(string field)
    {
        lock (_lock)
        {
            if (!_initialValues.TryGetValue(field, out var initial))
                return false;
            if (!_currentValues.TryGetValue(field, out var current))
                return false;
            return !Equals(initial, current);
        }
    }

    /// <summary>
    /// Returns true if any registered field is dirty.
    /// </summary>
    public bool IsDirty()
    {
        lock (_lock)
        {
            foreach (var field in _registeredFields)
            {
                if (_initialValues.TryGetValue(field, out var initial)
                    && _currentValues.TryGetValue(field, out var current)
                    && !Equals(initial, current))
                    return true;
            }
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Reset
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resets a field to its initial value, clears touched state and all messages.
    /// Returns the initial value so the caller can update the UI.
    /// </summary>
    public object? Reset(string field)
    {
        object? initial;
        bool changed;
        lock (_lock)
        {
            changed = _touchedFields.Remove(field);
            if (_messages.Remove(field)) changed = true;
            if (_externalMessages.Remove(field)) changed = true;
            _owned.Remove(field);
            _asyncGeneration.Remove(field);

            _initialValues.TryGetValue(field, out initial);
            // Only rewind a value the context is actually tracking. Creating an entry
            // for a field it has never seen is not observable (IsDirty needs both an
            // initial and a current value) but would make Reset("unknown") look like a
            // change and notify.
            if (_currentValues.TryGetValue(field, out var current) && !Equals(current, initial))
            {
                _currentValues[field] = initial;
                changed = true;
            }

            if (changed) _version++;
        }
        if (changed) RaiseChanged();
        return initial;
    }

    /// <summary>
    /// Resets all fields to their initial values, clears all touched states and messages.
    /// Returns a dictionary of field → initial value.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ResetAll()
    {
        Dictionary<string, object?> result;
        bool changed;
        lock (_lock)
        {
            changed = _touchedFields.Count > 0 || _messages.Count > 0 || _externalMessages.Count > 0;
            _touchedFields.Clear();
            _messages.Clear();
            _externalMessages.Clear();
            _owned.Clear();
            _asyncGeneration.Clear();

            result = new Dictionary<string, object?>();
            foreach (var (field, initial) in _initialValues)
            {
                if (_currentValues.TryGetValue(field, out var current) && !Equals(current, initial))
                {
                    _currentValues[field] = initial;
                    changed = true;
                }
                result[field] = initial;
            }

            if (changed) _version++;
        }
        if (changed) RaiseChanged();
        return result;
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private bool HasSeverity(string field, Severity severity)
    {
        if (_messages.TryGetValue(field, out var internal_))
            foreach (var msg in internal_)
                if (msg.Severity == severity)
                    return true;
        if (_externalMessages.TryGetValue(field, out var external))
            foreach (var msg in external)
                if (msg.Severity == severity)
                    return true;
        return false;
    }

    private static void CheckSeverity(string field,
        Dictionary<string, List<ValidationMessage>> store,
        ref Severity? highest)
    {
        if (!store.TryGetValue(field, out var list)) return;
        foreach (var msg in list)
        {
            if (highest is null || msg.Severity > highest.Value)
                highest = msg.Severity;
            if (highest == Severity.Error)
                return; // can't go higher
        }
    }
}
