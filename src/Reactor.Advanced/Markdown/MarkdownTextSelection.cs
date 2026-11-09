using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Markdown;

/// <summary>Policy shared by the Markdown leaves of one assembled message.</summary>
public sealed record MarkdownTextSelectionOptions
{
    /// <summary>
    /// Feedback only, called after Reactor's single clipboard write. The Boolean is true
    /// only on success. Expected clipboard-access failures report false; unexpected
    /// exceptions propagate. This callback must not write the clipboard again.
    /// </summary>
    public Action<string, bool>? OnCopyResult { get; init; }
}

/// <summary>Message-bounded, opt-in semantic text selection for Markdown and custom content.</summary>
public static class MarkdownTextSelection
{
    static MarkdownTextSelection() =>
        ControlRegistry.RegisterDecorator<MarkdownSelectionElement>(static () => new MarkdownSelectionHandler());

    /// <summary>Wrap the complete assembled message once, not each Markdown leaf.</summary>
    public static Element Scope(MarkdownTextSelectionOptions options, Element content)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(content);
        return Wrap(content, SelectionRole.Scope) with { Options = options };
    }

    /// <summary>
    /// Register exact visible plain text, with an explicit separator to the next surface.
    /// Chrome not included in this wrapper is not selectable.
    /// </summary>
    public static Element Surface(string text, Element content, string separatorAfter = "\n")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(separatorAfter);
        return Wrap(content, SelectionRole.Surface) with
        { Text = text, SeparatorAfter = separatorAfter };
    }

    /// <summary>
    /// Include or exclude descendant surfaces without changing the app's visual rendering.
    /// Nested gates compose. The app owns disclosure state and native Expander policy.
    /// </summary>
    public static Element Visibility(bool isVisible, Element content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Wrap(content, SelectionRole.Visibility) with { IsVisible = isVisible };
    }

    private static MarkdownSelectionElement Wrap(Element content, SelectionRole role)
    {
        var layout = content.Modifiers?.Layout;
        LayoutModifiers? outer = layout is null ? null : new()
        {
            Margin = layout.Margin,
            MarginInlineStart = layout.MarginInlineStart,
            MarginInlineEnd = layout.MarginInlineEnd,
            Width = layout.Width,
            Height = layout.Height,
            MinWidth = layout.MinWidth,
            MinHeight = layout.MinHeight,
            MaxWidth = layout.MaxWidth,
            MaxHeight = layout.MaxHeight,
            HorizontalAlignment = layout.HorizontalAlignment,
            VerticalAlignment = layout.VerticalAlignment,
            IsVisible = layout.IsVisible,
        };
        var inner = layout is null ? null : layout with
        {
            Margin = null, MarginInlineStart = null, MarginInlineEnd = null,
            Width = null, Height = null, MinWidth = null, MinHeight = null,
            MaxWidth = null, MaxHeight = null,
            HorizontalAlignment = null, VerticalAlignment = null, IsVisible = null,
        };
        var placements = content.Attached?.Where(p => p.Value is
            GridAttached or CanvasAttached or FlexAttached or WrapGridAttached).ToDictionary(p => p.Key, p => p.Value);
        var remaining = placements is null || placements.Count == 0 ? content.Attached :
            content.Attached!.Where(p => !placements.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        if (outer == new LayoutModifiers()) outer = null;
        if (outer is null && content.Key is null && placements is not { Count: > 0 })
            return new MarkdownSelectionElement(content, role);
        // The wrapper occupies the parent's slot. Inner padding, typography and
        // visuals stay on the content; outer sizing/margins/placement apply once.
        return new MarkdownSelectionElement(content with
        {
            Key = null,
            Modifiers = content.Modifiers is null ? null : content.Modifiers with { Layout = inner },
            Attached = remaining is { Count: 0 } ? null : remaining,
        }, role)
        {
            Key = content.Key,
            Modifiers = outer is null ? null : new ElementModifiers { Layout = outer },
            Attached = placements is { Count: > 0 } ? placements : null,
        };
    }
}

internal enum SelectionRole { Scope, Surface, Visibility }

internal sealed record MarkdownSelectionElement(Element Content, SelectionRole Role) : Element
{
    internal MarkdownTextSelectionOptions? Options { get; init; }
    internal string Text { get; init; } = "";
    internal string SeparatorAfter { get; init; } = "\n";
    internal bool IsVisible { get; init; } = true;
}

internal sealed partial class MarkdownSelectionMarker : UserControl
{
    internal MarkdownSelectionElement Metadata { get; set; } = null!;
    internal UIElement? Child { get => Content; set => Content = value; }
}

internal sealed class MarkdownSelectionHandler : IDecoratorElementHandler<MarkdownSelectionElement>
{
    public Element? GetSourceTarget(MarkdownSelectionElement element) => element.Content;

    public UIElement Mount(MountContext ctx, MarkdownSelectionElement element)
        => Create(element, ctx.MountChild(element.Content));

    private static UIElement Create(MarkdownSelectionElement element, UIElement? child)
    {
        if (element.Role == SelectionRole.Scope)
        {
            var host = new MarkdownSelectionHost { Options = element.Options! };
            host.SetContent(child);
            return host;
        }
        return new MarkdownSelectionMarker { Metadata = element, Child = child };
    }

