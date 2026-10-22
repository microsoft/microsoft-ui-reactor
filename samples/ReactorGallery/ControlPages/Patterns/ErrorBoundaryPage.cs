using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Patterns;

class ErrorBoundaryPage : Component
{
    // Each demo is its own component, so clicking in one card re-renders only that card.
    // An ErrorBoundary retries its child on every re-render, so a page-level click would
    // otherwise recover every crashed card at once.
    public override Element Render() =>
        ScrollView(VStack(16,
            PageHeader("ErrorBoundary",
                "Catches an exception thrown while a subtree renders and shows a fallback in its place, so one broken component cannot take the page down with it."),

            SampleCard("Catch, explain, and retry",
                Component<RetryDemo>(),
                sourceCode: @"
var (attempt, setAttempt) = UseState(0);

ErrorBoundary(
    // Re-rendering the boundary retries its child. The new key also guarantees
    // a fresh instance, so no state from the failed attempt survives.
    Component<CrashingCounter>().WithKey($""attempt-{attempt}""),
    ex => VStack(8,
        TextBlock(""Something went wrong"").Bold().Foreground(Theme.SystemCritical),
        TextBlock(ex.Message).Foreground(Theme.SecondaryText),
        Button(""Retry"", () => setAttempt(attempt + 1))
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
                Component<StaticFallbackDemo>(),
                sourceCode: @"
var (attempt, setAttempt) = UseState(0);

VStack(8,
    ErrorBoundary(
        Component<CrashingCounter>().WithKey($""attempt-{attempt}""),
        TextBlock(""This widget is unavailable right now."")),
    Button(""Reset"", () => setAttempt(attempt + 1)))
"),

            SampleCard("Without a boundary: RenderErrorHandler",
                Component<UnguardedDemo>(),
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
var (attempt, setAttempt) = UseState(0);

VStack(8,
    // No boundary: the counter stays mounted with its crashing state, and every
    // re-render retries it with that state, so a new key (a fresh instance) recovers it.
    Component<CrashingCounter>().WithKey($""attempt-{attempt}""),
    Button(""Reset"", () => setAttempt(attempt + 1)))
")
        ).Margin(36, 24, 36, 36));
}

class RetryDemo : Component
{
    public override Element Render()
    {
        var (attempt, setAttempt) = UseState(0);
        return ErrorBoundary(
            Component<CrashingCounter>().WithKey($"attempt-{attempt}"),
            ex => VStack(8,
                TextBlock("Something went wrong").Bold().Foreground(Theme.SystemCritical),
                TextBlock(ex.Message).Foreground(Theme.SecondaryText),
                Button("Retry", () => setAttempt(attempt + 1))
            ).Padding(12).Background(Theme.SystemCriticalBackground).CornerRadius(8));
    }
}

class StaticFallbackDemo : Component
{
    public override Element Render()
    {
        var (attempt, setAttempt) = UseState(0);
        return VStack(8,
            ErrorBoundary(
                Component<CrashingCounter>().WithKey($"attempt-{attempt}"),
                TextBlock("This widget is unavailable right now.").Foreground(Theme.SecondaryText)),
            Button("Reset", () => setAttempt(attempt + 1)),
            Caption("Pass an element instead of a function when the fallback does not need the exception.")
                .Foreground(Theme.SecondaryText).TextWrapping());
    }
}

class UnguardedDemo : Component
{
    public override Element Render()
    {
        var (attempt, setAttempt) = UseState(0);
        return VStack(8,
            Component<CrashingCounter>().WithKey($"attempt-{attempt}"),
            Button("Reset", () => setAttempt(attempt + 1)),
            Caption("With no ErrorBoundary, Reactor still contains the failure to the component's own slot, but its built-in fallback shows the full exception text. Ship a RenderErrorHandler so end users see your message instead. Set it app-wide, per window (WindowSpec.RenderErrorHandler), or per host. The gallery leaves the built-in fallback on so you can see it here.")
                .Foreground(Theme.SecondaryText).TextWrapping());
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
