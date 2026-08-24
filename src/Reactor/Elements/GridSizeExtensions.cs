namespace Microsoft.UI.Reactor;

/// <summary>
/// Fluent extension methods for <see cref="GridSize"/> to set the optional <c>Min</c> and <c>Max</c> properties.
/// </summary>
public static class GridSizeExtensions
{
    /// <summary>
    /// Sets the <c>Min</c> property of a <see cref="GridSize"/> instance.
    /// </summary>
    /// <param name="size">The <see cref="GridSize"/> instance to modify.</param>
    /// <param name="min">The minimum size value to set.</param>
    /// <returns>A new <see cref="GridSize"/> instance with the <c>Min</c> property set.</returns>
    public static GridSize MinSize(this GridSize size, double min) => size with { Min = min };

    /// <summary>
    /// Sets the <c>Max</c> property of a <see cref="GridSize"/> instance.
    /// </summary>
    /// <param name="size">The <see cref="GridSize"/> instance to modify.</param>
    /// <param name="max">The maximum size value to set.</param>
    /// <returns>A new <see cref="GridSize"/> instance with the <c>Max</c> property set.</returns>
    public static GridSize MaxSize(this GridSize size, double max) => size with { Max = max };

    /// <summary>
    /// Sets both the <c>Min</c> and <c>Max</c> properties of a <see cref="GridSize"/> instance.
    /// </summary>
    /// <param name="size">The <see cref="GridSize"/> instance to modify.</param>
    /// <param name="min">The minimum size value to set.</param>
    /// <param name="max">The maximum size value to set.</param>
    /// <returns>A new <see cref="GridSize"/> instance with the <c>Min</c> and <c>Max</c> properties set.</returns>
    public static GridSize MinMaxSize(this GridSize size, double min, double max) => size with { Min = min, Max = max };
}