    public UIElement Update(UpdateContext ctx, MarkdownSelectionElement oldEl,
        MarkdownSelectionElement newEl, UIElement control)
    {
        if ((oldEl.Role == SelectionRole.Scope) != (newEl.Role == SelectionRole.Scope))
        {
            ctx.Reconciler.UnmountChild(control);
            return Create(newEl, ctx.MountChild(newEl.Content));
        }
        if (control is MarkdownSelectionHost host)
        {
            host.Options = newEl.Options!;
            host.SetContent(ctx.ReconcileChild(oldEl.Content, newEl.Content, host.Body));
            // LayoutUpdated refreshes after the native text tree settles, not mid-reconciliation.
        }
        else
        {
            var marker = (MarkdownSelectionMarker)control;
            marker.Metadata = newEl;
            marker.Child = ctx.ReconcileChild(oldEl.Content, newEl.Content, marker.Child);
        }
        return control;
    }

    public V1UnmountDisposition Unmount(UnmountContext ctx, MarkdownSelectionElement? element, UIElement control)
    {
        if (control is MarkdownSelectionHost host)
        {
            host.Detach();
            if (host.Body is { } child) ctx.Reconciler.UnmountChild(child);
            host.SetContent(null);
        }
        else
        {
            var marker = (MarkdownSelectionMarker)control;
            if (marker.Child is { } child) ctx.Reconciler.UnmountChild(child);
            marker.Child = null;
        }
        return V1UnmountDisposition.SkipPool;
    }
}

internal readonly record struct SelectionPart(object Identity, string Text, string Separator);

internal static class MarkdownSelectionCopy
{
    internal static void Write(string text, Action<string> write, Action<string, bool>? result)
    {
        bool success;
        try { write(text); success = true; }
        catch (UnauthorizedAccessException) { success = false; }
        catch (COMException e) when (e.HResult is unchecked((int)0x80070005) or
            unchecked((int)0x800401D0) or unchecked((int)0x800401D1))
        { success = false; }
        result?.Invoke(text, success);
    }
}

/// <summary>Pure UTF-16 document model. Identity belongs to the mounted surface, not source markup.</summary>
internal sealed class MarkdownSelectionDocument
{
    internal sealed record Entry(object Identity, string Text, string Separator, int Start)
    {
        internal int End => Start + Text.Length;
    }

    internal IReadOnlyList<Entry> Entries { get; private set; } = [];
    internal string Text { get; private set; } = "";
    internal int Anchor { get; private set; }
    internal int Caret { get; private set; }
    internal int Start => Math.Min(Anchor, Caret);
    internal int End => Math.Max(Anchor, Caret);
    internal string SelectedText => Text[Start..End];
    private int[] _graphemes = [];

    internal bool Rebuild(IEnumerable<SelectionPart> parts)
    {
        var next = new List<Entry>();
        var text = new StringBuilder();
        foreach (var part in parts)
        {
            if (next.Count > 0) text.Append(next[^1].Separator);
            next.Add(new Entry(part.Identity, part.Text, part.Separator, text.Length));
            text.Append(part.Text);
        }
        var value = text.ToString();
        bool changed = value != Text || next.Count != Entries.Count ||
            next.Where((entry, i) => i >= Entries.Count || !ReferenceEquals(entry.Identity, Entries[i].Identity)
                || entry.Text != Entries[i].Text || entry.Separator != Entries[i].Separator).Any();
        if (!changed) return false;
        int anchor = Remap(Anchor, next, value.Length);
        int caret = Remap(Caret, next, value.Length);
        Entries = next;
        Text = value;
        _graphemes = StringInfo.ParseCombiningCharacters(value);
        Select(anchor, caret);
        return true;
    }

    private int Remap(int offset, List<Entry> next, int length)
    {
        var old = Entries.LastOrDefault(e => e.Start <= offset);
        if (old is null) return Math.Min(offset, length);
        var replacement = next.FirstOrDefault(e => ReferenceEquals(e.Identity, old.Identity));
        if (replacement is null) return Math.Min(offset, length);
        int local = Math.Clamp(offset - old.Start, 0, old.Text.Length);
        int prefix = 0;
        while (prefix < old.Text.Length && prefix < replacement.Text.Length &&
            old.Text[prefix] == replacement.Text[prefix]) prefix++;
        int suffix = 0;
        while (suffix < old.Text.Length - prefix && suffix < replacement.Text.Length - prefix &&
            old.Text[^(suffix + 1)] == replacement.Text[^(suffix + 1)]) suffix++;
        if (local > prefix)
            local = local >= old.Text.Length - suffix
                ? replacement.Text.Length - (old.Text.Length - local)
                : prefix;
        return replacement.Start + Math.Clamp(local, 0, replacement.Text.Length);
    }

    internal void Select(int anchor, int caret)
    {
        Anchor = Math.Clamp(anchor, 0, Text.Length);
        Caret = Math.Clamp(caret, 0, Text.Length);
    }

    internal int MoveGrapheme(int offset, int direction)
    {
        int index = Array.BinarySearch(_graphemes, offset);
        if (direction < 0)
        {
            index = index >= 0 ? index - 1 : ~index - 1;
            return index >= 0 ? _graphemes[index] : 0;
        }
        index = index >= 0 ? index + 1 : ~index;
        return index < _graphemes.Length ? _graphemes[index] : Text.Length;
    }

    internal void Move(int direction, bool extend)
    {
        int next = !extend && Start != End ? (direction < 0 ? Start : End)
            : MoveGrapheme(Caret, direction);
        Select(extend ? Anchor : next, next);
    }
}
