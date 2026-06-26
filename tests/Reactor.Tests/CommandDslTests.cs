using Microsoft.UI.Reactor.Core;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for command-aware DSL overloads: Button(cmd), AppBarButton(cmd), MenuItem(cmd), MenuItem(cmd, param).
/// Pure C# record tests, no WinUI thread needed.
/// </summary>
public class CommandDslTests
{
    private static readonly Command _testCmd = new()
    {
        Label = "Save",
        Execute = () => { },
        Icon = new SymbolIconData("Save"),
        Accelerator = new KeyboardAcceleratorData(VirtualKey.S, VirtualKeyModifiers.Control),
        AccessKey = "S",
        Description = "Save the file",
    };

    // ════════════════════════════════════════════════════════════════
    //  Button(command)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Button_Command_Maps_Label_And_IsEnabled()
    {
        var el = Button(_testCmd);
        Assert.Equal("Save", el.Label);
        Assert.True(el.EffectiveIsEnabled);
        // issue #637 — dispatch flows from the typed Command, not a pre-baked OnClick closure.
        Assert.Null(el.OnClick);
        Assert.Same(_testCmd, el.Command);
    }

    [Fact]
    public void Button_Disabled_Command_Maps_IsEnabled_False()
    {
        var cmd = _testCmd with { CanExecute = false };
        var el = Button(cmd);
        Assert.False(el.EffectiveIsEnabled);
    }

    // ════════════════════════════════════════════════════════════════
    //  AppBarButton(command)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void AppBarButton_Command_Maps_All_Metadata()
    {
        var el = AppBarButton(_testCmd);
        Assert.Equal("Save", el.Label);
        Assert.True(el.IsEnabled);
        Assert.IsType<SymbolIconData>(el.IconElement);
        Assert.Equal("Save", ((SymbolIconData)el.IconElement!).Symbol);
        Assert.NotNull(el.KeyboardAccelerators);
        Assert.Single(el.KeyboardAccelerators!);
        Assert.Equal(VirtualKey.S, el.KeyboardAccelerators![0].Key);
        Assert.Equal(VirtualKeyModifiers.Control, el.KeyboardAccelerators[0].Modifiers);
        Assert.Equal("S", el.AccessKey);
    }

    [Fact]
    public void AppBarButton_Disabled_Command()
    {
        var cmd = _testCmd with { CanExecute = false };
        var el = AppBarButton(cmd);
        Assert.False(el.IsEnabled);
    }

    [Fact]
    public void AppBarButton_No_Icon_Results_In_No_Icon()
    {
        var cmd = new Command { Label = "Close", Execute = () => { } };
        var el = AppBarButton(cmd);
        Assert.Null(el.IconElement);
    }

    [Fact]
    public void AppBarButton_No_Accelerator_Results_In_No_Accelerator()
    {
        var cmd = new Command { Label = "Share", Execute = () => { }, Icon = new SymbolIconData("Share") };
        var el = AppBarButton(cmd);
        Assert.Null(el.KeyboardAccelerators);
    }

    // ════════════════════════════════════════════════════════════════
    //  MenuItem(command)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MenuItem_Command_Maps_All_Metadata()
    {
        var el = MenuItem(_testCmd);
        Assert.Equal("Save", el.Text);
        Assert.True(el.IsEnabled);
        Assert.IsType<SymbolIconData>(el.IconElement);
        Assert.NotNull(el.KeyboardAccelerators);
        Assert.Equal("S", el.AccessKey);
    }

    [Fact]
    public void MenuItem_Disabled_Command()
    {
        var cmd = _testCmd with { CanExecute = false };
        var el = MenuItem(cmd);
        Assert.False(el.IsEnabled);
    }

    // ════════════════════════════════════════════════════════════════
    //  MenuItem<T>(command, parameter)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MenuItem_Parameterized_Invokes_Execute_With_Argument()
    {
        string? received = null;
        var cmd = new Command<string>
        {
            Label = "Delete",
            Execute = item => received = item,
        };
        var el = MenuItem(cmd, "item-42");

        Assert.Equal("Delete", el.Text);
        Assert.NotNull(el.OnClick);
        el.OnClick!();
        Assert.Equal("item-42", received);
    }

    [Fact]
    public void MenuItem_Parameterized_Disabled()
    {
        var cmd = new Command<int> { Label = "Select", CanExecute = false };
        var el = MenuItem(cmd, 7);
        Assert.False(el.IsEnabled);
    }
}
