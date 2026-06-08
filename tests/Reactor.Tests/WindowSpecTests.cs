using Microsoft.UI.Reactor;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §1.1 / §1.7 — WindowSpec construction and validation invariants.
/// Pure-data tests that do not require a XAML Application context.
/// </summary>
public class WindowSpecTests
{
    [Fact]
    public void Defaults_Are_Spec_036_Defaults()
    {
        var spec = new WindowSpec();
        Assert.Equal("Reactor App", spec.Title);
        Assert.Equal(1024, spec.Width);
        Assert.Equal(768, spec.Height);
        Assert.Equal(WindowStartPosition.Default, spec.StartPosition);
        Assert.Equal(PresenterKind.Overlapped, spec.Presenter);
        Assert.Equal(WindowResizeMode.CanResize, spec.ResizeMode);
        Assert.Null(spec.AspectRatio);
        Assert.False(spec.IsMovableByBackground);
        Assert.True(spec.IsMinimizable);
        Assert.True(spec.IsMaximizable);
        Assert.Equal(WindowStyle.Default, spec.Style);
        Assert.Equal(WindowCornerStyle.Default, spec.CornerStyle);
        Assert.Equal(WindowLevel.Normal, spec.Level);
        Assert.Equal(WindowSizeToContent.Manual, spec.SizeToContent);
        Assert.True(spec.ShowInTaskbar);
        Assert.True(spec.ShowInSwitcher);
        Assert.False(spec.PersistPlacement);
        Assert.Equal(WindowStartPosition.Default, spec.PersistenceFallback);
        Assert.Null(spec.ExtendsContentIntoTitleBar);
        Assert.True(spec.ActivateOnOpen);
        Assert.Null(spec.Embed);
        spec.Validate(); // defaults must be valid
    }

