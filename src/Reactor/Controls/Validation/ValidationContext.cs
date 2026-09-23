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
    public event Action? Changed;

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
    /// </summary>
    private void RaiseChanged()
    {
        if (ValidationRenderScope.InRender) return;
        Changed?.Invoke();
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
            _version++;
        }
        RaiseChanged();
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
            _version++;
        }
        RaiseChanged();
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
            if (changed) _version++;
        }
        if (changed) RaiseChanged();
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
            if (changed) _version++;
        }
        if (changed) RaiseChanged();
    }

    /// <summary>
    /// Replaces the internal (validator-produced) messages for a field in one step,
    /// bumping <see cref="Version"/> and raising <see cref="Changed"/> only when the new
    /// set actually differs from the old one.
    /// <para>
    /// This is what makes per-render validation safe. The previous
    /// <c>ClearInternal</c> + N&#215;<c>Add</c> sequence bumped the version two or more
    /// times on *every* pass even when the value and its verdict were unchanged, so once
    /// validators run on every render a change-notification built on that signal would
    /// re-render forever. <see cref="ValidationMessage"/> is a record, so the comparison
    /// below is ordinary structural equality.
    /// </para>
    /// </summary>
    internal void ReplaceInternal(string field, List<ValidationMessage> messages)
    {
        bool changed;
        lock (_lock)
        {
            _messages.TryGetValue(field, out var existing);
            changed = !SameMessages(existing, messages);
            if (changed)
            {
                if (messages.Count == 0)
                    _messages.Remove(field);
                else
                    _messages[field] = messages;
                _version++;
            }
        }
        if (changed) RaiseChanged();
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
    /// Clears only external messages for the specified field.
    /// </summary>
    public void ClearExternal(string field)
    {
        bool changed;
        lock (_lock)
        {
            changed = _externalMessages.Remove(field);
            if (changed) _version++;
        }
        if (changed) RaiseChanged();
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
            if (changed) _version++;
        }
        if (changed) RaiseChanged();
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
    /// </summary>
    public void SetInitialValue(string field, object? value)
    {
        lock (_lock)
        {
            _initialValues[field] = value;
            _currentValues[field] = value;
        }
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
        lock (_lock)
        {
            _touchedFields.Remove(field);
            _messages.Remove(field);
            _externalMessages.Remove(field);
            _initialValues.TryGetValue(field, out initial);
            _currentValues[field] = initial;
            _version++;
        }
        RaiseChanged();
        return initial;
    }

    /// <summary>
    /// Resets all fields to their initial values, clears all touched states and messages.
    /// Returns a dictionary of field → initial value.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ResetAll()
    {
        Dictionary<string, object?> result;
        lock (_lock)
        {
            _touchedFields.Clear();
            _messages.Clear();
            _externalMessages.Clear();
            result = new Dictionary<string, object?>();
            foreach (var (field, initial) in _initialValues)
            {
                _currentValues[field] = initial;
                result[field] = initial;
            }
            _version++;
        }
        RaiseChanged();
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
