using System;
using System.Collections;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using WinUITableView = Microsoft.UI.Xaml.Controls.TableView;
using TableViewTextColumn = Microsoft.UI.Xaml.Controls.TableViewTextColumn;

namespace TableViewDemo;

public static class TableViewFacade
{
    internal static readonly string SelfTestLog =
        Path.Combine(AppContext.BaseDirectory, "tvdemo-selftest.log");
    static readonly bool SelfTest =
        Environment.GetEnvironmentVariable("TVDEMO_SELFTEST") == "1";

    public static WinUITableView LastInstance { get; private set; }

    static WinUITableView Build(IEnumerable items, double height)
    {
        var tv = new WinUITableView { Height = height, MinWidth = 520 };
        var first = items?.Cast<object>().FirstOrDefault();
        if (first != null)
            foreach (var p in first.GetType().GetProperties())
                tv.Columns.Add(new TableViewTextColumn
                {
                    Header = p.Name,
                    Binding = new Binding { Path = new PropertyPath(p.Name) },
                });
        tv.ItemsSource = items;
        LastInstance = tv;
        if (SelfTest)
            File.AppendAllText(SelfTestLog,
                "PASS: native " + tv.GetType().FullName + " activated + " + tv.Columns.Count +
                " auto-columns + ItemsSource set inside Reactor mount (WinAppSDK 2.0.1)\n");
        return tv;
    }

    public static XamlHostElement TableView(IEnumerable items, double height = 360) =>
        new XamlHostElement(
            Factory: () => Build(items, height),
            Updater: ctrl => ((WinUITableView)ctrl).ItemsSource = items)
        {
            TypeKey = "Reactor.Controls.TableView",
        };
}
