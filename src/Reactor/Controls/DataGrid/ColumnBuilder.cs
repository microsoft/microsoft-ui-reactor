using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Controls.Validation;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// Fluent builder for column definitions. Supports validation chaining.
/// </summary>
public class ColumnBuilder<T>
{
    private FieldDescriptor _descriptor;

    internal ColumnBuilder(FieldDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    /// <summary>Add validators to this column.</summary>
    public ColumnBuilder<T> Validate(params IValidator[] validators)
    {
        var existing = _descriptor.Validators?.ToList() ?? new List<IValidator>();
        existing.AddRange(validators);
        _descriptor = _descriptor with { Validators = existing };
        return this;
    }

    /// <summary>Add async validators to this column.</summary>
    public ColumnBuilder<T> ValidateAsync(params IAsyncValidator[] validators)
    {
        var existing = _descriptor.AsyncValidators?.ToList() ?? new List<IAsyncValidator>();
        existing.AddRange(validators);
        _descriptor = _descriptor with { AsyncValidators = existing };
        return this;
    }

    /// <summary>Set a custom cell renderer for this column.</summary>
    public ColumnBuilder<T> CellRenderer(Func<object, Element> renderer)
    {
        _descriptor = _descriptor with { CellRenderer = renderer };
        return this;
    }

    /// <summary>
    /// Set a custom inline editor for this column. The delegate receives the
    /// current value and an onChange callback and returns the editor Element.
    /// See <see cref="Editors"/> for pre-built factories.
    /// </summary>
    public ColumnBuilder<T> WithEditor(Func<object, Action<object>, Element> editor)
    {
        _descriptor = _descriptor with { Editor = editor };
        return this;
    }

    /// <summary>Width / min / max / flex for the column.</summary>
    public ColumnBuilder<T> Width(double? width = null, double? min = null, double? max = null, double? flex = null)
    {
        _descriptor = _descriptor with
        {
            Width = width ?? _descriptor.Width,
            MinWidth = min ?? _descriptor.MinWidth,
            MaxWidth = max ?? _descriptor.MaxWidth,
            Flex = flex ?? _descriptor.Flex,
        };
        return this;
    }

    /// <summary>Set sortable state.</summary>
    public ColumnBuilder<T> NotSortable()
    {
        _descriptor = _descriptor with { Sortable = false };
        return this;
    }

    /// <summary>Set filterable state.</summary>
    public ColumnBuilder<T> NotFilterable()
    {
        _descriptor = _descriptor with { Filterable = false };
        return this;
    }

    /// <summary>Build the FieldDescriptor.</summary>
    public FieldDescriptor Build() => _descriptor;

    /// <summary>Implicit conversion to FieldDescriptor for clean DSL usage.</summary>
    public static implicit operator FieldDescriptor(ColumnBuilder<T> builder)
        => builder._descriptor;
}
