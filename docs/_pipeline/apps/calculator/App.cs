using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<CalculatorApp>("Calculator", width: 380, height: 500
#if DEBUG
    , preview: true
#endif
);

// <snippet:calculator-app>
class CalculatorApp : Component
{
    public override Element Render()
    {
        var (display, setDisplay) = UseState("0");
        var (operand, setOperand) = UseState<double?>(null);
        var (op, setOp) = UseState<string?>(null);
        var (resetNext, setResetNext) = UseState(false);

        void PressDigit(string digit)
        {
            if (resetNext || display == "0")
            {
                setDisplay(digit);
                setResetNext(false);
            }
            else
            {
                setDisplay(display + digit);
            }
        }

        void PressOp(string nextOp)
        {
            var current = double.Parse(display);
            if (operand.HasValue && op != null)
            {
                var result = Calculate(operand.Value, current, op);
                setDisplay(FormatResult(result));
                setOperand(result);
            }
            else
            {
                setOperand(current);
            }
            setOp(nextOp);
            setResetNext(true);
        }

        void PressEquals()
        {
            if (operand.HasValue && op != null)
            {
                var current = double.Parse(display);
                var result = Calculate(operand.Value, current, op);
                setDisplay(FormatResult(result));
                setOperand(null);
                setOp(null);
                setResetNext(true);
            }
        }

        void PressClear()
        {
            setDisplay("0");
            setOperand(null);
            setOp(null);
            setResetNext(false);
        }

        Element NumButton(string digit) =>
            Button(digit, () => PressDigit(digit))
                .Width(60).Height(48);

        Element OpButton(string label, string opCode) =>
            Button(label, () => PressOp(opCode))
                .Width(60).Height(48);

        return VStack(4,
            // Display
            TextBlock(display)
                .FontSize(32).Bold()
                .HAlign(HorizontalAlignment.Right)
                .Padding(horizontal: 12, vertical: 8),

            // Button grid
            HStack(4, Button("C", PressClear).Width(60).Height(48),
                       NumButton("7"), NumButton("8"), NumButton("9")),
            HStack(4, OpButton("/", "/"),
                       NumButton("4"), NumButton("5"), NumButton("6")),
            HStack(4, OpButton("*", "*"),
                       NumButton("1"), NumButton("2"), NumButton("3")),
            HStack(4, OpButton("-", "-"),
                       NumButton("0"), OpButton("+", "+"),
                       Button("=", PressEquals).Width(60).Height(48))
        ).Padding(16);
    }

    static double Calculate(double a, double b, string op) => op switch
    {
        "+" => a + b,
        "-" => a - b,
        "*" => a * b,
        "/" => b != 0 ? a / b : 0,
        _ => b,
    };

    static string FormatResult(double value) =>
        value == Math.Floor(value) ? $"{value:F0}" : $"{value:G10}";
}
// </snippet:calculator-app>
