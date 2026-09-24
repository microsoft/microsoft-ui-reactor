using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

class KeyboardInputPage : Component
{
    public override Element Render()
    {
        var (draft, setDraft) = UseState("");
        var (submitted, setSubmitted) = UseState("Nothing submitted yet.");

        var (keyLog, setKeyLog) = UseState("Give the box focus and press a key.");

        var (typed, setTyped) = UseState("Nothing typed yet.");

        var (saves, setSaves) = UseState(0);
        var save = new Command
        {
            Label = "Save",
            Execute = () => setSaves(saves + 1),
            Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control),
        };

        return ScrollView(VStack(16,
            PageHeader("Keyboard input",
                "Attach .OnKeyDown / .OnKeyUp to any element. e.Key is Windows.System.VirtualKey — the WinRT enum, whose member names differ from WPF's System.Windows.Input.Key."),

            SampleCard("Enter submits",
                VStack(8,
                    TextBox(draft, setDraft, placeholderText: "Type, then press Enter")
                        .AutomationName("Draft")
                        .Width(320)
                        .OnKeyDown((_, e) =>
                        {
                            if (e.Key != VirtualKey.Enter) return;
                            setSubmitted($"Submitted: {draft}");
                            e.Handled = true;
                        }),
                    TextBlock(submitted).Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (draft, setDraft) = UseState("""");

TextBox(draft, setDraft, placeholderText: ""Type, then press Enter"")
    .OnKeyDown((_, e) =>
    {
        if (e.Key != VirtualKey.Enter) return;
        setSubmitted($""Submitted: {draft}"");
        e.Handled = true;   // stop the key bubbling further
    })
"),

            SampleCard("VirtualKey has no OEM members",
                VStack(8,
                    TextBox(placeholderText: "Press any key here")
                        .AutomationName("Key probe")
                        .Width(320)
                        .OnKeyDown((_, e) => setKeyLog($"e.Key = VirtualKey.{e.Key} — numeric code {(int)e.Key}")),
                    TextBlock(keyLog).Foreground(Theme.SecondaryText),
                    Caption("VirtualKey is WinRT, not WPF. There is no OemPeriod, OemPlus, OemMinus or Equal — punctuation keys simply have no named member, so compare the numeric code instead.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// Named members you can rely on: letters (VirtualKey.A), digits (Number0, NumberPad0),
// numpad operators (Add, Subtract, Multiply, Divide, Decimal), and the editing and
// navigation keys (Enter, Space, Back, Escape, Tab, Delete, Left, Right, Up, Down).
var (keyLog, setKeyLog) = UseState(""Give the box focus and press a key."");

TextBox(placeholderText: ""Press any key here"")
    .OnKeyDown((_, e) =>
    {
        // VK_OEM_PERIOD '.' and VK_OEM_PLUS '=' have no named VirtualKey member.
        if ((int)e.Key == 190) setKeyLog(""decimal point"");
        if ((int)e.Key == 187) setKeyLog(""equals"");
    })
"),

            SampleCard("OnCharacterReceived resolves the character for you",
                VStack(8,
                    TextBox(placeholderText: "Type . or = here")
                        .AutomationName("Character probe")
                        .Width(320)
                        .OnCharacterReceived((_, e) => setTyped($"Character received: '{e.Character}'")),
                    TextBlock(typed).Foreground(Theme.SecondaryText),
                    Caption("For text and punctuation this is the better hook: WinUI has already applied the keyboard layout, so you never map a scan code by hand.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (typed, setTyped) = UseState(""Nothing typed yet."");

TextBox(placeholderText: ""Type . or = here"")
    .OnCharacterReceived((_, e) => setTyped($""Character received: '{e.Character}'""))
"),

            SampleCard("App-wide chords belong on a Command",
                VStack(8,
                    CommandHost([save],
                        VStack(8,
                            Button(save),
                            TextBlock($"Saved {saves} time(s) — press Ctrl+S with focus anywhere in this card.")
                                .Foreground(Theme.SecondaryText))),
                    Caption("A .OnKeyDown handler only fires while its element has focus, so a Ctrl/Alt chord written that way silently does nothing elsewhere. REACTOR_INPUT_001 flags it and points here.")
                        .Foreground(Theme.SecondaryText),
                    Caption("Accelerators follow WinUI's scoping rule: they fire when the surface carrying them is in the focused element's ancestor chain. CommandHost widens that to a whole subtree. For a truly window-wide chord, render the command from a MenuBar or CommandBar at the window root.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var save = new Command
{
    Label = ""Save"",
    Execute = () => setSaves(saves + 1),
    Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control),
};

// Button(save) alone scopes the chord to the button's ancestor chain.
// CommandHost widens it to this subtree; MenuBar/CommandBar at the window
// root is what makes an accelerator genuinely window-wide.
CommandHost([save],
    VStack(8,
        Button(save),
        TextBlock($""Saved {saves} time(s)"")))
")
        ).Margin(36, 24, 36, 36));
    }
}
