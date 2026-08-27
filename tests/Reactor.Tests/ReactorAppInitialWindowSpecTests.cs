using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1143 — <c>ReactorApp.BuildInitialWindowSpec</c> is the single point where the
/// primary window's <see cref="WindowSpec"/> is decided, for both the flat
/// <c>Run(title, width, height, …, icon)</c> overloads and the
/// <c>Run(WindowSpec, …)</c> ones.
/// </summary>
/// <remarks>
/// Headless by construction: the method touches no <c>Microsoft.UI.Xaml</c> type, which is
/// why it lives on the static <c>ReactorApp</c> rather than on <c>ReactorApplication</c>
/// (constructing anything deriving from <c>Application</c> throws <c>COMException</c> in a
/// headless test host).
/// </remarks>
public class ReactorAppInitialWindowSpecTests
{
    [Fact]
    public void Icon_Option_Reaches_The_Synthesized_Spec()
    {
        // The whole point of the issue: an icon passed to Run must survive into the
        // WindowSpec that opens the primary window.
        var icon = WindowIcon.FromPath(@"C:\Assets\App.ico");

        var spec = ReactorApp.BuildInitialWindowSpec(
            new ReactorAppOptions(RootFactory: () => null!, WindowIcon: icon));

        Assert.Same(icon, spec.Icon);
    }

    [Fact]
    public void No_Icon_Option_Leaves_The_Spec_Icon_Null()
    {
        // Guards the fallback contract: a null spec.Icon is what lets ReactorWindow
        // reach for Assets\AppIcon.ico / the exe PE icon.
        var spec = ReactorApp.BuildInitialWindowSpec(
            new ReactorAppOptions(RootFactory: () => null!));

        Assert.Null(spec.Icon);
    }

    [Fact]
    public void Flat_Options_Map_Onto_The_Spec()
    {
        var spec = ReactorApp.BuildInitialWindowSpec(new ReactorAppOptions(
            RootFactory: () => null!,
            WindowTitle: "Mapped",
            WindowWidth: 640,
            WindowHeight: 480));

        Assert.Equal("Mapped", spec.Title);
        Assert.Equal(640, spec.Width);
        Assert.Equal(480, spec.Height);
        Assert.Equal(PresenterKind.Overlapped, spec.Presenter);
    }

    [Fact]
    public void FullScreen_Option_Selects_The_FullScreen_Presenter()
    {
        var spec = ReactorApp.BuildInitialWindowSpec(
            new ReactorAppOptions(RootFactory: () => null!, FullScreen: true));

        Assert.Equal(PresenterKind.FullScreen, spec.Presenter);
    }

    [Fact]
    public void Explicit_Spec_Wins_Over_Every_Flat_Option()
    {
        // The Run(WindowSpec, …) overloads pass both, and the spec must not be
        // partially overwritten by the flattened copies that ride alongside it.
        var supplied = new WindowSpec
        {
            Title = "From spec",
            Width = 1024,
            MinWidth = 320,
            Icon = WindowIcon.FromPath(@"C:\Assets\Spec.ico"),
        };

        var spec = ReactorApp.BuildInitialWindowSpec(new ReactorAppOptions(
            RootFactory: () => null!,
            WindowTitle: "From flat options",
            WindowWidth: 111,
            WindowIcon: WindowIcon.FromPath(@"C:\Assets\Flat.ico"),
            InitialWindowSpec: supplied));

        Assert.Same(supplied, spec);
        Assert.Equal("From spec", spec.Title);
        Assert.Equal(1024, spec.Width);
        Assert.Equal(320, spec.MinWidth);
        Assert.Equal(@"C:\Assets\Spec.ico", spec.Icon!.Source);
    }

    [Fact]
    public void Spec_Fields_Beyond_The_Flat_Options_Survive()
    {
        // These are exactly the fields Run<TRoot>(string, …) could never reach — the
        // structural gap the WindowSpec overloads close.
        var supplied = new WindowSpec
        {
            Backdrop = BackdropChoice.Of(BackdropKind.Mica),
            CornerStyle = WindowCornerStyle.Rounded,
            MaxHeight = 900,
            PersistenceId = "main",
            PersistPlacement = true,
        };

        var spec = ReactorApp.BuildInitialWindowSpec(
            new ReactorAppOptions(RootFactory: () => null!, InitialWindowSpec: supplied));

        Assert.Equal(BackdropKind.Mica, spec.Backdrop!.Kind);
        Assert.Equal(WindowCornerStyle.Rounded, spec.CornerStyle);
        Assert.Equal(900, spec.MaxHeight);
        Assert.Equal("main", spec.PersistenceId);
        Assert.True(spec.PersistPlacement);
    }
}
