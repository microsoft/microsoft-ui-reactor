// Spec 038 Phase-1 ship gate: comprehensive Reactor stubs for tuning runs.
//
// The corpus's `before.text` is real, restoreable Reactor C# but we deliberately
// don't load Reactor.dll into the test compilation (TestCompilation.Create
// excludes Reactor / WinUI assemblies). To run the SymbolSuggester semantically
// we need stub types covering the surface used in the corpus.
//
// These stubs are NOT meant to be functionally accurate — only structurally
// rich enough for Roslyn to bind references in the corpus's `before.text` so
// the suggester sees the receiver and member names it expects. Over-stubbing
// is the conservative bias here; under-stubbing makes the suggester silent
// (because receiver type is null), which corrupts the false-positive count.
//
// Add to this set when a tuning run reports "no diagnostic at expected
// location" for a row whose corpus diag is in our handled-codes set — that's
// usually because the receiver type isn't stubbed yet.

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Tuning;

internal static class ReactorCorpusStubs
{
    public const string Source = """
namespace Microsoft.UI.Xaml
{
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public enum TextAlignment { Left, Center, Right, Justify }
    public enum TextWrapping { NoWrap, Wrap, WrapWholeWords }
}

namespace Microsoft.UI.Reactor
{
    using System;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Layout;

    public abstract class Element
    {
        public Element Padding(double v) => this;
        public Element Background(string s) => this;
        public Element Margin(double v) => this;
        public Element Grid(int row, int col) => this;
        public Element WithKey(string key) => this;
    }

    public class Component : Element
    {
        public virtual Element Render() => default!;
        public (T, Action<T>) UseState<T>(T initial) => (initial, _ => { });
    }

    public static class ReactorApp
    {
        public static void Run<T>(string title, int width = 0, int height = 0) where T : Component, new() { }
    }

    public static class Theme
    {
        public static string SubtleFill => "subtle";
        public static string Background => "bg";
        public static string Foreground => "fg";
        public static string Accent => "accent";
    }

    public static class Factories
    {
        public static Microsoft.UI.Reactor.Core.ButtonElement Button(string label, Action? onClick = null) => new();
        public static Microsoft.UI.Reactor.Core.ButtonElement Button(Element content, Action? onClick = null) => new();
        public static Microsoft.UI.Reactor.Core.TextBlockElement TextBlock(string content) => new();
        public static Microsoft.UI.Reactor.Core.TextBlockElement Heading(string content) => new();
        public static Microsoft.UI.Reactor.Core.TextBlockElement Caption(string content) => new();
        public static Microsoft.UI.Reactor.Core.TextBlockElement Body(string content) => new();
        public static Microsoft.UI.Reactor.Core.GridElement Grid(
            GridSize[] columns = null!, GridSize[] rows = null!, params Element[] children) => new();
        public static Microsoft.UI.Reactor.Core.BorderElement Border(Element child = null!) => new();
        public static Microsoft.UI.Reactor.Core.RadioButtonElement RadioButton(string label) => new();
        public static Microsoft.UI.Reactor.Core.ContentDialogElement ContentDialog(Element content = null!) => new();
        public static Microsoft.UI.Reactor.Core.StackPanelElement StackPanel(params Element[] children) => new();
    }
}

namespace Microsoft.UI.Reactor.Layout
{
    public sealed class GridSize
    {
        public static GridSize Auto { get; } = new();
        public static GridSize Star() => new();
        public static GridSize Length(double v) => new();
    }
}

namespace Microsoft.UI.Reactor.Core
{
    using System;
    using Microsoft.UI.Reactor;

    public class ButtonElement : Element
    {
        public string Label { get; set; } = "";
        public Action? OnClickHandler { get; set; }
        public Microsoft.UI.Xaml.HorizontalAlignment HorizontalAlignment { get; set; }
        public ButtonElement Set(Action<ButtonElement> mutate) { mutate(this); return this; }
    }

    public class GridElement : Element
    {
        public double RowSpacing { get; set; }
        public double ColumnSpacing { get; set; }
        public GridElement Set(Action<GridElement> mutate) { mutate(this); return this; }
    }

    public class TextBlockElement : Element
    {
        public double FontSize { get; set; }
        public Microsoft.UI.Xaml.HorizontalAlignment HorizontalAlignment { get; set; }
        public Microsoft.UI.Xaml.TextAlignment TextAlignment { get; set; }
        public Microsoft.UI.Xaml.TextWrapping TextWrapping { get; set; }
        public TextBlockElement Set(Action<TextBlockElement> mutate) { mutate(this); return this; }
    }

    public class BorderElement : Element
    {
        public int GridRowSpan { get; set; }
        public BorderElement Set(Action<BorderElement> mutate) { mutate(this); return this; }
    }

    public class RadioButtonElement : Element
    {
        public Microsoft.UI.Xaml.HorizontalAlignment HorizontalAlignment { get; set; }
        public RadioButtonElement Set(Action<RadioButtonElement> mutate) { mutate(this); return this; }
    }

    public class ContentDialogElement : Element
    {
        public bool IsOpen { get; set; }
        public ContentDialogElement Set(Action<ContentDialogElement> mutate) { mutate(this); return this; }
    }

    public class StackPanelElement : Element
    {
        public double Spacing { get; set; }
        public StackPanelElement Set(Action<StackPanelElement> mutate) { mutate(this); return this; }
    }
}
""";
}
