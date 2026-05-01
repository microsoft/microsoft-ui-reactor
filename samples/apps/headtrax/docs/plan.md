# HeadTrax – Sample App Spec & Build Plan

## Goal

A sample app that showcases the Reactor DataGrid with data virtualization against a
fake 150–250K employee database. Two data paths demonstrate the provider model:

1. **SQLite Direct** – C# `IDataSource<Dictionary<string, object?>>` that
   translates `DataRequest` into parameterized SQL.
2. **GraphQL API** – C# `IDataSource<Dictionary<string, object?>>` that posts
   queries to a local Node.js Apollo Server, which in turn reads the same SQLite
   database.

Both providers advertise full server-side capabilities (sort, filter, search,
count, projection). The app lets the user toggle between them at runtime so
behavior can be compared side-by-side.

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│  HeadTrax  (Reactor WinUI app)                     │
│                                                  │
│  ┌──────────┐   ┌──────────────┐                │
│  │ Toolbar   │   │ EmployeeGrid │  ← DataGrid<> │
│  └──────────┘   └──────┬───────┘                │
│                         │ IDataSource<Dict>      │
│            ┌────────────┴────────────┐           │
│            ▼                         ▼           │
│   SqliteDataSource          GraphQLDataSource    │
│   (Microsoft.Data.Sqlite)   (HttpClient → GQL)   │
└────────────┬─────────────────────────┬───────────┘
             │ reads file directly     │ HTTP POST
             ▼                         ▼
        ┌──────────┐          ┌──────────────┐
        │ SQLite DB │◄─────── │ Node.js svc  │
        │ headtrax  │         │ Apollo Server │
        │  .db      │         └──────────────┘
        └──────────┘
             ▲
             │ seeded by
        generate-data.js
```

### Row type: `Dictionary<string, object?>`

We avoid code-generated C# models. Rows are plain dictionaries keyed by
snake_case column name. `FieldDescriptor` instances are built manually in
`EmployeeSchema.cs` with lambda accessors into the dictionary. This keeps the
sample dynamic and demonstrates that `DataGrid<T>` works with any T, not just
reflection-friendly records.

---

## Directory Layout

```
samples/apps/headtrax/
├── docs/
│   ├── plan.md                 ← this file
│   └── missing-features.md    ← DSL gaps found during development
│
├── service/                    ← Node.js (data gen + GraphQL server)
│   ├── package.json
│   ├── .gitignore
│   ├── schema.sql              ← SQLite DDL (employees table, indexes, FTS)
│   ├── generate-data.js        ← Faker-based data seeder
│   └── server.js               ← Express + Apollo Server 4
│
└── HeadTrax/                   ← C# Reactor app
    ├── HeadTrax.csproj
    ├── Program.cs              ← Entry point, CLI arg parsing
    ├── AppConfig.cs            ← Runtime config (db path, graphql url)
    ├── App.cs                  ← Root component, data source switching
    ├── Schema/
    │   └── EmployeeSchema.cs   ← FieldDescriptor column definitions
    ├── DataSources/
    │   ├── SqliteDataSource.cs ← IDataSource + IKeyedDataSource over SQLite
    │   └── GraphQLDataSource.cs← IDataSource over GraphQL
    └── Components/
        ├── Toolbar.cs          ← Data source picker, status bar
        └── EmployeeGrid.cs     ← DataGrid wrapper with column toggle
