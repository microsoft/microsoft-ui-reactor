using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 057 §9.3 — headless-safe reactive-cell contract for <see cref="ElementRef"/>.
/// Non-null value-change, null-after-non-null, typed projection, and re-entrancy
/// assertions live in the <c>ReactiveElementRefCell</c> selftest fixture because
/// constructing real WinUI controls requires a XAML host.
/// </summary>
public class ReactiveElementRefTests
{
    [Fact]
    public void SetCurrent_Null_On_Fresh_Cell_Is_NoOp()
    {
        var cell = new ElementRef();
        var fireCount = 0;
        cell.CurrentChanged += _ => fireCount++;

        cell.SetCurrent(null);

        Assert.Null(cell.Current);
        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void CurrentChanged_Subscribe_Unsubscribe_Leaves_No_Leftover_Handler_On_NoOp()
    {
        var cell = new ElementRef();
        var fireCount = 0;
        void Handler(Microsoft.UI.Xaml.FrameworkElement? _) => fireCount++;

        cell.CurrentChanged += Handler;
        cell.CurrentChanged -= Handler;
        cell.SetCurrent(null);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void Typed_CurrentChanged_Subscribe_Unsubscribe_Is_Headless_Safe()
    {
        var typed = TypedElementRef.Create<Button>();
        ElementRef inner = typed;
        var fireCount = 0;
        void Handler(Button? _) => fireCount++;

        typed.CurrentChanged += Handler;
        typed.CurrentChanged -= Handler;
        inner.SetCurrent(null);

        Assert.Null(typed.Current);
        Assert.Equal(0, fireCount);
    }
}
