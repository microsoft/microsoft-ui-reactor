namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// A JSON-Schema fragment for an MCP tool's <c>inputSchema</c> (or a nested
/// property). Recursive: object nodes carry <see cref="Properties"/> /
/// <see cref="Required"/>; leaf nodes carry <see cref="Type"/> plus optional
/// <see cref="Description"/> / <see cref="Enum"/>.
///
/// Registered in <see cref="DevtoolsJsonContext"/> so <c>tools/list</c> and the
/// GET /mcp schema document serialize through the source generator (Native AOT)
/// instead of the reflection fallback. Property declaration order + the context's
/// camelCase policy + WhenWritingNull reproduce the previous anonymous-object wire
/// byte-for-byte:
///   type, [description], [enum], [properties], [required], [additionalProperties].
/// </summary>
internal sealed record SchemaNode(
    string Type,
    string? Description = null,
    string[]? Enum = null,
    IReadOnlyDictionary<string, SchemaNode>? Properties = null,
    string[]? Required = null,
    bool? AdditionalProperties = null);

/// <summary>
/// Terse builders for <see cref="SchemaNode"/> so tool registrations read close to
/// the JSON they emit. <c>Root</c> is the top-level object schema (always
/// emits <c>additionalProperties:false</c> and a — possibly empty — properties map);
/// <c>Obj</c> is a nested object property (no <c>additionalProperties</c>).
/// </summary>
internal static class Schema
{
    private static readonly IReadOnlyDictionary<string, SchemaNode> EmptyProps =
        new Dictionary<string, SchemaNode>();

    public static SchemaNode Root(params (string Name, SchemaNode Node)[] properties)
        => new("object",
            Properties: properties.Length == 0 ? EmptyProps : Dict(properties),
            AdditionalProperties: false);

    public static SchemaNode Root(string[] required, params (string Name, SchemaNode Node)[] properties)
    {
        var props = Dict(properties);
        global::System.Diagnostics.Debug.Assert(
            global::System.Array.TrueForAll(required, r => props.ContainsKey(r)),
            "Schema.Root: every 'required' name must be a declared property.");
        return new("object", Properties: props, Required: required, AdditionalProperties: false);
    }

    public static SchemaNode Obj(string? description, params (string Name, SchemaNode Node)[] properties)
        => new("object", Description: description, Properties: Dict(properties));

    public static SchemaNode Str(string? description = null, string[]? oneOf = null)
        => new("string", Description: description, Enum: oneOf);

    public static SchemaNode Int(string? description = null) => new("integer", Description: description);

    public static SchemaNode Bool(string? description = null) => new("boolean", Description: description);

    public static SchemaNode Num(string? description = null) => new("number", Description: description);

    public static SchemaNode Arr(string? description = null) => new("array", Description: description);

    private static Dictionary<string, SchemaNode> Dict((string Name, SchemaNode Node)[] properties)
    {
        var d = new Dictionary<string, SchemaNode>(properties.Length);
        foreach (var (name, node) in properties)
            d[name] = node;
        return d;
    }
}

/// <summary>
/// The self-describing document emitted by GET /mcp — the tool inventory, selector
/// grammar, and protocol/schema versions in one payload. Named (vs. anonymous) so
/// it serializes through the source generator under Native AOT.
/// </summary>
internal sealed record SchemaDocument(
    string Schema,
    string ProtocolVersion,
    string Build,
    string Transport,
    string Endpoint,
    string SelectorGrammar,
    string TreeSchemaVersion,
    SchemaToolInfo[] Tools,
    SchemaEventInfo[] Events);

/// <summary>One tool entry in the GET /mcp schema document.</summary>
internal sealed record SchemaToolInfo(string Name, string Description, SchemaNode InputSchema);

/// <summary>One event entry in the GET /mcp schema document.</summary>
internal sealed record SchemaEventInfo(string Name, string Description);
