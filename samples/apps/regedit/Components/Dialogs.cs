using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactorRegedit.Core;
using ReactorRegedit.Native;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components;

/// <summary>
/// The app's modal dialogs. Each is a component that owns its edit state (so typing
/// doesn't re-render the shell) and reports through callbacks; the shell mounts one
/// at a time, keyed by a fresh id.
/// </summary>
internal static class DialogChrome
{
    public static ContentDialogElement Dialog(string title, Element content, string primary, string? secondary,
        Action onPrimary, Action onDismiss, bool primaryEnabled = true)
    {
        return ContentDialog(title, content, primary) with
        {
            IsOpen = true,
            SecondaryButtonText = secondary,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = primaryEnabled,
            OnClosed = result =>
            {
                onDismiss();
                if (result == ContentDialogResult.Primary) onPrimary();
            },
        };
    }

    public static Element Label(string text) => TextBlock(text).FontSize(13);

    public static Element ReadOnlyName(string name) =>
        TextBox(name, _ => { }).IsReadOnly().AutomationName(Strings.ValueName);
}

// ── Confirm ──────────────────────────────────────────────────────────

internal sealed record ConfirmProps(string Title, string Message, Action OnYes, Action OnClose,
    string YesText = "Yes", string NoText = "No");

internal sealed class ConfirmDialog : Component<ConfirmProps>
{
    public override Element Render() =>
        DialogChrome.Dialog(Props.Title,
            TextBlock(Props.Message).TextWrapping(TextWrapping.Wrap).Width(380),
            Props.YesText, Props.NoText, Props.OnYes, Props.OnClose);
}

// ── Edit String / Expandable String ──────────────────────────────────

internal sealed record EditStringProps(string Name, string Data, bool Expandable, Action<string> OnOk, Action OnClose);

internal sealed class EditStringDialog : Component<EditStringProps>
{
    public override Element Render()
    {
        var (text, setText) = UseState(Props.Data);
        return DialogChrome.Dialog(
            Props.Expandable ? Strings.EditExpandStringTitle : Strings.EditStringTitle,
            VStack(4,
                DialogChrome.Label(Strings.ValueName),
                DialogChrome.ReadOnlyName(Props.Name),
                DialogChrome.Label(Strings.ValueData).Margin(0, 6, 0, 0),
                TextBox(text, setText).AutomationName(Strings.ValueData)
            ).Width(420),
            Strings.OK, Strings.Cancel, () => Props.OnOk(text), Props.OnClose);
    }
}

// ── Edit Multi-String ────────────────────────────────────────────────

internal sealed record EditMultiStringProps(string Name, string[] Lines, Action<string> OnOk, Action OnClose);

internal sealed class EditMultiStringDialog : Component<EditMultiStringProps>
{
    public override Element Render()
    {
        var (text, setText) = UseState(string.Join("\r", Props.Lines));
        return DialogChrome.Dialog(Strings.EditMultiStringTitle,
            VStack(4,
                DialogChrome.Label(Strings.ValueName),
                DialogChrome.ReadOnlyName(Props.Name),
                DialogChrome.Label(Strings.ValueData).Margin(0, 6, 0, 0),
                TextBox(text, setText)
                    .AcceptsReturn()
                    .TextWrapping(TextWrapping.NoWrap)
                    .Height(220)
                    .AutomationName(Strings.ValueData)
            ).Width(420),
            Strings.OK, Strings.Cancel, () => Props.OnOk(text), Props.OnClose);
    }
}

// ── Edit DWORD / QWORD ───────────────────────────────────────────────

internal sealed record EditNumberProps(string Name, bool IsQword, ulong Value, Action<ulong> OnOk, Action OnClose);

internal sealed class EditNumberDialog : Component<EditNumberProps>
{
    public override Element Render()
    {
        var (hex, setHex) = UseState(true);
        var (text, setText) = UseState(Props.IsQword ? Props.Value.ToString("x") : ((uint)Props.Value).ToString("x"));

        bool valid;
        ulong parsed;
        if (Props.IsQword) valid = ValueFormat.TryParseQword(text, hex, out parsed);
        else { valid = ValueFormat.TryParseDword(text, hex, out var d); parsed = d; }

        void SwitchBase(bool toHex)
        {
            if (toHex == hex) return;
            if (valid) setText(toHex ? parsed.ToString("x") : parsed.ToString());
            setHex(toHex);
        }

        return DialogChrome.Dialog(Props.IsQword ? Strings.EditQwordTitle : Strings.EditDwordTitle,
            VStack(4,
                DialogChrome.Label(Strings.ValueName),
                DialogChrome.ReadOnlyName(Props.Name),
                HStack(16,
                    VStack(4,
                        DialogChrome.Label(Strings.ValueData),
                        TextBox(text, setText).Width(200).AutomationName(Strings.ValueData),
                        When(!valid, () => TextBlock(hex ? Strings.InvalidHex : Strings.InvalidDecimal)
                            .FontSize(12).Foreground(Theme.SystemCritical))
                    ),
                    VStack(2,
                        DialogChrome.Label(Strings.Base),
                        RadioButton(Strings.Hexadecimal, hex, on => { if (on) SwitchBase(true); }, "numberBase"),
                        RadioButton(Strings.Decimal, !hex, on => { if (on) SwitchBase(false); }, "numberBase")
                    )
                ).Margin(0, 6, 0, 0)
            ).Width(420),
            Strings.OK, Strings.Cancel, () => { if (valid) Props.OnOk(parsed); }, Props.OnClose, primaryEnabled: valid);
    }
}

// ── Edit Binary ──────────────────────────────────────────────────────

internal sealed record EditBinaryProps(string Name, string TypeName, byte[] Data, Action<byte[]> OnOk, Action OnClose);

internal sealed class EditBinaryDialog : Component<EditBinaryProps>
{
    public override Element Render()
    {
        var bytesRef = UseRef(Props.Data);
        return DialogChrome.Dialog(Strings.EditBinaryTitle,
            VStack(4,
                DialogChrome.Label(Strings.ValueName),
                DialogChrome.ReadOnlyName(Props.Name),
                DialogChrome.Label($"{Strings.ValueData} ({Props.TypeName})").Margin(0, 6, 0, 0),
                ScrollView(
                    XamlHost(() =>
                    {
                        var editor = new HexEditorView(Props.Data);
                        editor.BytesChanged += b => bytesRef.Current = b;
                        return editor;
                    }, typeKey: "hex-editor")
                ).Height(260)
            ).Width(460),
            Strings.OK, Strings.Cancel, () => Props.OnOk(bytesRef.Current), Props.OnClose);
    }
}

// ── About ────────────────────────────────────────────────────────────

internal sealed record MessageProps(string Title, string Message, Action OnClose);

internal sealed class MessageDialog : Component<MessageProps>
{
    public override Element Render() =>
        DialogChrome.Dialog(Props.Title,
            TextBlock(Props.Message).TextWrapping(TextWrapping.Wrap).Width(380),
            Strings.OK, null, () => { }, Props.OnClose);
}
