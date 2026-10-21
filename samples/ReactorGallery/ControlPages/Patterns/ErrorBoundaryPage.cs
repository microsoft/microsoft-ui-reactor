using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Patterns;

class ErrorBoundaryPage : Component
{
    public override Element Render()
    {
        // Each card owns its own reset key. Bumping a key gives the child a fresh identity,
        // so the next render mounts a new subtree instead of retrying the broken one.
        var (retryKey, setRetryKey) = UseState(0);
        var (staticKey, setStaticKey) = UseState(0);
        var (unguardedKey, setUnguardedKey) = UseState(0);

        return ScrollView(VStack(16,
            PageHeader("ErrorBoundary",
                "Catches an exception thrown while a subtree renders and shows a fallback in its place, so one broken component cannot take the page down with it."),

            SampleCard("Catch, explain, and retry",
                ErrorBoundary(
                    Component<CrashingCounter>().WithKey($"retry-{retryKey}"),
                    ex => VStack(8,
                        TextBlock("Something went wrong").Bold().Foreground(Theme.SystemCritical),
                        TextBlock(ex.Message).Foreground(Theme.SecondaryText),
                        Button("Retry", () => setRetryKey(retryKey + 1))
                    ).Padding(12).Background(Theme.SystemCriticalBackground).CornerRadius(8)),
                sourceCode: @"
var (retryKey, setRetryKey) = UseState(0);

ErrorBoundary(
    // A new key mounts a fresh subtree, dropping the state that caused the crash.
    Component<CrashingCounter>().WithKey($""retry-{retryKey}""),
    ex => VStack(8,
        TextBlock(""Something went wrong"").Bold().Foreground(Theme.SystemCritical),
        TextBlock(ex.Message).Foreground(Theme.SecondaryText),
        Button(""Retry"", () => setRetryKey(retryKey + 1))
    ).Padding(12).Background(Theme.SystemCriticalBackground).CornerRadius(8))

class CrashingCounter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        if (count >= 3)
            throw new InvalidOperationException($""The counter refuses to go past {count - 1}."");
        return Button($""Clicked {count} times"", () => setCount(count + 1));
    }
}
"),

            SampleCard("A static fallback",
                VStack(8,
                    ErrorBoundary(
                        Component<CrashingCounter>().WithKey($"static-{staticKey}"),
                        TextBlock("This widget is unavailable right now.").Foreground(Theme.SecondaryText)),
                    Button("Reset", () => setStaticKey(staticKey + 1)),
                    Caption("Pass an element instead of a function when the fallback does not need the exception.")
                        .Foreground(Theme.SecondaryText).TextWrapping()),
                sourceCode: @"
var (staticKey, setStaticKey) = UseState(0);

VStack(8,
    ErrorBoundary(
        Component<CrashingCounter>().WithKey($""static-{staticKey}""),
        TextBlock(""This widget is unavailable right now."")),
    Button(""Reset"", () => setStaticKey(staticKey + 1)))
"),

            SampleCard("Without a boundary: RenderErrorHandler",
                VStack(8,
                    Component<CrashingCounter>().WithKey($"unguarded-{unguardedKey}"),
                    Button("Reset", () => setUnguardedKey(unguardedKey + 1)),
                    Caption("With no ErrorBoundary, Reactor still contains the failure to the component's own slot, but its built-in fallback shows the full exception text. Ship a RenderErrorHandler so end users see your message instead. Set it app-wide, per window (WindowSpec.RenderErrorHandler), or per host. The gallery leaves the built-in fallback on so you can see it here.")
                        .Foreground(Theme.SecondaryText).TextWrapping()),
                sourceCode: @"
// Program.cs: replace the built-in fallback for every host, before ReactorApp.Run.
ReactorApp.DefaultRenderErrorHandler = error =>
{
    System.Diagnostics.Debug.WriteLine(error.Exception);

    // IsHostLevel: true replaces the whole window content,
    // false fills only the failing component's slot.
    return error.IsHostLevel
        ? TextBlock(""Something went wrong. Please restart the app."")
        : TextBlock($""{error.ComponentName} could not be shown."");
};

// Exceptions an ErrorBoundary catches stay with that boundary and never reach the handler.
VStack(8,
    Component<CrashingCounter>().WithKey($""unguarded-{unguardedKey}""),
    Button(""Reset"", () => setUnguardedKey(unguardedKey + 1)))
")
        ).Margin(36, 24, 36, 36));
    }
}

/// <summary>Renders normally until its third click, then throws from <see cref="Render"/>.</summary>
class CrashingCounter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        if (count >= 3)
            throw new InvalidOperationException($"The counter refuses to go past {count - 1}.");
        return HStack(8,
            Button($"Clicked {count} times", () => setCount(count + 1)),
            Caption("Throws on the third click.").Foreground(Theme.SecondaryText).VAlign(VerticalAlignment.Center));
    }
}
