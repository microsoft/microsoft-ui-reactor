// id: use-ref-dom
// intent: hand a mounted element reference to native APIs for focus and measurement
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

// Pair a stable ref with a mount effect when native APIs need the real control instance.
ReactorApp.Run<App>("UseRefDom", width: 400, height: 200);

class App : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("");
        var (message, setMessage) = UseState("Waiting for mount...");
        var focusAttempts = UseRef(0);
        var inputRef = this.UseElementRef<TextBox>();

        UseEffect(() =>
        {
            focusAttempts.Current++;
            if (inputRef.Current is { } box)
            {
                box.Focus(FocusState.Programmatic);
                setMessage($"Focus requested. ActualWidth = {box.ActualWidth:F0}px");
            }
        }, Array.Empty<object>());

        return VStack(12,
            TextBox(text, setText, "Focused on mount", header: "Focusable input").Width(240).Ref(inputRef),
            Caption(message),
            Caption($"Native ref uses: {focusAttempts.Current}"));
    }
}

