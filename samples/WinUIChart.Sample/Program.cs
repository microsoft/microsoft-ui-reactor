// Reactor.Advanced Chart sample — the WinUI Microsoft.UI.Xaml.Controls.Charts.Chart (Windows App
// SDK experimental channel) driven from a Reactor component: area, column and line series on
// shared axes with a legend and per-series visibility, and a date-time series that grows as
// state changes.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Advanced.Charts;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls.Charts;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

ReactorApp.Run<ChartDemo>("Reactor Chart", width: 960, height: 760);

class ChartDemo : Component
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun"];
    private static readonly double[] Actual = [12, 23, 37, 31, 46, 52];
    private static readonly double[] Forecast = [16, 25, 35, 40, 48, 55];
    private static readonly double[] Target = [20, 25, 40, 40, 50, 50];
    private static readonly double[] InitialReadings = [72, 76, 74, 79, 77];
    private static readonly DateTimeOffset Start = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    public override Element Render()
    {
        var (showForecast, setShowForecast) = UseState(true);
        var (showTarget, setShowTarget) = UseState(true);
        var (readings, setReadings) = UseState(InitialReadings);

        // Equal axis records are shared by every series that uses them.
        var revenue = LinearAxis("Revenue ($k)", minimum: 0, maximum: 60).GridLines(GridLines.Major);

        var results = Chart(
                AreaSeries(Months, Forecast, "Forecast", yAxis: revenue).Visible(showForecast),
                BarSeries(Months, Actual, "Actual", yAxis: revenue).Vertical().DataLabels(),
                LineSeries(Months, Target, "Target", yAxis: revenue).Markers(MarkerShape.Diamond).Visible(showTarget))
            .Legend("Monthly results");

        // A fresh array is all it takes to redraw; the native Samples object is reused.
        var days = readings.Select((_, i) => Start.AddDays(7 * i)).ToArray();
        var temperature = Chart(
            LineSeries(days, readings, "Temperature",
                    xAxis: DateTimeAxis("Week") with { IntervalType = DateTimeIntervalType.Week, LabelFormat = "month day" },
                    yAxis: LinearAxis("°F").GridLines(GridLines.Major))
                .Markers());

        return ScrollView(VStack(12,
            Heading("Chart"),
            SubHeading("Area, column and line series on shared axes"),
            HStack(16,
                ToggleSwitch(showForecast, setShowForecast, header: "Forecast"),
                ToggleSwitch(showTarget, setShowTarget, header: "Target")),
            results.Height(320),
            SubHeading("A live time series"),
            Button("Add reading", () => setReadings([.. readings, 70 + Random.Shared.Next(15)])),
            temperature.Height(260)
        ).Padding(24));
    }
}