    [Fact]
    public void Validate_Rejects_NonPositive_Width()
    {
        var spec = new WindowSpec { Width = 0, Height = 100 };
        var ex = Assert.Throws<ArgumentException>(() => spec.Validate());
        Assert.Contains("Width", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_NonPositive_Height()
    {
        var spec = new WindowSpec { Width = 100, Height = -1 };
        var ex = Assert.Throws<ArgumentException>(() => spec.Validate());
        Assert.Contains("Height", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_Min_Greater_Than_Max_Width()
    {
        var spec = new WindowSpec { MinWidth = 800, MaxWidth = 400 };
        Assert.Throws<ArgumentException>(() => spec.Validate());
    }

    [Fact]
    public void Validate_Rejects_Min_Greater_Than_Max_Height()
    {
        var spec = new WindowSpec { MinHeight = 600, MaxHeight = 300 };
        Assert.Throws<ArgumentException>(() => spec.Validate());
    }

    [Fact]
    public void Validate_Rejects_Manual_Without_Position()
    {
        var spec = new WindowSpec { StartPosition = WindowStartPosition.Manual };
        var ex = Assert.Throws<ArgumentException>(() => spec.Validate());
        Assert.Contains("ManualPosition", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_Position_Without_Manual()
    {
        var spec = new WindowSpec
        {
            StartPosition = WindowStartPosition.Default,
            ManualPosition = (10, 10),
        };
        var ex = Assert.Throws<ArgumentException>(() => spec.Validate());
        Assert.Contains("ManualPosition", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Accepts_Manual_With_Position()
    {
        var spec = new WindowSpec
        {
            StartPosition = WindowStartPosition.Manual,
            ManualPosition = (50, 50),
        };
        spec.Validate();
    }

    [Fact]
    public void Validate_Rejects_Embed_With_NonPositive_HostPid()
    {
        var spec = new WindowSpec { Embed = new EmbedRequest(WindowEmbedStyle.Child, 0, InitialVisibility: false) };

        var ex = Assert.Throws<ArgumentException>(() => spec.Validate());

        Assert.Contains("HostPid", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_Child_Embed_With_Owner()
    {
        var owner = (ReactorWindow)global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ReactorWindow));
        var spec = new WindowSpec { Owner = owner, Embed = new EmbedRequest(WindowEmbedStyle.Child, 123, InitialVisibility: false) };

        var ex = Assert.Throws<ArgumentException>(() => spec.Validate());

        Assert.Contains("Owner", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Child_Embed_Dpi_Failure_Is_Catchable()
    {
        var window = (ReactorWindow)global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ReactorWindow));
        var method = typeof(ReactorWindow).GetMethod("VerifyEmbedDpiAwareness", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!;

        try
        {
            method.Invoke(window, [WindowEmbedStyle.Child]);
        }
        catch (global::System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException inner)
        {
            Assert.Contains("PerMonitorV2", inner.Message, StringComparison.Ordinal);
            return;
        }

        // The current process is already PerMonitorV2; no exception is also valid.
    }

    [Fact]
    public void Validate_Rejects_Embed_With_PersistPlacement()
    {
        var spec = new WindowSpec
        {
            PersistenceId = "preview",
            PersistPlacement = true,
            Embed = new EmbedRequest(WindowEmbedStyle.Owner, 123, InitialVisibility: false),
        };

        var ex = Assert.Throws<ArgumentException>(() => spec.Validate());

        Assert.Contains("PersistPlacement", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Records_Are_Value_Equal_On_Identical_Field_Sets()
    {
        var a = new WindowSpec { Title = "A", Width = 800, Height = 600 };
        var b = new WindowSpec { Title = "A", Width = 800, Height = 600 };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Records_Are_Not_Equal_When_Field_Changes()
    {
        var a = new WindowSpec { Title = "A" };
        var b = new WindowSpec { Title = "B" };
        Assert.NotEqual(a, b);
    }

    // ─── Localization round-trip — non-ASCII / RTL strings (spec 036 §0.3) ───

    [Theory]
    [InlineData("日本語タイトル")]                    // CJK
    [InlineData("نافذة اختبار")]                    // Arabic (RTL)
    [InlineData("שלום עולם")]                      // Hebrew (RTL)
    [InlineData("Здравствуй мир")]                  // Cyrillic
    [InlineData("Réactor — émoji 🎉")]             // Latin + diacritics + emoji
    public void Title_Round_Trips_Non_Ascii(string title)
    {
        var spec = new WindowSpec { Title = title };
        Assert.Equal(title, spec.Title);
        Assert.Equal(spec, new WindowSpec { Title = title });
    }
}

/// <summary>
/// Spec 036 §1.1 / §4.4 — WindowKey identity and conversion semantics.
/// </summary>
public class WindowKeyTests
{
    [Fact]
    public void Of_Rejects_Empty_Name()
    {
        Assert.Throws<ArgumentException>(() => WindowKey.Of(""));
    }

    [Fact]
    public void Of_Rejects_Null_Name()
    {
        Assert.Throws<ArgumentException>(() => WindowKey.Of(null!));
    }

    [Fact]
    public void Equality_Is_Ordinal_On_Name()
    {
        Assert.Equal(WindowKey.Of("settings"), WindowKey.Of("settings"));
        Assert.NotEqual(WindowKey.Of("Settings"), WindowKey.Of("settings"));
    }

    [Fact]
    public void Implicit_String_Conversion()
    {
        WindowKey key = "main";
        Assert.Equal("main", key.Name);
    }

    [Fact]
    public void ToString_Is_Name()
    {
        Assert.Equal("main", WindowKey.Of("main").ToString());
    }
}

/// <summary>
/// Spec 036 §1.1 — WindowIcon factories and validation.
/// </summary>
public class WindowIconTests
{
    [Fact]
    public void FromPath_Rejects_Empty()
    {
        Assert.Throws<ArgumentException>(() => WindowIcon.FromPath(""));
    }

    [Fact]
    public void FromResource_Rejects_Empty()
    {
        Assert.Throws<ArgumentException>(() => WindowIcon.FromResource(""));
    }

    [Fact]
    public void FromPath_Sets_IsResource_False()
    {
        var icon = WindowIcon.FromPath(@"C:\Assets\app.ico");
        Assert.False(icon.IsResource);
        Assert.Equal(@"C:\Assets\app.ico", icon.Source);
    }

    [Fact]
    public void FromResource_Sets_IsResource_True()
    {
        var icon = WindowIcon.FromResource("ms-appx:///Assets/app.ico");
        Assert.True(icon.IsResource);
    }
}
