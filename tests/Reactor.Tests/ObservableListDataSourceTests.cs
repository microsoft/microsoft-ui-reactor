using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for ObservableListDataSource: collection changes and INPC tracking.
/// </summary>
public class ObservableListDataSourceTests
{
    private class InpcItem : INotifyPropertyChanged
    {
        private int _id;
        private string _name = "";

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPC(nameof(Id)); } }
        }
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPC(nameof(Name)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPC(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    [Fact]
    public void Adding_Item_Fires_DataChanged()
    {
        var collection = new ObservableCollection<InpcItem>();
        var source = new ObservableListDataSource<InpcItem>(collection, x => (RowKey)x.Id);
        var fired = false;
        source.DataChanged += (_, _) => fired = true;

        collection.Add(new InpcItem { Id = 1, Name = "Alice" });
        Assert.True(fired);
    }

    [Fact]
    public void Removing_Item_Fires_DataChanged()
    {
        var item = new InpcItem { Id = 1, Name = "Alice" };
        var collection = new ObservableCollection<InpcItem> { item };
        var source = new ObservableListDataSource<InpcItem>(collection, x => (RowKey)x.Id);
        var fired = false;
        source.DataChanged += (_, _) => fired = true;

        collection.Remove(item);
        Assert.True(fired);
    }

    [Fact]
    public void Item_PropertyChanged_Fires_DataChanged()
    {
        var item = new InpcItem { Id = 1, Name = "Alice" };
        var collection = new ObservableCollection<InpcItem> { item };
        var source = new ObservableListDataSource<InpcItem>(collection, x => (RowKey)x.Id);
        var fired = false;
        source.DataChanged += (_, _) => fired = true;

        item.Name = "Alice Updated";
        Assert.True(fired);
    }

    [Fact]
    public void Dispose_Cleans_Up_Subscriptions()
    {
        var item = new InpcItem { Id = 1, Name = "Alice" };
        var collection = new ObservableCollection<InpcItem> { item };
        var source = new ObservableListDataSource<InpcItem>(collection, x => (RowKey)x.Id);
        var count = 0;
        source.DataChanged += (_, _) => count++;

        source.Dispose();

        // After dispose, changes should NOT fire
        item.Name = "Changed";
        collection.Add(new InpcItem { Id = 2, Name = "Bob" });
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Refetching_After_DataChanged_Reflects_New_State()
    {
        var collection = new ObservableCollection<InpcItem>
        {
            new InpcItem { Id = 1, Name = "Alice" },
        };
        var source = new ObservableListDataSource<InpcItem>(collection, x => (RowKey)x.Id);

        var page1 = await source.GetPageAsync(new DataRequest());
        Assert.Single(page1.Items);

        collection.Add(new InpcItem { Id = 2, Name = "Bob" });

        var page2 = await source.GetPageAsync(new DataRequest());
        Assert.Equal(2, page2.Items.Count);
    }

    [Fact]
    public void DataChanged_Provides_Sender()
    {
        var collection = new ObservableCollection<InpcItem>();
        var source = new ObservableListDataSource<InpcItem>(collection, x => (RowKey)x.Id);
        object? capturedSender = null;
        source.DataChanged += (s, _) => capturedSender = s;

        collection.Add(new InpcItem { Id = 1, Name = "Alice" });
        Assert.Same(source, capturedSender);
    }

    [Fact]
    public void DataChanged_From_INPC_Provides_Sender()
    {
        var item = new InpcItem { Id = 1, Name = "Alice" };
        var collection = new ObservableCollection<InpcItem> { item };
        var source = new ObservableListDataSource<InpcItem>(collection, x => (RowKey)x.Id);
        object? capturedSender = null;
        source.DataChanged += (s, _) => capturedSender = s;

        item.Name = "Updated";
        Assert.Same(source, capturedSender);
    }
}
