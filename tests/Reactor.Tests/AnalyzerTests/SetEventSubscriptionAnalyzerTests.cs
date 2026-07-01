using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

public class SetEventSubscriptionAnalyzerTests
{
    private const string Stubs = @"
using System;

public class FakeElement
{
    public event Action? PointerPressed { add {} remove {} }
    public event Action? PointerMoved { add {} remove {} }
    public event Action? PointerReleased { add {} remove {} }
    public event Action? Tapped { add {} remove {} }
    public event Action? KeyDown { add {} remove {} }
    public event Action? PointerEntered { add {} remove {} }
    public event Action? PointerExited { add {} remove {} }
    public event Action? PointerCanceled { add {} remove {} }
    public event Action? PointerCaptureLost { add {} remove {} }
    public event Action? PointerWheelChanged { add {} remove {} }
    public event Action? DoubleTapped { add {} remove {} }
    public event Action? RightTapped { add {} remove {} }
    public event Action? Holding { add {} remove {} }
    public event Action? KeyUp { add {} remove {} }
    public event Action? PreviewKeyDown { add {} remove {} }
    public event Action? PreviewKeyUp { add {} remove {} }
    public event Action? CharacterReceived { add {} remove {} }
    public event Action? AccessKeyDisplayRequested { add {} remove {} }
    public event Action? GotFocus { add {} remove {} }
    public event Action? LostFocus { add {} remove {} }
    public event Action? DragEnter { add {} remove {} }
    public event Action? DragOver { add {} remove {} }
    public event Action? DragLeave { add {} remove {} }
    public event Action? Drop { add {} remove {} }
    public event Action? SizeChanged { add {} remove {} }
    public event Action? CustomEvent { add {} remove {} }

    public FakeElement Set(Action<FakeElement> configure) { configure(this); return this; }
    public FakeElement OnMount(Action<FakeElement> configure) { configure(this); return this; }
}

public sealed class ChildTarget
{
    public event Action? Tapped { add {} remove {} }
}

public sealed class PointerRoutedEventArgs {}
public sealed class TappedRoutedEventArgs {}

public class TypedFakeElement
{
    public event Action<object, TappedRoutedEventArgs>? Tapped { add {} remove {} }

    public TypedFakeElement Set(Action<TypedFakeElement> configure) { configure(this); return this; }
}

public static class TypedFakeElementExtensions
{
    public static TypedFakeElement OnTapped(this TypedFakeElement el, Action<object, TappedRoutedEventArgs> handler) => el;
}

public static class FakeElementExtensions
{
    public static FakeElement OnPointerPressed(this FakeElement el, Action handler) => el;
    public static FakeElement OnPointerMoved(this FakeElement el, Action handler) => el;
    public static FakeElement OnPointerReleased(this FakeElement el, Action handler) => el;
    public static FakeElement OnTapped(this FakeElement el, Action handler) => el;
    public static FakeElement OnKeyDown(this FakeElement el, Action handler) => el;
    public static FakeElement OnPointerEntered(this FakeElement el, Action handler) => el;
    public static FakeElement OnPointerExited(this FakeElement el, Action handler) => el;
    public static FakeElement OnPointerCanceled(this FakeElement el, Action handler) => el;
    public static FakeElement OnPointerCaptureLost(this FakeElement el, Action handler) => el;
    public static FakeElement OnPointerWheelChanged(this FakeElement el, Action handler) => el;
    public static FakeElement OnDoubleTapped(this FakeElement el, Action handler) => el;
    public static FakeElement OnRightTapped(this FakeElement el, Action handler) => el;
    public static FakeElement OnHolding(this FakeElement el, Action handler) => el;
    public static FakeElement OnKeyUp(this FakeElement el, Action handler) => el;
    public static FakeElement OnPreviewKeyDown(this FakeElement el, Action handler) => el;
    public static FakeElement OnPreviewKeyUp(this FakeElement el, Action handler) => el;
    public static FakeElement OnCharacterReceived(this FakeElement el, Action handler) => el;
    public static FakeElement OnAccessKeyDisplayRequested(this FakeElement el, Action handler) => el;
    public static FakeElement OnGotFocus(this FakeElement el, Action handler) => el;
    public static FakeElement OnLostFocus(this FakeElement el, Action handler) => el;
    public static FakeElement OnSizeChanged(this FakeElement el, Action handler) => el;
}
";

