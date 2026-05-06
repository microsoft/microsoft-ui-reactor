// Shared rendering helpers for gallery samples

using Microsoft.UI.Reactor.Core;

namespace ReactorCharting.Gallery;

/// <summary>
/// Base class for all gallery samples. Each sample provides a title,
/// description, source code snippet, and a Render method that builds the chart
/// as a Reactor Element tree (declarative, reconciler-compatible).
/// </summary>
public abstract class GallerySample
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    public abstract string Category { get; }
    public abstract string SourceCode { get; }
    public abstract Element Render();

    /// <summary>SVG icon filename (without extension), derived from class name.</summary>
    public virtual string IconName => GetType().Name.Replace("Sample", "");
}
