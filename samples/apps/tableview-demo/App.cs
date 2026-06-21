using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories;
using Reactor.Controls;

namespace TableViewDemo;

public sealed record Person(string Name, int Age, string City);

public sealed class App : Component
{
    static readonly Person[] Data =
    {
        new("Alice", 30, "Seattle"), new("Bob", 25, "Redmond"),
        new("Charlie", 41, "Bellevue"), new("Diana", 36, "Kirkland"),
        new("Erin", 29, "Tacoma"), new("Frank", 52, "Renton"),
        new("Grace", 33, "Bothell"), new("Hank", 47, "Sammamish"),
        new("Ivy", 28, "Issaquah"), new("Jack", 39, "Everett"),
        new("Kara", 44, "Lynnwood"), new("Leo", 31, "Shoreline"),
    };

    static readonly System.Collections.Generic.List<TableColumn> Columns = new()
    {
        new TableColumn("Name", nameof(Person.Name)),
        new TableColumn("Age", nameof(Person.Age)),
        new TableColumn("City", nameof(Person.City)),
    };

    public App()
    {
        if (Environment.GetEnvironmentVariable("TVDEMO_SHOT") == "1")
        {
            var q = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var t = q.CreateTimer(); t.Interval = TimeSpan.FromSeconds(9);
            t.Tick += async (s, e) => { t.Stop(); await CaptureAsync(); Application.Current.Exit(); };
            t.Start();
        }
    }

    static async System.Threading.Tasks.Task CaptureAsync()
    {
        var dir = AppContext.BaseDirectory; var log = Path.Combine(dir, "tvdemo-shot.log");
        try
        {
            var tv = Reactor.Controls.TableViewHandler.LastInstance;
            try { tv?.XamlRoot?.Content?.UpdateLayout(); tv?.UpdateLayout(); tv?.Measure(new Windows.Foundation.Size(900, 600)); tv?.Arrange(new Windows.Foundation.Rect(0, 0, 900, 600)); tv?.UpdateLayout(); } catch { }
            await System.Threading.Tasks.Task.Delay(500);
            UIElement root = tv;
            var rootSz = (root as FrameworkElement);
            var cells = root == null ? Enumerable.Empty<TextBlock>() : Descendants(root).OfType<TextBlock>();
            var texts = cells.Where(t => !string.IsNullOrEmpty(t.Text)).Select(t => t.Text).ToList();
            File.WriteAllText(log,
                $"tv: cols={tv?.Columns?.Count} actualH={tv?.ActualHeight} rootW={rootSz?.ActualWidth} rootH={rootSz?.ActualHeight}\n" +
                $"rendered text ({texts.Count}): " + string.Join(" | ", texts) + "\n");
            if (root != null)
            {
                var rtb = new RenderTargetBitmap(); await rtb.RenderAsync(root);
                var pix = await rtb.GetPixelsAsync();
                var bytes = new byte[pix.Length];
                using (var rd = Windows.Storage.Streams.DataReader.FromBuffer(pix)) rd.ReadBytes(bytes);
                var png = Path.Combine(dir, "tableview-in-reactor.png");
                using var fs = new FileStream(png, FileMode.Create);
                var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fs.AsRandomAccessStream());
                enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, bytes);
                await enc.FlushAsync();
                File.AppendAllText(log, $"PNG {rtb.PixelWidth}x{rtb.PixelHeight}\n");
            }
        }
        catch (Exception ex) { File.WriteAllText(log, "CAPTURE FAIL: " + ex + "\n"); }
    }

    static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        { var c = VisualTreeHelper.GetChild(root, i); yield return c; foreach (var d in Descendants(c)) yield return d; }
    }

    public override Element Render() =>
        VStack(12,
            TextBlock("Native C++/WinRT TableView — first-class Reactor control (typed element + handler), WinAppSDK 2.0.1"),
            TableView(Data, Columns, height: 420));
}

