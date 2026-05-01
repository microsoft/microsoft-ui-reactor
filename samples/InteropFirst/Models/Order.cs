using System;

namespace InteropFirst.Models;

/// <summary>
/// Sample domain record exercised by the XAML <c>ListView</c> on the left
/// and the Reactor <c>DataGrid</c> on the right. Records compose well with
/// <c>x:Bind</c> (compile-time binding) for one-way readouts, which is what
/// this sample uses on the XAML side.
/// </summary>
public sealed record Order(int Id, string CustomerName, decimal Amount, DateTimeOffset PlacedAt);
