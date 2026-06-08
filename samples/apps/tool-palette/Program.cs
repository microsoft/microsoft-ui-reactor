using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run(_ =>
{
    var main = ReactorApp.OpenWindow(new WindowSpec
    {
        Title = "Tool Palette Main",
        Width = 720,
        Height = 480,
        StartPosition = WindowStartPosition.CenterOnPrimary,
    }, () => new MainWindow());

    ReactorApp.OpenWindow(new WindowSpec
    {
        Title = "Tools",
        Width = 220,
        Height = 320,
        Style = WindowStyle.ToolWindow,
        Level = WindowLevel.Floating,
        CornerStyle = WindowCornerStyle.RoundedSmall,
        Owner = main,
    }, () => new ToolsWindow());
});

internal sealed class MainWindow : Component
{
    public override Element Render() => Border(
        VStack(12,
            TextBlock("Canvas").FontSize(28).Bold(),
            TextBlock("The floating tool palette stays above this owner window.")))
        .Padding(32)
        .Background(SolidBackground);
}

internal sealed class ToolsWindow : Component
{
    public override Element Render() => Border(
        VStack(8,
            TextBlock("Tools").FontSize(18).Bold(),
            Button("Move", null),
            Button("Brush", null),
            Button("Crop", null),
            Button("Text", null)))
        .Padding(16)
        .Background(CardBackground)
        .WithBorder(CardStroke, 1)
        .CornerRadius(8);
}
