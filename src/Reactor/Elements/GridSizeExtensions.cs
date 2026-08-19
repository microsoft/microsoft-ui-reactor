using System;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Fluent extension methods for grid track constraints.
/// </summary>
public static class GridSizeExtensions
{
	/// <summary>Sets the minimum track size.</summary>
	public static GridSize MinSize(this GridSize size, double minSize) => size with { Min = minSize };
	
	/// <summary>Sets the maximum track size.</summary>
	public static GridSize MaxSize(this GridSize size, double maxSize) => size with { Max = maxSize };

}
