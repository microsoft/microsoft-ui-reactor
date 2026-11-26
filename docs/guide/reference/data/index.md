# Data

Async values, resources, the query cache and data sources.

**Learn more:** [Data System](../../data-system.md), [Async Resources](../../async-resources.md)

## Types

| Type | Description |
| --- | --- |
| [Async values](async-values.md) | The value a resource hook returns while it loads, after it loads and after it fails. |
| [Column attributes](column-attributes.md) | Attributes that change how a property shows as a column. |
| [Enums](enums.md) | The enumerations in this category. |
| [Paging](paging.md) | Types for infinite and paged resources. |
| [Query cache](query-cache.md) | The process-wide cache that resource hooks share. |
| [Sort and filter descriptors](descriptors.md) | Requests, sort, filter and field descriptors that data sources receive. |
| [`FocusRevalidationService`](FocusRevalidationService.md) | Central service that invalidates stale `QueryCache` entries when the app window is reactivated or resumed, so consumers that opted in via `ResourceOptions.RefetchOnWindowFocus = true` see fresh data after an Alt-Tab or resume. |
| [`IDataSource<T>`](IDataSource.md) | A read-only data source that provides paged access to items. |
| [`IKeyedDataSource<T>`](IKeyedDataSource.md) | A data source that supports fetching individual items by key. |
| [`IMutableDataSource<T>`](IMutableDataSource.md) | A mutable data source that supports CRUD operations. |
| [`IObservableDataSource<T>`](IObservableDataSource.md) | A data source that fires events when data changes. |
| [`ListDataSource<T>`](ListDataSource.md) | In-memory data source backed by a list. |
| [`ObservableListDataSource<T>`](ObservableListDataSource.md) | An observable data source that watches an ObservableCollection for changes and fires DataChanged when items are added, removed, or modified. |
