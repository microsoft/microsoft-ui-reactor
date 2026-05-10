using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace StressPerf.XBind;

// Optimal INPC for compiled bindings:
//  - Cached PropertyChangedEventArgs avoids one allocation per change × 4900
//    cells × ~30 ticks/s.
//  - Get-only properties (x:Bind OneWay only reads from the source).
//  - Derived state (DisplayText, PriceBrush) is pre-baked so the template has
//    no converters and the binding pipeline does only direct property reads.
public sealed class StockItemViewModel : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs DisplayTextArgs = new(nameof(DisplayText));
    private static readonly PropertyChangedEventArgs PriceBrushArgs = new(nameof(PriceBrush));

    private static readonly SolidColorBrush GreenBrush = new(Colors.LimeGreen);
    private static readonly SolidColorBrush RedBrush = new(Microsoft.UI.Colors.Red);

    private string _displayText = "";
    private SolidColorBrush _priceBrush = GreenBrush;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayText => _displayText;
    public SolidColorBrush PriceBrush => _priceBrush;

    public void Update(string symbol, double price, bool isUp)
    {
        var newText = $"{symbol} {price:F2}";
        if (_displayText != newText)
        {
            _displayText = newText;
            PropertyChanged?.Invoke(this, DisplayTextArgs);
        }
        var newBrush = isUp ? GreenBrush : RedBrush;
        if (!ReferenceEquals(_priceBrush, newBrush))
        {
            _priceBrush = newBrush;
            PropertyChanged?.Invoke(this, PriceBrushArgs);
        }
    }
}
