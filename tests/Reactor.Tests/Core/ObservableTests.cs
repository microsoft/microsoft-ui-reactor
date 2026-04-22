using System.ComponentModel;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

public class ObservableTests
{
    [Fact]
    public void DefaultConstructor_UsesDefaultOfT()
    {
        var cell = new Observable<int>();
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void InitialConstructor_StoresProvidedValue()
    {
        var cell = new Observable<string>("hello");
        Assert.Equal("hello", cell.Value);
    }

    [Fact]
    public void Assigning_DifferentValue_RaisesPropertyChanged()
    {
        var cell = new Observable<int>(1);
        var fired = 0;
        string? lastPropName = null;
        cell.PropertyChanged += (_, e) => { fired++; lastPropName = e.PropertyName; };

        cell.Value = 2;

        Assert.Equal(1, fired);
        Assert.Equal(nameof(Observable<int>.Value), lastPropName);
        Assert.Equal(2, cell.Value);
    }

    [Fact]
    public void Assigning_SameValue_DoesNotFire()
    {
        var cell = new Observable<int>(1);
        var fired = 0;
        cell.PropertyChanged += (_, _) => fired++;

        cell.Value = 1;

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Assigning_UsesDefaultEqualityComparer_ForNullableReference()
    {
        var cell = new Observable<string?>(null);
        var fired = 0;
        cell.PropertyChanged += (_, _) => fired++;

        cell.Value = null;
        Assert.Equal(0, fired);

        cell.Value = "x";
        Assert.Equal(1, fired);

        cell.Value = null;
        Assert.Equal(2, fired);
    }

    [Fact]
    public void ImplicitConversion_UnwrapsValue()
    {
        var cell = new Observable<int>(42);
        int raw = cell;
        Assert.Equal(42, raw);
    }

    [Fact]
    public void ToString_ReturnsInnerToString()
    {
        var cell = new Observable<int>(7);
        Assert.Equal("7", cell.ToString());

        var nullCell = new Observable<string?>(null);
        Assert.Equal(string.Empty, nullCell.ToString());
    }

    [Fact]
    public void Implements_INotifyPropertyChanged()
    {
        Assert.IsAssignableFrom<INotifyPropertyChanged>(new Observable<bool>());
    }
}
