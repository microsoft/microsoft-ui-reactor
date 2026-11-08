using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactorRegedit.Core;
using ReactorRegedit.Models;
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
        TextBox(name, _ => { }).IsReadOnly().IsTabStop(false).AutomationName(Strings.ValueName);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, object> s_selected = new();

    /// <summary>
    /// Classic regedit opens editors with the data selected so typing replaces it. The read-only
    /// name box isn't a tab stop, so the dialog's initial focus lands here; select all on that
    /// first focus only.
    /// </summary>
    public static TextBoxElement SelectAllOnFirstFocus(this TextBoxElement element) =>
        element.OnGotFocus((s, args) =>
        {
            if (s is not TextBox tb || s_selected.TryGetValue(tb, out _)) return;
            s_selected.Add(tb, new object());
            tb.SelectAll();
        });
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
                TextBox(text, setText).SelectAllOnFirstFocus().AutomationName(Strings.ValueData)
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
                TextBox(text, setText).SelectAllOnFirstFocus()
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
        // Text that was invalid when the base was switched stays invalid until edited, so an
        // out-of-range hex string can't silently turn into a different, valid decimal number.
        var (carriedInvalid, setCarriedInvalid) = UseState(false);

        bool valid;
        ulong parsed;
        if (Props.IsQword) valid = ValueFormat.TryParseQword(text, hex, out parsed);
        else { valid = ValueFormat.TryParseDword(text, hex, out var d); parsed = d; }
        valid &= !carriedInvalid;

        void SwitchBase(bool toHex)
        {
            if (toHex == hex) return;
            if (valid) setText(toHex ? parsed.ToString("x") : parsed.ToString());
            else setCarriedInvalid(true);
            setHex(toHex);
        }

        // Classic regedit caps the field at the widest valid value for the base.
        var maxLength = Props.IsQword ? (hex ? 16 : 20) : (hex ? 8 : 10);

        return DialogChrome.Dialog(Props.IsQword ? Strings.EditQwordTitle : Strings.EditDwordTitle,
            VStack(4,
                DialogChrome.Label(Strings.ValueName),
                DialogChrome.ReadOnlyName(Props.Name),
                HStack(16,
                    VStack(4,
                        DialogChrome.Label(Strings.ValueData),
                        TextBox(text, t => { setCarriedInvalid(false); setText(t); }).MaxLength(maxLength)
                            .SelectAllOnFirstFocus().Width(200).AutomationName(Strings.ValueData),
                        When(!valid, () => TextBlock(hex ? Strings.InvalidHex : Strings.InvalidDecimal)
                            .FontSize(12).Foreground(Theme.SystemCritical))
                    ),
                    VStack(2,
                        DialogChrome.Label(Strings.Base),
                        RadioButton(Strings.Hexadecimal, hex, on => { if (on) SwitchBase(true); }, "numberBase").Set(rb => rb.Style = ReactorRegedit.Native.AppTheme.CompactRadioButtonStyle),
                        RadioButton(Strings.Decimal, !hex, on => { if (on) SwitchBase(false); }, "numberBase").Set(rb => rb.Style = ReactorRegedit.Native.AppTheme.CompactRadioButtonStyle)
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

// ── Find ─────────────────────────────────────────────────────────────

internal sealed record FindProps(string Text, FindFlags Flags, Action<string, FindFlags> OnFind, Action OnClose);

internal sealed class FindDialog : Component<FindProps>
{
    public override Element Render()
    {
        var (text, setText) = UseState(Props.Text);
        var (flags, setFlags) = UseState(Props.Flags);

        Element Flag(string label, FindFlags flag) =>
            CheckBox(flags.HasFlag(flag), on => setFlags(on ? flags | flag : flags & ~flag), label).MinHeight(24).Padding(8, 2, 0, 0);

        var lookAtAny = (flags & (FindFlags.Keys | FindFlags.Values | FindFlags.Data)) != 0;
        return DialogChrome.Dialog(Strings.FindTitle,
            VStack(4,
                DialogChrome.Label(Strings.FindWhat),
                TextBox(text, setText).SelectAllOnFirstFocus().AutomationName(Strings.FindWhat),
                DialogChrome.Label(Strings.LookAt).Margin(0, 8, 0, 0),
                HStack(12,
                    Flag(Strings.FindKeys, FindFlags.Keys),
                    Flag(Strings.FindValues, FindFlags.Values),
                    Flag(Strings.FindData, FindFlags.Data)),
                Flag(Strings.MatchWholeStringOnly, FindFlags.WholeStringOnly)
            ).Width(380),
            Strings.FindNext, Strings.Cancel, () => Props.OnFind(text, flags), Props.OnClose,
            primaryEnabled: text.Length > 0 && lookAtAny);
    }
}

// ── Export range ─────────────────────────────────────────────────────

internal sealed record ExportProps(string SelectedBranch, Action<bool, string> OnExport, Action OnClose);

internal sealed class ExportDialog : Component<ExportProps>
{
    public override Element Render()
    {
        var hasBranch = Props.SelectedBranch.Length > 0;
        var (all, setAll) = UseState(!hasBranch);
        var (branch, setBranch) = UseState(Props.SelectedBranch);
        return DialogChrome.Dialog(Strings.ExportTitle,
            VStack(4,
                DialogChrome.Label(Strings.ExportRange),
                RadioButton(Strings.All, all, on => { if (on) setAll(true); }, "exportRange").Set(rb => rb.Style = ReactorRegedit.Native.AppTheme.CompactRadioButtonStyle),
                RadioButton(Strings.SelectedBranch, !all, on => { if (on) setAll(false); }, "exportRange").Set(rb => rb.Style = ReactorRegedit.Native.AppTheme.CompactRadioButtonStyle),
                TextBox(branch, setBranch).IsEnabled(!all)
                    .AutomationName(Strings.SelectedBranch)
            ).Width(420),
            Strings.Export.TrimEnd('.'), Strings.Cancel, () => Props.OnExport(all, branch), Props.OnClose,
            primaryEnabled: all || branch.Trim().Length > 0);
    }
}

// ── Favorites ────────────────────────────────────────────────────────

internal sealed record AddFavoriteProps(string Name, Action<string> OnOk, Action OnClose);

internal sealed class AddFavoriteDialog : Component<AddFavoriteProps>
{
    public override Element Render()
    {
        var (name, setName) = UseState(Props.Name);
        return DialogChrome.Dialog(Strings.AddFavoriteTitle,
            VStack(4,
                DialogChrome.Label(Strings.FavoriteName),
                TextBox(name, setName).AutomationName(Strings.FavoriteName)
            ).Width(360),
            Strings.OK, Strings.Cancel, () => Props.OnOk(name.Trim()), Props.OnClose,
            primaryEnabled: name.Trim().Length > 0);
    }
}

internal sealed record RemoveFavoriteProps(string[] Names, Action<string> OnRemove, Action OnClose);

internal sealed class RemoveFavoriteDialog : Component<RemoveFavoriteProps>
{
    public override Element Render()
    {
        var (index, setIndex) = UseState(Props.Names.Length > 0 ? 0 : -1);
        return DialogChrome.Dialog(Strings.RemoveFavoriteTitle,
            VStack(4,
                DialogChrome.Label(Strings.SelectFavorite),
                Props.Names.Length == 0
                    ? TextBlock("There are no favorites.").FontSize(13)
                    : ListBox(Props.Names, index, setIndex).Height(180).AutomationName(Strings.SelectFavorite)
                        .Set(lb => lb.ItemContainerStyle = ReactorRegedit.Native.ListHelpers.CompactListBoxItemStyle())
            ).Width(360),
            Strings.OK, Strings.Cancel,
            () => { if (index >= 0 && index < Props.Names.Length) Props.OnRemove(Props.Names[index]); },
            Props.OnClose, primaryEnabled: index >= 0);
    }
}

// ── About ────────────────────────────────────────────────────────────

internal sealed record MessageProps(string Title, string Message, Action OnClose);

// ── Prompt (Load Hive key name) ──────────────────────────────────────

internal sealed record PromptProps(string Title, string Label, string Initial, Func<string, string?> Validate,
    Action<string> OnOk, Action OnClose);

internal sealed class PromptDialog : Component<PromptProps>
{
    public override Element Render()
    {
        var (text, setText) = UseState(Props.Initial);
        var error = text.Length == 0 ? null : Props.Validate(text);
        return DialogChrome.Dialog(Props.Title,
            VStack(4,
                DialogChrome.Label(Props.Label),
                TextBox(text, setText).SelectAllOnFirstFocus().AutomationName(Props.Label.TrimEnd(':')),
                When(error is not null, () => TextBlock(error!).FontSize(12).Foreground(Theme.SystemCritical))
            ).Width(360),
            Strings.OK, Strings.Cancel, () => Props.OnOk(text.Trim()), Props.OnClose,
            primaryEnabled: text.Trim().Length > 0 && error is null);
    }
}

internal sealed class MessageDialog : Component<MessageProps>
{
    public override Element Render() =>
        DialogChrome.Dialog(Props.Title,
            TextBlock(Props.Message).TextWrapping(TextWrapping.Wrap).Width(380),
            Strings.OK, null, () => { }, Props.OnClose);
}
