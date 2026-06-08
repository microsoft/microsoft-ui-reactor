using System;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class RichTextBlockDescriptorTests
{
    [Fact]
    public void Descriptor_BindsStandardElementPaddingToRichTextBlockPadding()
    {
        var entry = Assert.Single(RichTextBlockDescriptor.Descriptor.Properties, IsPaddingEntry);
        var get = GetPrivateField<Func<RichTextBlockElement, Optional<Thickness>>>(entry, "_get");
        var set = GetPrivateField<Action<WinUI.RichTextBlock, Thickness>>(entry, "_set");

        var unset = new RichTextBlockElement("cell");
        var padded = unset.Padding(1, 2, 3, 4);

        Assert.False(get(unset).HasValue);
        Assert.True(get(padded).HasValue);
        Assert.Equal(new Thickness(1, 2, 3, 4), get(padded).Value);
        Assert.NotNull(set);
    }

    private static bool IsPaddingEntry(PropEntry<RichTextBlockElement, WinUI.RichTextBlock> entry)
    {
        var type = entry.GetType();
        return type.IsGenericType
            && type.GetGenericTypeDefinition().Name == "OneWayClearValuePropEntry`3"
            && type.GenericTypeArguments[2] == typeof(Thickness);
    }

    private static T GetPrivateField<T>(object owner, string name)
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field.GetValue(owner));
    }
}
