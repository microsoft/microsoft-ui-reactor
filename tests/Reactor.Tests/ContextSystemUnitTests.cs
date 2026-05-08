using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 2 unit tests: ContextScope, Context, .Provide() modifier,
/// and UseContext hook in isolation. No reconciler, no WinUI controls.
/// </summary>
public class ContextSystemUnitTests
{
    private static readonly Context<string> TestTheme = new("light");
    private static readonly Context<int> TestCount = new(0);
    private static readonly Context<string?> TestSession = new(defaultValue: null);

    // ════════════════════════════════════════════════════════════════
    //  ContextScope tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ContextScope_Read_Returns_Default_When_Stack_Is_Empty()
    {
        var scope = new ContextScope();
        var value = scope.Read(TestTheme);
        Assert.Equal("light", value);
    }

    [Fact]
    public void ContextScope_Push_Then_Read_Returns_Pushed_Value()
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "dark" });

        var value = scope.Read(TestTheme);
        Assert.Equal("dark", value);

        scope.Pop(1);
    }

    [Fact]
    public void ContextScope_Nested_Push_Inner_Shadows_Outer_For_Same_Context()
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "dark" });
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "high-contrast" });

        Assert.Equal("high-contrast", scope.Read(TestTheme));

        scope.Pop(1);
        Assert.Equal("dark", scope.Read(TestTheme));

        scope.Pop(1);
        Assert.Equal("light", scope.Read(TestTheme)); // default
    }

    [Fact]
    public void ContextScope_Nested_Push_Different_Contexts_Both_Readable()
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "dark" });
        scope.Push(new Dictionary<ContextBase, object?> { [TestCount] = 42 });

        Assert.Equal("dark", scope.Read(TestTheme));
        Assert.Equal(42, scope.Read(TestCount));

        scope.Pop(1);
        scope.Pop(1);
    }

    [Fact]
    public void ContextScope_Pop_Restores_Previous_Value()
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "dark" });
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "blue" });

        Assert.Equal("blue", scope.Read(TestTheme));
        scope.Pop(1);
        Assert.Equal("dark", scope.Read(TestTheme));
        scope.Pop(1);
        Assert.Equal("light", scope.Read(TestTheme)); // default
    }

    [Fact]
    public void ContextScope_Version_Increments_On_Push_And_Pop()
    {
        var scope = new ContextScope();
        var v0 = scope.Version;

        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "dark" });
        var v1 = scope.Version;
        Assert.True(v1 > v0);

        scope.Pop(1);
        var v2 = scope.Version;
        Assert.True(v2 > v1);
    }

    [Fact]
    public void ContextScope_Nullable_Value_Works()
    {
        var scope = new ContextScope();
        Assert.Null(scope.Read(TestSession)); // default is null

        scope.Push(new Dictionary<ContextBase, object?> { [TestSession] = "user123" });
        Assert.Equal("user123", scope.Read(TestSession));

        scope.Pop(1);
        Assert.Null(scope.Read(TestSession));
    }

    // ════════════════════════════════════════════════════════════════
    //  .Provide() modifier tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Provide_Sets_ContextValues_On_Element_Record()
    {
        var el = new TextBlockElement("hello").Provide(TestTheme, "dark");

        Assert.NotNull(el.ContextValues);
        Assert.Single(el.ContextValues);
        Assert.Equal("dark", el.ContextValues[TestTheme]);
    }

    [Fact]
    public void Chained_Provide_Merges_Into_Single_Dictionary()
    {
        var el = new TextBlockElement("hello")
            .Provide(TestTheme, "dark")
            .Provide(TestCount, 42);

        Assert.NotNull(el.ContextValues);
        Assert.Equal(2, el.ContextValues.Count);
        Assert.Equal("dark", el.ContextValues[TestTheme]);
        Assert.Equal(42, el.ContextValues[TestCount]);
    }

    [Fact]
    public void Same_Context_Provided_Twice_Last_Write_Wins()
    {
        var el = new TextBlockElement("hello")
            .Provide(TestTheme, "dark")
            .Provide(TestTheme, "blue");

        Assert.NotNull(el.ContextValues);
        Assert.Single(el.ContextValues);
        Assert.Equal("blue", el.ContextValues[TestTheme]);
    }

    [Fact]
    public void Provide_Returns_Same_Element_Type()
    {
        var text = new TextBlockElement("hello").Provide(TestTheme, "dark");
        Assert.IsType<TextBlockElement>(text);
        Assert.Equal("hello", text.Content);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseContext hook tests (via RenderContext with mock ContextScope)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseContext_With_Scope_Returns_Scope_Value()
    {
        var ctx = new RenderContext();
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "dark" });

        ctx.BeginRender(() => { }, scope);
        var value = ctx.UseContext(TestTheme);

        Assert.Equal("dark", value);
        scope.Pop(1);
    }

    [Fact]
    public void UseContext_No_Provider_Returns_Default_Value()
    {
        var ctx = new RenderContext();
        var scope = new ContextScope();

        ctx.BeginRender(() => { }, scope);
        var value = ctx.UseContext(TestTheme);

        Assert.Equal("light", value); // Context default
    }

    [Fact]
    public void UseContext_Without_Scope_Returns_Default_Value()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var value = ctx.UseContext(TestTheme);

        Assert.Equal("light", value);
    }

    [Fact]
    public void UseContext_Hook_Order_Violation_Throws()
    {
        var ctx = new RenderContext();
        var scope = new ContextScope();

        // First render: UseState then UseContext
        ctx.BeginRender(() => { }, scope);
        ctx.UseState(0);
        ctx.UseContext(TestTheme);

        // Second render: try UseContext where UseState was
        ctx.BeginRender(() => { }, scope);
        var ex = Assert.Throws<HookOrderException>(() => ctx.UseContext(TestTheme));
        Assert.Contains("ContextHookState", ex.Message);
    }

    [Fact]
    public void UseContext_Multiple_Contexts_Independent()
    {
        var ctx = new RenderContext();
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?>
        {
            [TestTheme] = "dark",
            [TestCount] = 99,
        });

        ctx.BeginRender(() => { }, scope);
        var theme = ctx.UseContext(TestTheme);
        var count = ctx.UseContext(TestCount);

        Assert.Equal("dark", theme);
        Assert.Equal(99, count);
        scope.Pop(2);
    }

    [Fact]
    public void UseContext_Returns_Updated_Value_On_Rerender()
    {
        var ctx = new RenderContext();
        var scope = new ContextScope();

        // First render
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "dark" });
        ctx.BeginRender(() => { }, scope);
        Assert.Equal("dark", ctx.UseContext(TestTheme));
        scope.Pop(1);

        // Second render with different value
        scope.Push(new Dictionary<ContextBase, object?> { [TestTheme] = "blue" });
        ctx.BeginRender(() => { }, scope);
        Assert.Equal("blue", ctx.UseContext(TestTheme));
        scope.Pop(1);
    }

    [Fact]
    public void UseContext_ContextHooks_Enumerates_Context_Hooks()
    {
        var ctx = new RenderContext();
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?>
        {
            [TestTheme] = "dark",
            [TestCount] = 5,
        });

        ctx.BeginRender(() => { }, scope);
        ctx.UseContext(TestTheme);
        ctx.UseContext(TestCount);

        var hooks = ctx.ContextHooks.ToList();
        Assert.Equal(2, hooks.Count);
        Assert.Same(TestTheme, hooks[0].Context);
        Assert.Equal("dark", hooks[0].LastValue);
        Assert.Same(TestCount, hooks[1].Context);
        Assert.Equal(5, hooks[1].LastValue);

        scope.Pop(2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Context type tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Context_Stores_Default_Value()
    {
        var ctx = new Context<int>(42);
        Assert.Equal(42, ctx.DefaultValue);
    }

    [Fact]
    public void Context_DefaultValueBoxed_Returns_Boxed_Default()
    {
        ContextBase ctx = new Context<int>(42);
        Assert.Equal(42, ctx.DefaultValueBoxed);
    }

    [Fact]
    public void Context_DebugName_Uses_CallerMemberName()
    {
        // When defined as a local variable or explicitly named
        var ctx = new Context<string>("default", name: "MyContext");
        Assert.Equal("MyContext", ctx.DebugName);
    }

    // ════════════════════════════════════════════════════════════════
    //  Element.ContextValuesEqual tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShallowEquals_Includes_ContextValues_Comparison()
    {
        var a = new TextBlockElement("hi").Provide(TestTheme, "dark");
        var b = new TextBlockElement("hi").Provide(TestTheme, "dark");
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_Different_ContextValues_Returns_False()
    {
        var a = new TextBlockElement("hi").Provide(TestTheme, "dark");
        var b = new TextBlockElement("hi").Provide(TestTheme, "light");
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_One_Has_ContextValues_Other_Doesnt_Returns_False()
    {
        var a = new TextBlockElement("hi").Provide(TestTheme, "dark");
        var b = new TextBlockElement("hi");
        Assert.False(Element.ShallowEquals(a, b));
    }
}
