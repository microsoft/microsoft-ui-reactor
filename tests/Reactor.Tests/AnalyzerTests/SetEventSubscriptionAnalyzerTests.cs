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
    public static FakeElement OnDragEnter(this FakeElement el, Action handler) => el;
    public static FakeElement OnDragOver(this FakeElement el, Action handler) => el;
    public static FakeElement OnDragLeave(this FakeElement el, Action handler) => el;
    public static FakeElement OnDrop(this FakeElement el, Action handler) => el;
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
    void OnDrop() {}

    void M()
    {
        var el = new FakeElement();
        {|REACTOR_EVENT_001:el.Set(fe => { fe.Drop += OnDrop; })|};
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
        {|REACTOR_EVENT_001:el.Set(fe => fe.DragEnter += OnHandler)|};
        {|REACTOR_EVENT_001:el.Set(fe => fe.DragLeave += OnHandler)|};
        {|REACTOR_EVENT_001:el.Set(fe => fe.SizeChanged += OnHandler)|};
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
    public async Task CodeFix_Rewrites_Block_Body_To_OnDrop()
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
    public async Task Analyzer_Fires_But_CodeFix_Suppressed_For_DragOver()
    {
        var code = Stubs + @"
class C
{
    void OnDragOver() {}

    void M()
    {
        var el = new FakeElement();
        {|REACTOR_EVENT_001:el.Set(fe => fe.DragOver += OnDragOver)|};
    }
}";

        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}