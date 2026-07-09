using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor.Data;

namespace HeadTrax.DataSources;

/// <summary>
/// IDataSource&lt;Dictionary&lt;string, object?&gt;&gt; backed by a GraphQL endpoint.
/// Translates DataRequest into a GraphQL query and maps the response back
/// to dictionary rows.
///
/// Written to live in the sample app for now but structured so it could move
/// into Microsoft.UI.Reactor.Data.Providers as a first-class provider later.
/// </summary>
internal sealed class GraphQLDataSource :
    IDataSource<Dictionary<string, object?>>,
    IDisposable
{
    private readonly string _endpoint;
    private readonly HttpClient _httpClient;

    // Map snake_case field names → camelCase GraphQL field names
    private static readonly Dictionary<string, string> FieldToGraphQL = new()
    {
        ["id"] = "id",
        ["employee_number"] = "employeeNumber",
        ["first_name"] = "firstName",
        ["last_name"] = "lastName",
        ["email"] = "email",
        ["phone"] = "phone",
        ["title"] = "title",
        ["department"] = "department",
        ["location"] = "location",
        ["hire_date"] = "hireDate",
        ["salary"] = "salary",
        ["manager_id"] = "managerId",
        ["level"] = "level",
        ["status"] = "status",
        ["birth_date"] = "birthDate",
        ["gender"] = "gender",
        ["performance_rating"] = "performanceRating",
        ["stock_options"] = "stockOptions",
        ["is_remote"] = "isRemote",
        ["cost_center"] = "costCenter",
        ["created_at"] = "createdAt",
        ["updated_at"] = "updatedAt",
    };

    // Reverse map: camelCase → snake_case
    private static readonly Dictionary<string, string> GraphQLToField =
        FieldToGraphQL.ToDictionary(kv => kv.Value, kv => kv.Key);

    // All GraphQL field names for the default projection
    private static readonly string AllFieldsFragment = string.Join("\n    ",
        FieldToGraphQL.Values);

    public GraphQLDataSource(string endpoint)
    {
        _endpoint = endpoint;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Running total of rows fetched across all GetPageAsync calls.</summary>
    public int TotalRowsFetched { get; private set; }

    /// <summary>Optional callback fired after each page fetch with the new total.</summary>
    public Action<int>? OnRowsFetchedChanged { get; set; }

    public DataSourceCapabilities Capabilities =>
        DataSourceCapabilities.ServerSort |
        DataSourceCapabilities.ServerFilter |
        DataSourceCapabilities.ServerSearch |
        DataSourceCapabilities.ServerCount |
        DataSourceCapabilities.ServerSelect;

    public RowKey GetRowKey(Dictionary<string, object?> item) =>
        item.TryGetValue("id", out var id) && id is not null
            ? new RowKey(id.ToString()!)
            : throw new InvalidOperationException("Row missing 'id' key.");

    // ── Paged query ─────────────────────────────────────────────────────────

    public async Task<DataPage<Dictionary<string, object?>>> GetPageAsync(
        DataRequest request,
        CancellationToken cancellationToken = default)
    {
        // Build the GraphQL query
        var query = BuildQuery(request);
        var variables = BuildVariables(request);

        var payload = new GraphQLRequest(query, variables);
        var content = new StringContent(
            JsonSerializer.Serialize(payload, HeadTraxGraphQLJsonContext.Default.GraphQLRequest),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            HeadTraxGraphQLJsonContext.Default.GraphQLResponse, cancellationToken);

        if (result?.Data?.Employees is null)
            return new DataPage<Dictionary<string, object?>>([], null, 0);

        var page = result.Data.Employees;
        var items = new List<Dictionary<string, object?>>(page.Items.Count);

        foreach (var item in page.Items)
        {
            items.Add(MapToDictionary(item));
        }

        TotalRowsFetched += items.Count;
        OnRowsFetchedChanged?.Invoke(TotalRowsFetched);

        return new DataPage<Dictionary<string, object?>>(
            items,
            page.ContinuationToken,
            page.TotalCount);
    }

    // ── Query construction ──────────────────────────────────────────────────

    private string BuildQuery(DataRequest request)
    {
        // Determine which fields to fetch
        string fields;
        if (request.Select is { Count: > 0 })
        {
            var graphqlFields = new List<string> { "id" };
            foreach (var f in request.Select)
            {
                if (FieldToGraphQL.TryGetValue(f, out var gql) && gql != "id")
                    graphqlFields.Add(gql);
            }
            fields = string.Join("\n    ", graphqlFields);
        }
        else
        {
            fields = AllFieldsFragment;
        }

        return $$"""
            query GetEmployees(
                $pageSize: Int
                $continuationToken: String
                $sort: [SortInput!]
                $filters: [FilterInput!]
                $searchQuery: String
            ) {
                employees(
                    pageSize: $pageSize
                    continuationToken: $continuationToken
                    sort: $sort
                    filters: $filters
                    searchQuery: $searchQuery
                ) {
                    items {
                        {{fields}}
                    }
                    totalCount
                    continuationToken
                }
            }
            """;
    }

    private static JsonObject BuildVariables(DataRequest request)
    {
        var vars = new JsonObject
        {
            ["pageSize"] = request.PageSize,
            ["continuationToken"] = request.ContinuationToken,
        };

        if (!string.IsNullOrWhiteSpace(request.SearchQuery))
            vars["searchQuery"] = request.SearchQuery;

        if (request.Sort is { Count: > 0 })
        {
            var sortItems = request.Sort.Select(s => (JsonNode?)new JsonObject
            {
                ["field"] = FieldToGraphQL.GetValueOrDefault(s.Field, s.Field),
                ["direction"] = s.Direction == SortDirection.Descending ? "DESC" : "ASC",
            }).ToArray();
            vars["sort"] = new JsonArray(sortItems);
        }

        if (request.Filters is { Count: > 0 })
        {
            var filterItems = request.Filters.Select(f => (JsonNode?)new JsonObject
            {
                ["field"] = FieldToGraphQL.GetValueOrDefault(f.Field, f.Field),
                ["operator"] = MapFilterOperator(f.Operator),
                ["value"] = f.Value?.ToString(),
                ["valueTo"] = f.ValueTo?.ToString(),
            }).ToArray();
            vars["filters"] = new JsonArray(filterItems);
        }

        return vars;
    }

    private static string MapFilterOperator(FilterOperator op) => op switch
    {
        FilterOperator.Equals => "EQUALS",
        FilterOperator.NotEquals => "NOT_EQUALS",
        FilterOperator.Contains => "CONTAINS",
        FilterOperator.StartsWith => "STARTS_WITH",
        FilterOperator.EndsWith => "ENDS_WITH",
        FilterOperator.GreaterThan => "GREATER_THAN",
        FilterOperator.GreaterThanOrEqual => "GREATER_THAN_OR_EQUAL",
        FilterOperator.LessThan => "LESS_THAN",
        FilterOperator.LessThanOrEqual => "LESS_THAN_OR_EQUAL",
        FilterOperator.Between => "BETWEEN",
        FilterOperator.In => "IN",
        FilterOperator.IsNull => "IS_NULL",
        FilterOperator.IsNotNull => "IS_NOT_NULL",
        _ => "EQUALS",
    };

    // ── Response mapping ────────────────────────────────────────────────────

    private static Dictionary<string, object?> MapToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var prop in element.EnumerateObject())
        {
            // Convert camelCase back to snake_case
            var fieldName = GraphQLToField.GetValueOrDefault(prop.Name, prop.Name);

            dict[fieldName] = prop.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number when prop.Value.TryGetInt64(out var l) => l,
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => prop.Value.GetRawText(),
            };
        }

        return dict;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // ── Response DTOs ───────────────────────────────────────────────────────

    internal sealed class GraphQLResponse
    {
        public GraphQLData? Data { get; set; }
        public List<GraphQLError>? Errors { get; set; }
    }

    internal sealed class GraphQLData
    {
        public EmployeePage? Employees { get; set; }
    }

    internal sealed class EmployeePage
    {
        public List<JsonElement> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public string? ContinuationToken { get; set; }
    }

    internal sealed class GraphQLError
    {
        public string? Message { get; set; }
    }
}

/// <summary>GraphQL request envelope (query + variables tree).</summary>
internal sealed record GraphQLRequest(string query, JsonNode variables);

/// <summary>
/// Source-generated (reflection-free / NativeAOT-safe) JSON metadata for the
/// GraphQL request/response DTOs. CamelCase + drop-nulls mirror the previous
/// hand-rolled <c>JsonOptions</c>; the <c>variables</c> tree is a verbatim
/// <see cref="JsonNode"/> so its keys pass through untouched.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphQLRequest))]
[JsonSerializable(typeof(GraphQLDataSource.GraphQLResponse))]
internal partial class HeadTraxGraphQLJsonContext : JsonSerializerContext
{
}
