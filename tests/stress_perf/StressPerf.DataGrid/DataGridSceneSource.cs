using Microsoft.UI.Reactor.Data;

namespace StressPerf.DataGrid;

/// <summary>
/// A row of stock data for the DataGrid. Each row holds one cell per column.
/// Mirrors the row shape used by the legacy <c>StressPerf.ReactorGrid</c> scene,
/// reused here on the modern compare-mode /perf contract.
/// </summary>
public sealed record StockRow(int Id, StressPerf.Shared.StockItem[] Cells);

/// <summary>
/// Deterministic DataGrid workload data source for the /perf macro benchmark.
///
/// Unlike <see cref="StressPerf.Shared.StockDataSource"/> (a flat positional cell
/// array fed to a native <c>Grid</c> by the StocksGrid leg), this source drives the
/// REAL <c>DataGrid</c> control (<c>DataGridComponent&lt;StockRow&gt;</c>) through its
/// <see cref="IDataSource{T}"/> + <see cref="IObservableDataSource{T}"/> paging path —
/// the path that owns the per-render array/LINQ allocation (#663/#669) and the
/// per-cell/row modifier-delegate churn (#671) no other wired leg exercises.
///
/// Each tick, <see cref="Update"/> mutates a <c>percent</c> fraction of cells and
/// fires <see cref="DataChanged"/>. The grid's observable subscription reloads the
/// page, which bumps DataGridState and schedules a full
/// <c>DataGridComponent.Render()</c> — re-running, EVERY render and regardless of
/// whether sort/filter changed, the unconditional per-render rebuilds #663/#669
/// target (the <c>sortKey</c> join, the <c>DataRequest</c> + <c>.ToList()</c>s, the
/// header+row <c>colWidths</c>/<c>gridColDefs</c> arrays, the <c>Columns</c> getter's
/// <c>Where+ToList</c>, the per-column <c>GetSortDirection</c>/<c>GetColumnWidth</c>
/// LINQ, the row-def arrays, the setter spread) and re-allocating the per-realized-
/// cell/row <c>.OnTapped</c>/<c>.OnPointerPressed</c> closures inside <c>RenderRow</c>.
///
/// Deterministic (fixed RNG seed 42, matching <see cref="StressPerf.Shared.StockDataSource"/>)
/// so main-vs-PR /perf runs compare identical edit sequences; the row SET and count
/// are held constant (only cell VALUES change) so working-set and render-count stay
/// stable across the run.
/// </summary>
public sealed class DataGridSceneSource : IDataSource<StockRow>, IObservableDataSource<StockRow>
{
    /// <summary>Column count — wide so the per-column header/row LINQ (#128) scales.</summary>
    public const int DefaultColumns = 30;

    /// <summary>Row count — large enough to fully realize a tall viewport's worth of rows.</summary>
    public const int DefaultRows = 200;

    private readonly int _columnCount;
    private readonly int _rowCount;
    private readonly int _totalCells;
    private readonly StockRow[] _rows;
    private readonly Random _rng = new(42); // deterministic seed (matches StockDataSource)

    public DataGridSceneSource(int columns = DefaultColumns, int rows = DefaultRows)
    {
        if (columns < 1) columns = 1;
        if (rows < 1) rows = 1;
        _columnCount = columns;
        _rowCount = rows;
        _totalCells = columns * rows;
        _rows = new StockRow[rows];

        var rng = _rng;
        for (int r = 0; r < rows; r++)
        {
            var cells = new StressPerf.Shared.StockItem[columns];
            for (int c = 0; c < columns; c++)
            {
                char c1 = (char)('A' + (r % 26));
                char c2 = (char)('A' + (c / 3 % 26));
                char c3 = (char)('A' + (c % 26));
                string symbol = string.Create(3, (c1, c2, c3), static (span, s) =>
                {
                    span[0] = s.c1;
                    span[1] = s.c2;
                    span[2] = s.c3;
                });
                double price = Math.Round(10.0 + rng.NextDouble() * 990.0, 2);
                cells[c] = new StressPerf.Shared.StockItem(symbol, price, price, true);
            }
            _rows[r] = new StockRow(r, cells);
        }
    }

    public int ColumnCount => _columnCount;
    public int RowCount => _rowCount;

    public event EventHandler? DataChanged;

    public DataSourceCapabilities Capabilities => DataSourceCapabilities.ServerCount;

    public RowKey GetRowKey(StockRow item) => item.Id;

    public Task<DataPage<StockRow>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
    {
        var offset = 0;
        if (request.ContinuationToken is not null && int.TryParse(request.ContinuationToken, out var parsed))
            offset = parsed;

        var pageSize = Math.Min(request.PageSize, _rowCount - offset);
        if (pageSize <= 0)
            return Task.FromResult(new DataPage<StockRow>(Array.Empty<StockRow>(), null, _rowCount));

        var items = new StockRow[pageSize];
        Array.Copy(_rows, offset, items, 0, pageSize);

        var nextOffset = offset + pageSize;
        var continuation = nextOffset < _rowCount ? nextOffset.ToString() : null;

        return Task.FromResult(new DataPage<StockRow>(items, continuation, _rowCount));
    }

    /// <summary>
    /// Mutate a <paramref name="percent"/> fraction of cells (same value-churn logic
    /// as <see cref="StressPerf.Shared.StockDataSource.Update"/>) and fire
    /// <see cref="DataChanged"/> to drive the DataGrid's reload → re-render.
    /// The row SET is unchanged (only cell VALUES mutate), so the workload measures
    /// the per-render reconcile/allocation path, not structural list churn.
    /// </summary>
    /// <returns>The number of cells actually mutated (for logging parity).</returns>
    public int Update(double percent)
    {
        int count = Math.Max(1, (int)(_totalCells * percent / 100.0));
        var rng = _rng;

        for (int i = 0; i < count; i++)
        {
            int idx = rng.Next(_totalCells);
            int row = idx / _columnCount;
            int col = idx % _columnCount;
            var cells = _rows[row].Cells;
            var item = cells[col];
            double delta = ((rng.NextDouble() - 0.48) * 2.0) * item.CurrentPrice * 0.02;
            double newPrice = Math.Max(0.01, Math.Round(item.CurrentPrice + delta, 2));
            cells[col] = new StressPerf.Shared.StockItem(item.Symbol, item.CurrentPrice, newPrice, newPrice >= item.CurrentPrice);
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
        return count;
    }
}