    [Fact]
    public async Task Fires_For_Direct_Set_Event_Subscription()
    {
        var source = Stubs + @"
class C
{
    void OnTapped() {}

    void M()
    {
        var el = new FakeElement();
        {|REACTOR_EVENT_001:el.Set(fe => fe.Tapped += OnTapped)|};
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Block_Bodied_Set_Event_Subscription()
    {
        var source = Stubs + @"
class C
{
    void OnTapped() {}

    void M()
    {
        var el = new FakeElement();
        {|REACTOR_EVENT_001:el.Set(fe => { fe.Tapped += OnTapped; })|};
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Expanded_Event_Surface()
    {
        var source = Stubs + @"
class C
{
    void OnHandler() {}

    void M()
    {
        var el = new FakeElement();
        {|REACTOR_EVENT_001:el.Set(fe => fe.PointerPressed += OnHandler)|};
        {|REACTOR_EVENT_001:el.Set(fe => fe.RightTapped += OnHandler)|};
        {|REACTOR_EVENT_001:el.Set(fe => fe.SizeChanged += OnHandler)|};
        {|REACTOR_EVENT_001:el.Set(fe => fe.AccessKeyDisplayRequested += OnHandler)|};
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Event_Without_Modifier()
    {
        var source = Stubs + @"
class C
{
    void OnCustom() {}

    void M()
    {
        var el = new FakeElement();
        el.Set(fe => fe.CustomEvent += OnCustom);
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_OnMount_Subscription()
    {
        var source = Stubs + @"
class C
{
    void OnTapped() {}

    void M()
    {
        var el = new FakeElement();
        el.OnMount(fe => fe.Tapped += OnTapped);
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Member_Chain_Subscription()
    {
        var source = Stubs + @"
class C
{
    void OnTapped() {}

    void M(ChildTarget child)
    {
        var el = new FakeElement();
        el.Set(fe => child.Tapped += OnTapped);
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Remove_Assignment()
    {
        var source = Stubs + @"
class C
{
    void OnTapped() {}

    void M()
    {
        var el = new FakeElement();
        el.Set(fe => fe.Tapped -= OnTapped);
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Multi_Statement_Block_Lambda()
    {
        var source = Stubs + @"
class C
{
    void OnTapped() {}
    void Log() {}

    void M()
    {
        var el = new FakeElement();
        el.Set(fe => { Log(); fe.Tapped += OnTapped; });
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Drag_Target_Events()
    {
        var source = Stubs + @"
class C
{
    void OnHandler() {}

    void M()
    {
        var el = new FakeElement();
        el.Set(fe => fe.DragEnter += OnHandler);
        el.Set(fe => fe.DragOver += OnHandler);
        el.Set(fe => fe.DragLeave += OnHandler);
        el.Set(fe => fe.Drop += OnHandler);
    }
}";

        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_To_OnTapped()
    {
        var before = Stubs + @"
class C
{
    void OnTapped() {}

    void M()
    {
        var el = new FakeElement();
        {|REACTOR_EVENT_001:el.Set(fe => fe.Tapped += OnTapped)|};
    }
}";

        var after = Stubs + @"
class C
{
    void OnTapped() {}

    void M()
    {
        var el = new FakeElement();
        el.OnTapped(OnTapped);
    }
}";

        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Block_Body_To_OnTapped()
    {
        var before = Stubs + @"
class C
{
    void OnTapped() {}

    void M()
    {
        var el = new FakeElement();
        {|REACTOR_EVENT_001:el.Set(fe => { fe.Tapped += OnTapped; })|};
    }
}";

        var after = Stubs + @"
class C
{
    void OnTapped() {}

    void M()
    {
        var el = new FakeElement();
        el.OnTapped(OnTapped);
    }
}";

        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_With_Realistic_Typed_Handler_Signature()
    {
        var before = Stubs + @"
class C
{
    void OnTapped(object sender, TappedRoutedEventArgs args) {}

    void M()
    {
        var el = new TypedFakeElement();
        {|REACTOR_EVENT_001:el.Set(fe => fe.Tapped += OnTapped)|};
    }
}";

        var after = Stubs + @"
class C
{
    void OnTapped(object sender, TappedRoutedEventArgs args) {}

    void M()
    {
        var el = new TypedFakeElement();
        el.OnTapped(OnTapped);
    }
}";

        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}