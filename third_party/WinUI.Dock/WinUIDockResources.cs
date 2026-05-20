namespace WinUI.Dock;

public partial class WinUIDockResources : ResourceDictionary
{
    public WinUIDockResources()
    {
        MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("ms-appx:///WinUI.Dock/Themes/Styles.xaml", UriKind.Absolute)
        });

        MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("ms-appx:///WinUI.Dock/Themes/Themes.xaml", UriKind.Absolute)
        });
    }
}
