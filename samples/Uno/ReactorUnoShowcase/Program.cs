// Secondary windows are real on every Uno *desktop* head (X11 / Win32 / macOS /
// FrameBuffer) but throw InvalidOperationException on Android and iOS, and there
// are no OS windows in the browser at all. So the multi-window demo is gated on
// "desktop", not merely "not wasm".
#if !__WASM__ && !__ANDROID__ && !__IOS__ && !__MACCATALYST__
#define REACTOR_DESKTOP
#endif

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Desktop and iOS share ReactorApp.Run; wasm needs the async entry (the browser
// thread can't block); Android has no console entry point and starts from the
// Activity in Platforms/Android/.
#if __ANDROID__
// intentionally empty — see Platforms/Android/
#elif __WASM__
await ReactorApp.RunAsync<Showcase>("Reactor Showcase (Uno)", width: 560, height: 620);
#else
ReactorApp.Run<Showcase>("Reactor Showcase (Uno)", width: 560, height: 620);
#endif

class Showcase : Component
{
    static readonly string[] Fruits = { "Donuts", "Apples", "Bananas" };

    public override Element Render()
    {
        var (on, setOn) = UseState(true);
        var (slider, setSlider) = UseState(40.0);
        var (chk, setChk) = UseState<bool?>(true);
        var (combo, setCombo) = UseState(0);
        var (count, setCount) = UseState(0);
        var (picked, setPicked) = UseState("(none)");

        return ScrollView(
            VStack(14,
                Heading("Reactor on Uno — Control Showcase"),

                TextBlock($"Counter: {count}"),
                HStack(8,
                    Button("-", () => setCount(count - 1)),
                    Button("+", () => setCount(count + 1))),

                ToggleSwitch(on, setOn, header: "Feature toggle"),
                TextBlock(on ? "Feature is ON" : "Feature is OFF"),

                TextBlock($"Slider value: {slider:0}"),
                Slider(slider, 0, 100, setSlider),
                Progress(slider),

                CheckBox(chk, b => setChk(b), label: "I agree"),
                TextBlock(chk == true ? "Checked" : "Unchecked"),

                TextBlock($"Favourite: {Fruits[combo]}"),
                ComboBox(Fruits, combo, setCombo),

                // Uno implements the WinRT pickers, including the
                // WindowNative.GetWindowHandle + InitializeWithWindow association
                // the shared hook performs — the very pattern Uno's own docs
                // prescribe. Nothing Windows-only here.
                Heading("File picker"),
                TextBlock($"Picked: {picked}"),
                Button("Pick a file…", async () =>
                {
                    var file = await UseFilePickerAsync(new FilePickerOptions());
                    setPicked(file?.Name ?? "(cancelled)");
                })
#if REACTOR_DESKTOP
                ,
                Heading("Multi-window"),
                TextBlock("Each window gets its own Reactor host, render loop, and state."),
                Button("Open a second window", OpenSecondWindow)
#endif
            ).Padding(24)
        );
    }

#if REACTOR_DESKTOP
    // Secondary windows are real on every Uno desktop head (X11 / Win32 / macOS /
    // FrameBuffer). Android and iOS throw InvalidOperationException instead.
    static void OpenSecondWindow() =>
        ReactorApp.OpenWindow(
            new WindowSpec { Title = "Second Window", Width = 380, Height = 260 },
            static () => new SecondWindow());
#endif
}

#if REACTOR_DESKTOP
class SecondWindow : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (locked, setLocked) = UseState<bool?>(false);

        // A real closing guard, backed by Uno's AppWindow.Closing on the desktop
        // heads: tick the box and the window refuses to close.
        UseClosingGuard(() => locked != true);

        return VStack(12,
            Heading("Second window 🎉"),
            TextBlock("Independent of the main window:"),
            TextBlock($"Count: {count}"),
            HStack(8,
                Button("-", () => setCount(count - 1)),
                Button("+", () => setCount(count + 1))
            ),
            CheckBox(locked, b => setLocked(b), label: "Prevent closing"),
            TextBlock(locked == true
                ? "Close is blocked — untick to close."
                : "Close is allowed.")
        ).Padding(24);
    }
}
#endif
