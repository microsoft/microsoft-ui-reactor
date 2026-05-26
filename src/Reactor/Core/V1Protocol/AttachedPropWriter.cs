using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §6 / §14 Phase 1 (1.8) — writer for an attached DP from a parent
/// container onto a child control (Grid.Row, Canvas.Left, DockPanel.Dock,
/// etc.). Authors of container handlers register these on the child element
/// type; the engine invokes them during child mount.
///
/// <para><b>Type-safety contract:</b> <see cref="Get"/> pulls the value off
/// the typed child element; <see cref="Write"/> applies it to the
/// <see cref="UIElement"/> control. Both lambdas are strongly typed — no
/// reflection, AOT-compatible.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record AttachedPropWriter<TChildElement>(
    string Name,
    Func<TChildElement, object?> Get,
    Action<UIElement, object?> Write)
    where TChildElement : Element;