```

---

## Database Schema

Table: `employees` (see `service/schema.sql` for full DDL)

| Column               | Type    | Notes                        |
|----------------------|---------|------------------------------|
| id                   | INTEGER | PK, auto-increment           |
| employee_number      | TEXT    | `HT-000001` format           |
| first_name           | TEXT    |                              |
| last_name            | TEXT    |                              |
| email                | TEXT    |                              |
| phone                | TEXT    |                              |
| title                | TEXT    | Level-appropriate job title   |
| department           | TEXT    | 14 departments               |
| location             | TEXT    | 15 global office locations   |
| hire_date            | TEXT    | ISO 8601 date                |
| salary               | REAL    | Level-banded                 |
| manager_id           | INTEGER | FK → employees(id), nullable |
| level                | INTEGER | 0 (CEO) – 7 (IC)            |
| status               | TEXT    | Active / On Leave / Terminated |
| birth_date           | TEXT    |                              |
| gender               | TEXT    |                              |
| performance_rating   | REAL    | 1.0 – 5.0                   |
| stock_options        | INTEGER |                              |
| is_remote            | INTEGER | 0/1 boolean                  |
| cost_center          | TEXT    |                              |
| created_at           | TEXT    |                              |
| updated_at           | TEXT    |                              |

Indexes on every column the grid might sort/filter on. FTS5 virtual table
covers `first_name, last_name, email, title, department, location` with
automatic sync triggers.

### Hierarchy shape (for 200K target)

| Level | Role          | ~Count  |
|-------|---------------|---------|
| 0     | CEO           | 1       |
| 1     | C-suite / SVP | ~600    |
| 2     | VP            | ~2,400  |
| 3     | Director      | ~6,000  |
| 4     | Sr Manager    | ~12,000 |
| 5     | Manager       | ~24,000 |
| 6     | Lead / Sr IC  | ~30,000 |
| 7     | IC            | ~125,000|

Generated top-down so every employee's manager exists before they're inserted.
Children are distributed across parents at the level above via modulo (with
variance in the data itself from random title/dept/location assignment).

---

## What Has Been Built

### Fully written – needs compilation testing

| File | Status | Notes |
|------|--------|-------|
| `service/schema.sql` | **Done** | DDL, indexes, FTS, triggers |
| `service/package.json` | **Done** | Dependencies declared |
| `service/generate-data.js` | **Done** | `@faker-js/faker`, batched insert (5K per txn), progress output |
| `service/server.js` | **Done** | Apollo Server 4, parameterized query builder, column whitelist, camelCase mapping, prepared statements for lookups |
| `service/.gitignore` | **Done** | Ignores `node_modules/`, `*.db` |
| `HeadTrax/HeadTrax.csproj` | **Done** | References Reactor + Microsoft.Data.Sqlite |
| `HeadTrax/Program.cs` | **Done** | Parses `--db` and `--graphql-url` CLI args |
| `HeadTrax/AppConfig.cs` | **Done** | Static config with defaults |
| `HeadTrax/App.cs` | **Done** | FlexColumn layout, UseRef-based IDisposable cleanup for data source swaps, UseEffect unmount cleanup |
| `HeadTrax/Schema/EmployeeSchema.cs` | **Done** | 22 `FieldDescriptor` columns with categories, formatters, widths |
| `HeadTrax/DataSources/SqliteDataSource.cs` | **Done** | Full `IDataSource` + `IKeyedDataSource`, parameterized SQL, FTS search, column whitelist |
| `HeadTrax/DataSources/GraphQLDataSource.cs` | **Done** | Full `IDataSource`, builds GraphQL query string, maps camelCase ↔ snake_case, `JsonElement` row parsing |
| `HeadTrax/Components/Toolbar.cs` | **Done** | ToggleButton pair for data source switching, FlexColumn spacers, `with {}` for flex props |
| `HeadTrax/Components/EmployeeGrid.cs` | **Done** | FlexColumn layout, `with {}` for flex props, DataGrid call |

### Phase 1 complete

- HeadTrax **added** to `Reactor.sln`.
- `npm install` run in `service/`.
- 500-row dev dataset generated (`headtrax.db` exists).
- C# app compiles cleanly (0 errors, 1 harmless nullable warning).
- GraphQL server starts and serves data.

---

## Resolved Compile Issues

All compile issues from the original draft have been fixed:

1. **Missing DSL methods** – `Spacer()` → `FlexColumn().Width(N)`,
   `FlexSpacer()` → `FlexColumn().Flex(grow: 1)`,
   `SegmentedControl` → two `ToggleButton`s with manual state.
2. **Wrong fluent method names** – `.Grow(1)` → `.Flex(grow: 1)`,
   `.VerticalAlignment()` → `.VAlign()`,
   `.Gap()/.AlignItems()/.JustifyContent()` → `with { ColumnGap, AlignItems, JustifyContent }`.
3. **VStack vs FlexColumn** – Replaced `VStack` with `FlexColumn` where children need flex grow.
4. **IDisposable leak** – `App.cs` now uses `UseRef` to track the current data source
   and disposes the old instance in `UseMemo` on mode change, plus `UseEffect` cleanup on unmount.
5. **Missing `using Microsoft.UI.Reactor;`** – All files needed `using Microsoft.UI.Reactor;` for extension methods
   (`.Flex()`, `.Opacity()`, `.FontSize()`, `.VAlign()`, `.Width()`, etc.).
6. **`args` name conflict** – Renamed to `cliArgs` in `Program.cs` (top-level statements
   already define `args`).
7. **Wrong npm package** – `@as-integrations/express` → `@apollo/server/express4`
   (built into `@apollo/server`).

---

## Remaining Work

### Phase 1: Make it compile and run ✅

All Phase 1 tasks are complete. The app compiles, npm deps are installed,
a 500-row dev dataset exists, and the GraphQL server starts cleanly.

### Phase 2: Full demo features

6. ✅ **Search bar** – Enabled via `showSearch: true` on the DataGrid. Uses
   the built-in search UI which passes `SearchQuery` to the data source.
   Both providers already have FTS support.
7. **Filter panel** – Column-specific filter UI. Could be a flyout per column
   header or a sidebar filter panel. Both data sources already support all
   `FilterOperator` values.
8. ✅ **Employee detail pane** – `rowDetailTemplate` on the DataGrid shows a
   three-column layout (person, org, compensation) when a row is expanded
   via the ▶ toggle.
9. **Column chooser** – UI for toggling column visibility beyond the current
   all/compact toggle. Could use a `CheckBox` list in a flyout.
10. ✅ **Status bar** – DataGrid's built-in search bar shows total count.
    Toolbar shows employee count and data source mode.
11. **Manager hierarchy drill-down** – Filter to show direct reports of a
    selected employee. Uses `manager_id` filter.

### Phase 3: Polish

12. **Error handling** – GraphQL connection failures, missing database file,
    schema mismatch. Show user-friendly error states.
13. **Loading states** – Skeleton rows or shimmer while pages load.
14. **Theming** – Dark/light mode support using Reactor's theme system.
15. **Performance measurement** – Add timing instrumentation to compare
    SQLite direct vs GraphQL latency at various dataset sizes.

---

## Key Design Decisions

### Why `Dictionary<string, object?>` instead of a C# record?

GraphQL responses are inherently dynamic. Using dictionaries avoids:
- Code generation for matching C# types to the GraphQL schema.
- Breaking changes when columns are added to the database.
- Demonstrates that `DataGrid<T>` + `FieldDescriptor` work with any T.

The trade-off is losing compile-time type safety on field access, but
`EmployeeSchema.cs` centralizes field names so typos are caught in one place.

### Why snake_case in the dictionary?

The SQLite column names are snake_case. Using them as-is in the dictionary
avoids a mapping layer on the SQLite side. The GraphQL data source maps
camelCase responses back to snake_case so both providers produce identical
dictionary keys. This means `EmployeeSchema.cs` and the grid only deal with
one naming convention.

### Why a column whitelist in SqliteDataSource?

`DataRequest.Sort` and `DataRequest.Filters` contain user-controlled field
names that get interpolated into SQL. The whitelist in `AllowedColumns`
prevents SQL injection. Column names can't be parameterized in SQLite, so
validation is the only defense.

### Why `better-sqlite3` in Node.js?

Synchronous API means no callback overhead for the query-heavy GraphQL
resolvers. It's also significantly faster than `sqlite3` (async wrapper)
for read-heavy workloads, which matters when serving 250K-row datasets.

---

## Running the Sample

```bash
# 1. Generate data
cd samples/apps/headtrax/service
npm install
npm run generate              # 200K employees (~15-30s)
# or: npm run generate:small  # 500 employees for quick dev

