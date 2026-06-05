using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Optional&lt;T&gt; properties in the reflection-backed PropertyGrid path.
/// </summary>
public class PropertyGridOptionalTests
{
    private class OptionalModel
    {
        public Optional<string> Title { get; set; } = Optional<string>.Of("hello");
    }

    [Fact]
    public void Optional_Property_Is_ReadOnly_Label_Editor()
    {
        var meta = ReflectionTypeMetadataProvider.CreateMetadata(typeof(OptionalModel));
        var model = new OptionalModel();

        var descriptor = Assert.Single(meta.Decompose!(model));

        Assert.Equal(typeof(Optional<string>), descriptor.FieldType);
        Assert.True(descriptor.IsReadOnly);
        Assert.Null(descriptor.SetValue);
        Assert.NotNull(descriptor.Editor);

        var editor = Assert.IsType<TextBlockElement>(descriptor.Editor!(descriptor.GetValue(model)!, _ => { }));
        Assert.Equal("hello", editor.Content);

        model.Title = Optional<string>.Unset;
        var unsetEditor = Assert.IsType<TextBlockElement>(descriptor.Editor!(descriptor.GetValue(model)!, _ => { }));
        Assert.Equal("Unset", unsetEditor.Content);
    }
}
