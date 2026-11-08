using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReactorRegedit.Core;

namespace ReactorRegedit.Native;

/// <summary>
/// Binary editor: offset | hex | ASCII columns. Only the hex column is editable; it is
/// kept in canonical form (pairs, 8 per line) as you type, so offsets and ASCII stay
/// aligned with it line for line.
/// </summary>
internal sealed partial class HexEditorView : Grid
{
    private readonly TextBlock _offsets;
    private readonly TextBox _hex;
    private readonly TextBlock _ascii;
    private bool _reformatting;

    public event Action<byte[]>? BytesChanged;

    public byte[] Bytes { get; private set; } = [];

    public HexEditorView(byte[] initial)
    {
        var mono = new FontFamily("Consolas");
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _offsets = new TextBlock
        {
            FontFamily = mono, FontSize = 13, Padding = new Thickness(0, 4, 10, 4),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            IsTextScaleFactorEnabled = false,
        };
        AutomationProperties.SetAccessibilityView(_offsets, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _hex = new TextBox
        {
            FontFamily = mono, FontSize = 13, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            Width = 8 * 3 * 7.3 + 24, MinHeight = 0, Padding = new Thickness(6, 4, 6, 4),
            IsSpellCheckEnabled = false, IsTextPredictionEnabled = false,
            IsTextScaleFactorEnabled = false,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_hex, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_hex, "Value data (hexadecimal)");
        _ascii = new TextBlock
        {
            FontFamily = mono, FontSize = 13, Padding = new Thickness(10, 4, 0, 4),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            IsTextScaleFactorEnabled = false,
        };
        AutomationProperties.SetName(_ascii, "ASCII");
        SetColumn(_hex, 1);
        SetColumn(_ascii, 2);
        Children.Add(_offsets);
        Children.Add(_hex);
        Children.Add(_ascii);

        Bytes = initial;
        _reformatting = true;
        _hex.Text = HexDump.FormatBytes(initial);
        _reformatting = false;
        UpdateSideColumns();
        _hex.TextChanging += OnTextChanging;
        Loaded += (_, _) => _hex.Focus(FocusState.Programmatic);
    }

    private void OnTextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        if (_reformatting || !args.IsContentChanging) return;
        var raw = sender.Text;
        var digitsBefore = HexDump.DigitsBefore(raw, sender.SelectionStart);
        var digits = HexDump.ExtractDigits(raw, out _);
        var canonical = HexDump.FormatDigits(digits);
        if (canonical != raw)
        {
            _reformatting = true;
            sender.Text = canonical;
            sender.SelectionStart = HexDump.CaretAfterDigits(canonical, digitsBefore);
            _reformatting = false;
        }
        Bytes = HexDump.DigitsToBytes(digits);
        UpdateSideColumns();
        BytesChanged?.Invoke(Bytes);
    }

    private void UpdateSideColumns()
    {
        _offsets.Text = HexDump.Offsets(Bytes.Length);
        _ascii.Text = HexDump.Ascii(Bytes);
    }
}