# 2. Start GraphQL server (only needed for GraphQL mode)
npm start
# → http://localhost:4000/graphql

# 3. Run the C# app
cd ../HeadTrax
dotnet run
# or: dotnet run -- --db /path/to/headtrax.db --graphql-url http://localhost:4000/graphql
```

---

## Framework APIs Used

| Reactor API | Where |
|---|---|
| `IDataSource<T>` | Both data sources implement this |
| `IKeyedDataSource<T>` | SqliteDataSource (for future detail pane) |
| `DataRequest` / `DataPage<T>` | Core request/response protocol |
| `SortDescriptor` / `FilterDescriptor` | Server-side sort & filter translation |
| `FieldDescriptor` | Column definitions in EmployeeSchema |
| `DataSourceCapabilities` | Both sources declare full server capabilities |
| `RowKey` | Employee ID as row identity |
| `DataGrid<T>()` | Grid instantiation with explicit columns |
| `Component<TProps>` | All UI components |
| `UseState` / `UseMemo` | State management in App, EmployeeGrid |
| `FlexRow` / `FlexColumn` | Layout |
| `ToggleSwitch` | Column visibility toggle |
| `ProgressRing` | Loading indicator |
| `Text` / `Heading` | Labels |

---

## GraphQL Schema Summary

The Node.js server exposes:

```graphql
employees(pageSize, continuationToken, sort, filters, searchQuery, select) → EmployeePage
employee(id) → Employee
departments → [String]
locations  → [String]
titles     → [String]
stats      → DataStats { totalEmployees, activePct, avgSalary, ... }
```

`Employee` type includes `manager`, `directReports`, and `directReportCount`
resolved fields for hierarchy navigation. Sort/filter/search are translated
to parameterized SQL server-side with the same whitelist approach as the
C# `SqliteDataSource`.
