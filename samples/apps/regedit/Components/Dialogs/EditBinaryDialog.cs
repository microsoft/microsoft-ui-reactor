using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components.Dialogs;

internal sealed record EditBinaryDialogProps(
    bool IsOpen,
    string ValueName,
    string ValueData,
    Action<string> OnValueDataChanged,
    Action OnSave,
    Action OnCancel
);

internal sealed class EditBinaryDialog : Component<EditBinaryDialogProps>
{
    public override Element Render()
    {
        return ContentDialog(
            Strings.EditBinaryTitle,
            VStack(12,
                VStack(4,
                    TextBlock(Strings.ValueName),
                    TextBox(Props.ValueName, _ => { })
                        .IsReadOnly()
                ),
                VStack(4,
                    TextBlock(Strings.ValueData),
                    TextBox(Props.ValueData, Props.OnValueDataChanged)
                        .Set(tb =>
                        {
                            tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
                            tb.AcceptsReturn = true;
                            tb.TextWrapping = TextWrapping.Wrap;
                        })
                        .Height(200)
                )
            ).Width(500),
            Strings.OK
        ) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            OnClosed = _ => Props.OnCancel(),
        };
    }

    /// <summary>
    /// Formats a byte array as a hex dump string suitable for display in the editor.
    /// Format: "0000  00 01 02 03 04 05 06 07  ........"
    /// </summary>
    public static string FormatHexDump(byte[] data)
    {
        if (data.Length == 0) return "";

        var lines = new List<string>();
        for (int offset = 0; offset < data.Length; offset += 8)
        {
            var lineBytes = data.Skip(offset).Take(8).ToArray();
            var hex = string.Join(" ", lineBytes.Select(b => b.ToString("X2")));
            var ascii = new string(lineBytes.Select(b => b >= 0x20 && b < 0x7F ? (char)b : '.').ToArray());
            lines.Add($"{offset:X4}  {hex,-23}  {ascii}");
        }
        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// Parses a hex dump string back into a byte array.
    /// Extracts only the hex bytes portion, ignoring offset and ASCII columns.
    /// </summary>
    public static byte[] ParseHexDump(string hexDump)
    {
        if (string.IsNullOrWhiteSpace(hexDump)) return [];

        var bytes = new List<byte>();
        foreach (var line in hexDump.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 6) continue;

            // Skip offset (first 4 chars + 2 spaces)
            var hexPart = trimmed.Length > 6 ? trimmed[6..] : "";
            // Take only the hex portion (before the double space + ASCII)
            var doubleSpaceIdx = hexPart.IndexOf("  ");
            if (doubleSpaceIdx >= 0)
                hexPart = hexPart[..doubleSpaceIdx];

            foreach (var token in hexPart.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (byte.TryParse(token, global::System.Globalization.NumberStyles.HexNumber, null, out var b))
                    bytes.Add(b);
            }
        }
        return bytes.ToArray();
    }
}
