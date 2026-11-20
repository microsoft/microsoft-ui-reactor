using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hooks;

namespace Microsoft.UI.Reactor.Data;

/// <summary>
/// Adapter bridging <see cref="IDataSource{T}"/> and <c>UseInfiniteResource</c>. Lets
/// any existing data source plug into the hook surface without modification.
/// </summary>
public static class DataSourceResourceExtensions
{
    /// <summary>
    /// Projects an <see cref="IDataSource{T}"/> through <c>UseInfiniteResource</c>,
    /// surfacing its pages as an <see cref="InfiniteResource{T}"/>. Deps are identity-based
    /// on <paramref name="source"/> and value-based on the request descriptors, so a new
    /// sort/filter/search restarts the pagination cleanly.
    /// </summary>
    /// <remarks>
    /// The initial page size comes from <c>options.PageSize</c> (default 50) when
    /// <paramref name="options"/> is supplied, or <c>request.PageSize</c> otherwise,
    /// and is clamped to at least 1.
    /// This size stays fixed across renders and query restarts. To change it,
    /// have the parent remount the calling component with a different key,
    /// e.g. <c>.WithKey($"tasks:{pageSize}")</c>, or increment a parent-owned
    /// reset version used by <c>.WithKey($"tasks:{resetVersion}")</c>.
    /// </remarks>
    public static InfiniteResource<T> UseDataSource<T>(
        this RenderContext ctx,
        IDataSource<T> source,
        DataRequest request,
        QueryCache cache,
        InfiniteResourceOptions? options = null,
        IHookDispatcher? dispatcher = null)
    {
        // IDataSource<T>'s ContinuationToken is offset-as-string by convention (see
        // ListDataSource / SqliteDataSource / GraphQLDataSource). Telling the hook how
        // to compute the cursor for an arbitrary page index bypasses the serial
        // "wait for page N-1" constraint baked into generic cursor paging — deep
        // scrolls can fetch pages in parallel this way, which matters for DataGrid
        // workflows that jump from row 0 to row 50 000 in a single scroll gesture.
        // The resource retains its initial options, so requests and cursors must retain its page size.
        var sizeRef = ctx.UseRef<int?>(null);
        int pageSize = sizeRef.Current ??= Math.Max(1, options?.PageSize ?? request.PageSize);
        var warnedRef = ctx.UseRef(false);
        // Keep the warning available if a diagnostic consumer is enabled on a later render.
        if (!warnedRef.Current
            && Math.Max(1, options?.PageSize ?? request.PageSize) != pageSize
            && DiagnosticLog.IsWarningEnabled)
        {
            warnedRef.Current = true;
            DiagnosticLog.Warning(LogCategory.Reactor, "UseDataSource.PageSize",
                $"PageSize changes are ignored; this hook retains {pageSize}. " +
                "To use a different size, remount the calling component by changing its key from the parent.");
        }
        // Use the same size for request batches, offset cursors, and resource row positions.
        options ??= InfiniteResourceOptions.Default;
        if (options.PageSize != pageSize)
            options = options with { PageSize = pageSize };

        return ctx.UseInfiniteResource<T, string>(
            fetchPage: async (cursor, ct) =>
            {
                var req = request with { PageSize = pageSize, ContinuationToken = cursor };
                var page = await source.GetPageAsync(req, ct).ConfigureAwait(false);
                return new Page<T, string>(page.Items, page.ContinuationToken, page.TotalCount);
            },
            cache: cache,
            deps: new object[] { source, request.Sort ?? (object)"", request.Filters ?? (object)"", request.SearchQuery ?? "" },
            options: options,
            dispatcher: dispatcher,
            cursorFromPageIndex: pageIndex => pageIndex == 0 ? null : (pageIndex * pageSize).ToString(global::System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Overload that reads the ambient <see cref="QueryCache"/> from
    /// <see cref="AppContexts.QueryCache"/>. <see cref="Hosting.ReactorHost"/> installs a
    /// process-wide default cache at startup; tests or subtrees may override it via
    /// <c>.Provide(AppContexts.QueryCache, customCache)</c>.
    /// </summary>
    /// <remarks>
    /// The initial page size comes from <c>options.PageSize</c> (default 50) when
    /// <paramref name="options"/> is supplied, or <c>request.PageSize</c> otherwise,
    /// and is clamped to at least 1.
    /// This size stays fixed across renders and query restarts. To change it,
    /// have the parent remount the calling component with a different key,
    /// e.g. <c>.WithKey($"tasks:{pageSize}")</c>, or increment a parent-owned
    /// reset version used by <c>.WithKey($"tasks:{resetVersion}")</c>.
    /// </remarks>
    public static InfiniteResource<T> UseDataSource<T>(
        this RenderContext ctx,
        IDataSource<T> source,
        DataRequest request,
        InfiniteResourceOptions? options = null,
        IHookDispatcher? dispatcher = null)
        => UseDataSource(ctx, source, request, ctx.UseContext(AppContexts.QueryCache), options, dispatcher);
}
