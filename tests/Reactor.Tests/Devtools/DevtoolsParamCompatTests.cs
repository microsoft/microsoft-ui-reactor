using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

// Joins the Localization tests' collection: every test here mutates Console.Error,
// and racing those mutations against ValidateCommandTests / PruneCommandTests etc.
// causes one test's stderr to land in another's StringWriter.
[Collection("ConsoleTests")]
public class DevtoolsParamCompatTests
{
    [Fact]
    public void PreviewParamAlone_EmitsDeprecationWarningOncePerProcess()
    {
        ReactorApp.ResetDeprecationWarningForTests();

        var (firstWarning, firstEffective) = CaptureStderr(() => ReactorApp.ResolveDevtoolsParam(false, true));
        Assert.True(firstEffective);
        Assert.Contains("'preview:' is deprecated", firstWarning);

        var (secondWarning, secondEffective) = CaptureStderr(() => ReactorApp.ResolveDevtoolsParam(false, true));
        Assert.True(secondEffective);
        Assert.DoesNotContain("deprecated", secondWarning);
    }

    [Fact]
    public void DevtoolsParam_Suppresses_PreviewDeprecationWarning()
    {
        ReactorApp.ResetDeprecationWarningForTests();

        var (output, effective) = CaptureStderr(() => ReactorApp.ResolveDevtoolsParam(true, true));
        Assert.True(effective);
        Assert.DoesNotContain("deprecated", output);
    }

    [Fact]
    public void DevtoolsFalse_PreviewFalse_ReturnsFalse()
    {
        ReactorApp.ResetDeprecationWarningForTests();
        var (_, effective) = CaptureStderr(() => ReactorApp.ResolveDevtoolsParam(false, false));
        Assert.False(effective);
    }

    [Fact]
    public void DevtoolsTrue_Alone_DoesNotWarn()
    {
        ReactorApp.ResetDeprecationWarningForTests();
        var (output, effective) = CaptureStderr(() => ReactorApp.ResolveDevtoolsParam(true, false));
        Assert.True(effective);
        Assert.DoesNotContain("deprecated", output);
    }

    private static (string Stderr, T Result) CaptureStderr<T>(Func<T> action)
    {
        var origErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var result = action();
            return (sw.ToString(), result);
        }
        finally
        {
            Console.SetError(origErr);
        }
    }
}
