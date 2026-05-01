using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using InteropFirst.Bridges;
using InteropFirst.Models;

namespace InteropFirst;

/// <summary>
/// Spec 033 §7 — the conventional MVVM ViewModel that drives both the XAML
/// <c>ListView</c> (via <c>x:Bind</c>) and the Reactor <c>OrdersDataGrid</c>
/// (via Reactor props). One source of truth, two consumers.
/// </summary>
public sealed class MainPageViewModel : INotifyPropertyChanged, IDisposable
{
    private int _nextId;
    private Order? _selectedOrder;

    public ObservableCollection<Order> Items { get; } = new();

    public Order? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (!ReferenceEquals(_selectedOrder, value))
            {
                _selectedOrder = value;
                OnPropertyChanged();
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }

    public MainPageViewModel()
    {
        AddCommand = new RelayCommand(Add);
        DeleteCommand = new RelayCommand(Delete, () => SelectedOrder is not null);
        SeedSampleData();
    }

    private void Add()
    {
        var id = ++_nextId;
        Items.Add(new Order(
            Id: id,
            CustomerName: $"Customer {id}",
            Amount: 100m * id,
            PlacedAt: DateTimeOffset.UtcNow));
    }

    private void Delete()
    {
        if (SelectedOrder is null) return;
        Items.Remove(SelectedOrder);
        SelectedOrder = null;
    }

    private void SeedSampleData()
    {
        // Use invariant culture for seed strings so the on-disk content is
        // reproducible across locales — spec §7.4 directive.
        for (int i = 1; i <= 10; i++)
        {
            _nextId = i;
            Items.Add(new Order(
                Id: i,
                CustomerName: string.Format(CultureInfo.InvariantCulture, "Customer {0}", i),
                Amount: 100m * i,
                PlacedAt: DateTimeOffset.UtcNow.AddMinutes(-i * 7)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        Items.Clear();
        _selectedOrder = null;
    }
}
