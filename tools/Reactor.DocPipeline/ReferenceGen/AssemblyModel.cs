using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

internal enum ApiTypeKind { Class, StaticClass, Struct, Interface, Enum, Delegate, Record, RecordStruct }

internal enum ApiMemberKind { Constructor, Property, Method, Event, Field, Operator }

/// <summary>One parameter of a member, already formatted for display.</summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Type">The C# type as a reader would write it, for example <c>Action?</c>.</param>
/// <param name="TypeDocId">The <c>T:</c> doc ID of the parameter's type, when it is a named type.</param>
/// <param name="Default">The default value as C# source, or <c>null</c> when the parameter is required.</param>
/// <param name="Modifier">One of <c>this</c>, <c>params</c>, <c>ref</c>, <c>out</c>, <c>in</c>, or empty.</param>
internal sealed record ApiParam(string Name, string Type, string? TypeDocId, string? Default, string Modifier);

/// <summary>A public type, read from the compiled assembly or synthesized from the XML file.</summary>
internal sealed class ApiType
{
    public required string DocId { get; init; }
    public required string Namespace { get; init; }

    /// <summary>The simple name without arity, for example <c>Component</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The C# name with type parameters, for example <c>Component&lt;TProps&gt;</c>.
    /// Nested types include the outer type: <c>Outer.Inner</c>.</summary>
    public required string DisplayName { get; init; }

    public ApiTypeKind Kind { get; init; }
    public bool IsSynthetic { get; init; }
    public ApiType? DeclaringType { get; init; }
    public IReadOnlyList<string> TypeParameters { get; init; } = Array.Empty<string>();
    public string Declaration { get; set; } = string.Empty;
    public string? BaseDocId { get; set; }
    public List<ApiMember> Members { get; } = new();
    public List<ApiMember> EnumValues { get; } = new();
    public ApiMember? PrimaryConstructor { get; set; }
    public string? ObsoleteMessage { get; set; }

    public override string ToString() => DocId;
}

/// <summary>A public member of an <see cref="ApiType"/>.</summary>
internal sealed class ApiMember
{
    public required string DocId { get; init; }
    public required ApiMemberKind Kind { get; init; }

    /// <summary>The member name as a reader looks it up (<c>Margin</c>, or the type name for a constructor).</summary>
    public required string Name { get; init; }
    public required ApiType DeclaringType { get; init; }

    /// <summary>The full C# declaration line, without a body.</summary>
    public required string Signature { get; init; }

    /// <summary>The short form used in headings and links, for example <c>Margin&lt;T&gt;(T, double)</c>.</summary>
    public required string ShortSignature { get; init; }

    public IReadOnlyList<ApiParam> Params { get; init; } = Array.Empty<ApiParam>();
    public IReadOnlyList<string> TypeParameters { get; init; } = Array.Empty<string>();
    public string? ReturnType { get; init; }
    public string? ReturnDocId { get; init; }
    public bool IsStatic { get; init; }
    public bool IsExtension { get; init; }

    /// <summary>For an extension method, the doc IDs of the types it extends: the
    /// <c>this</c> parameter's type, or its constraint types when it is generic.</summary>
    public IReadOnlyList<string> ExtendedTypeDocIds { get; init; } = Array.Empty<string>();

    /// <summary>The literal value of a constant or enum field.</summary>
    public string? ConstantValue { get; init; }
    public string? ObsoleteMessage { get; init; }

    public override string ToString() => DocId;
}

/// <summary>
/// The public surface of one assembly. Built from metadata with
/// <see cref="System.Reflection.Metadata"/>, so it needs no runtime and no
/// referenced assemblies (the WinUI binaries are not next to <c>Microsoft.UI.Reactor.dll</c>).
/// When the assembly is not available, <see cref="FromXml"/> builds a smaller
/// model from the doc IDs in the XML file alone.
/// </summary>
internal sealed class ApiModel
{
    public IReadOnlyList<ApiType> Types { get; }
    public IReadOnlyDictionary<string, ApiType> TypesByDocId { get; }
    public IReadOnlyDictionary<string, ApiMember> MembersByDocId { get; }

    /// <summary>True when the model came from the compiled assembly, so signatures,
    /// defaults, and extension methods are exact.</summary>
    public bool FromMetadata { get; }

    public ApiModel(IEnumerable<ApiType> types, bool fromMetadata)
    {
        Types = types.OrderBy(t => t.DocId, StringComparer.Ordinal).ToList();
        TypesByDocId = Types.ToDictionary(t => t.DocId, StringComparer.Ordinal);
        var members = new Dictionary<string, ApiMember>(StringComparer.Ordinal);
        foreach (var t in Types)
        {
            foreach (var m in t.Members.Concat(t.EnumValues))
                members.TryAdd(m.DocId, m);
        }
        MembersByDocId = members;
        FromMetadata = fromMetadata;
    }

    // ── XML-only fallback ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a model from XML doc IDs alone. Signatures come from
    /// <see cref="CrefSignature"/>; members attach to their declaring type, and a
    /// type without its own entry is synthesized.
    /// </summary>
    public static ApiModel FromXml(IReadOnlyList<MemberDoc> docs)
    {
        var types = new Dictionary<string, ApiType>(StringComparer.Ordinal);

        ApiType EnsureType(string typeCref)
        {
            if (types.TryGetValue(typeCref, out var existing)) return existing;
            var parts = CrefSignature.Parse(typeCref);
            var ns = parts.DeclaringName;
            var name = CrefSignature.StripArity(parts.Name);
            var doc = docs.FirstOrDefault(d => d.Cref == typeCref);
            // XML keeps <typeparam> tags in authoring order, and a cref carries no
            // ordinals, so a documented name is only trusted when there is one slot.
            IReadOnlyList<string> typeParams = parts.GenericArity switch
            {
                0 => Array.Empty<string>(),
                1 when doc is { TypeParams.Count: 1 } => new[] { doc.TypeParams[0].Name },
                _ => Enumerable.Range(1, parts.GenericArity).Select(i => "T" + i.ToString(CultureInfo.InvariantCulture)).ToList(),
            };
            var display = typeParams.Count > 0 ? name + "<" + string.Join(", ", typeParams) + ">" : name;
            var t = new ApiType
            {
                DocId = typeCref,
                Namespace = ns,
                Name = name,
                DisplayName = display,
                Kind = ApiTypeKind.Class,
                IsSynthetic = doc is null,
                TypeParameters = typeParams,
            };
            t.Declaration = "public class " + display;
            types[typeCref] = t;
            return t;
        }

        foreach (var d in docs.Where(d => d.Kind == MemberKind.Type))
            EnsureType(d.Cref);

        foreach (var d in docs.Where(d => d.Kind != MemberKind.Type && d.Kind != MemberKind.Unknown))
        {
            var declaring = CrefSignature.DeclaringTypeCref(d.Cref);
            if (declaring is null) continue;
            var type = EnsureType(declaring);
            var parts = CrefSignature.Parse(d.Cref);
            var isCtor = parts.Name is "#ctor" or "_ctor";
            var kind = d.Kind switch
            {
                MemberKind.Property => ApiMemberKind.Property,
                MemberKind.Field => ApiMemberKind.Field,
                MemberKind.Event => ApiMemberKind.Event,
                _ => isCtor ? ApiMemberKind.Constructor : ApiMemberKind.Method,
            };
            var heading = CrefSignature.Format(d, type.TypeParameters.Count > 0 ? type.TypeParameters : null);
            if (isCtor)
            {
                var open = heading.IndexOf('(');
                heading = type.Name + (open >= 0 ? heading[open..] : string.Empty);
            }
            // The same names CrefSignature.Format puts in the heading, so the
            // parameter table and the signature agree.
            IReadOnlyList<string> methodTypeParams = parts.GenericArity switch
            {
                0 => Array.Empty<string>(),
                1 when d.TypeParams is [{ Name.Length: > 0 } only] => new[] { only.Name },
                _ => Enumerable.Range(1, parts.GenericArity).Select(i => "T" + i.ToString(CultureInfo.InvariantCulture)).ToList(),
            };
            var names = d.Params.Select(p => p.Name).ToList();
            var ps = new List<ApiParam>();
            for (int k = 0; k < parts.ParameterTypes.Count; k++)
            {
                var name = k < names.Count && parts.ParameterTypes.Count == names.Count ? names[k] : "arg" + (k + 1).ToString(CultureInfo.InvariantCulture);
                ps.Add(new ApiParam(name, CrefSignature.FormatType(parts.ParameterTypes[k],
                    type.TypeParameters.Count > 0 ? type.TypeParameters : null,
                    methodTypeParams.Count > 0 ? methodTypeParams : null), null, null, string.Empty));
            }
            type.Members.Add(new ApiMember
            {
                DocId = d.Cref,
                Kind = kind,
                Name = isCtor ? type.Name : CrefSignature.StripArity(parts.Name),
                DeclaringType = type,
                Signature = heading,
                ShortSignature = heading,
                Params = ps,
                TypeParameters = isCtor ? Array.Empty<string>() : methodTypeParams,
            });
        }

        return new ApiModel(types.Values, fromMetadata: false);
    }

    // ── Metadata reader ───────────────────────────────────────────────────

    public static ApiModel FromAssembly(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = new MetadataModelReader(pe.GetMetadataReader());
        return new ApiModel(reader.ReadTypes(), fromMetadata: true);
    }
}

// ── Type references decoded from signatures ──────────────────────────────

internal abstract record TRef;

/// <param name="Path">Nested-type chain from outermost to innermost, each with its metadata name (arity suffix kept).</param>
internal sealed record NamedTRef(string Namespace, IReadOnlyList<string> Path, IReadOnlyList<TRef> Args, bool IsValueType, string? Keyword) : TRef
{
    public string FullName => (Namespace.Length > 0 ? Namespace + "." : string.Empty) + string.Join(".", Path);
}

internal sealed record GenParamTRef(bool IsMethod, int Index, string Name, bool IsValueType) : TRef;
internal sealed record ArrayTRef(TRef Element, int Rank) : TRef;
internal sealed record ByRefTRef(TRef Element) : TRef;
internal sealed record PointerTRef(TRef Element) : TRef;
internal sealed record InitOnlyTRef(TRef Inner) : TRef;

internal sealed record GenericNames(IReadOnlyList<string> TypeParams, IReadOnlyList<string> MethodParams,
    IReadOnlyList<bool> TypeParamIsStruct, IReadOnlyList<bool> MethodParamIsStruct);

/// <summary>Turns metadata signatures into <see cref="TRef"/> trees.</summary>
internal sealed class TRefProvider(MetadataReader md) : ISignatureTypeProvider<TRef, GenericNames>
{
    public TRef GetPrimitiveType(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Boolean => Prim("Boolean", "bool", true),
        PrimitiveTypeCode.Byte => Prim("Byte", "byte", true),
        PrimitiveTypeCode.SByte => Prim("SByte", "sbyte", true),
        PrimitiveTypeCode.Char => Prim("Char", "char", true),
        PrimitiveTypeCode.Int16 => Prim("Int16", "short", true),
        PrimitiveTypeCode.UInt16 => Prim("UInt16", "ushort", true),
        PrimitiveTypeCode.Int32 => Prim("Int32", "int", true),
        PrimitiveTypeCode.UInt32 => Prim("UInt32", "uint", true),
        PrimitiveTypeCode.Int64 => Prim("Int64", "long", true),
        PrimitiveTypeCode.UInt64 => Prim("UInt64", "ulong", true),
        PrimitiveTypeCode.Single => Prim("Single", "float", true),
        PrimitiveTypeCode.Double => Prim("Double", "double", true),
        PrimitiveTypeCode.IntPtr => Prim("IntPtr", "nint", true),
        PrimitiveTypeCode.UIntPtr => Prim("UIntPtr", "nuint", true),
        PrimitiveTypeCode.String => Prim("String", "string", false),
        PrimitiveTypeCode.Object => Prim("Object", "object", false),
        PrimitiveTypeCode.Void => Prim("Void", "void", true),
        PrimitiveTypeCode.TypedReference => Prim("TypedReference", null, true),
        _ => Prim(code.ToString(), null, true),
    };

    private static NamedTRef Prim(string name, string? keyword, bool valueType) =>
        new("System", new[] { name }, Array.Empty<TRef>(), valueType, keyword);

    public TRef GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var def = reader.GetTypeDefinition(handle);
        var path = new List<string>();
        var current = def;
        while (true)
        {
            path.Insert(0, reader.GetString(current.Name));
            var outer = current.GetDeclaringType();
            if (outer.IsNil) break;
            current = reader.GetTypeDefinition(outer);
        }
        var ns = reader.GetString(current.Namespace);
        var isValue = rawTypeKind == (byte)SignatureTypeKind.ValueType || IsValueTypeDefinition(reader, def);
        return Keywordize(new NamedTRef(ns, path, Array.Empty<TRef>(), isValue, null));
    }

    public TRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var path = new List<string>();
        var current = reader.GetTypeReference(handle);
        string ns;
        while (true)
        {
            path.Insert(0, reader.GetString(current.Name));
            if (current.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                current = reader.GetTypeReference((TypeReferenceHandle)current.ResolutionScope);
                continue;
            }
            ns = reader.GetString(current.Namespace);
            break;
        }
        var isValue = rawTypeKind == (byte)SignatureTypeKind.ValueType;
        return Keywordize(new NamedTRef(ns, path, Array.Empty<TRef>(), isValue, null));
    }

    private static TRef Keywordize(NamedTRef t)
    {
        if (t.Namespace == "System" && t.Path.Count == 1)
        {
            var kw = t.Path[0] switch
            {
                "String" => "string",
                "Object" => "object",
                "Boolean" => "bool",
                "Int32" => "int",
                "Int64" => "long",
                "Double" => "double",
                "Single" => "float",
                "Byte" => "byte",
                "Char" => "char",
                "Void" => "void",
                "Decimal" => "decimal",
                "Int16" => "short",
                "UInt16" => "ushort",
                "UInt32" => "uint",
                "UInt64" => "ulong",
                "SByte" => "sbyte",
                _ => null,
            };
            if (kw is not null) return t with { Keyword = kw };
        }
        return t;
    }

    internal static bool IsValueTypeDefinition(MetadataReader reader, TypeDefinition def)
    {
        var baseName = BaseTypeName(reader, def.BaseType);
        return baseName is "System.ValueType" or "System.Enum";
    }

    internal static string? BaseTypeName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.IsNil) return null;
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            {
                var r = reader.GetTypeReference((TypeReferenceHandle)handle);
                return reader.GetString(r.Namespace) + "." + reader.GetString(r.Name);
            }
            case HandleKind.TypeDefinition:
            {
                var d = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                return reader.GetString(d.Namespace) + "." + reader.GetString(d.Name);
            }
        }
        return null;
    }

    public TRef GetTypeFromSpecification(MetadataReader reader, GenericNames context, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, context);

    public TRef GetSZArrayType(TRef elementType) => new ArrayTRef(elementType, 1);
    public TRef GetArrayType(TRef elementType, ArrayShape shape) => new ArrayTRef(elementType, shape.Rank);
    public TRef GetByReferenceType(TRef elementType) => new ByRefTRef(elementType);
    public TRef GetPointerType(TRef elementType) => new PointerTRef(elementType);
    public TRef GetPinnedType(TRef elementType) => elementType;
    public TRef GetFunctionPointerType(MethodSignature<TRef> signature) => Prim("IntPtr", "nint", true);

    public TRef GetGenericInstantiation(TRef genericType, ImmutableArray<TRef> typeArguments) =>
        genericType is NamedTRef n ? n with { Args = typeArguments.ToArray() } : genericType;

    public TRef GetGenericTypeParameter(GenericNames context, int index) =>
        new GenParamTRef(false, index,
            index < context.TypeParams.Count ? context.TypeParams[index] : "T" + index.ToString(CultureInfo.InvariantCulture),
            index < context.TypeParamIsStruct.Count && context.TypeParamIsStruct[index]);

    public TRef GetGenericMethodParameter(GenericNames context, int index) =>
        new GenParamTRef(true, index,
            index < context.MethodParams.Count ? context.MethodParams[index] : "T" + index.ToString(CultureInfo.InvariantCulture),
            index < context.MethodParamIsStruct.Count && context.MethodParamIsStruct[index]);

    public TRef GetModifiedType(TRef modifier, TRef unmodifiedType, bool isRequired) =>
        isRequired && modifier is NamedTRef { Namespace: "System.Runtime.CompilerServices", Path: [ "IsExternalInit" ] }
            ? new InitOnlyTRef(unmodifiedType)
            : unmodifiedType;

    public MetadataReader Reader => md;
}

/// <summary>
/// Nullable reference annotations and tuple names for one signature position,
/// consumed in the same preorder the compiler wrote them.
/// </summary>
internal sealed class TypeAnnotations
{
    private readonly byte[] _flags;
    private readonly byte _context;
    private readonly string?[] _tupleNames;
    private int _flag;
    private int _tuple;

    public TypeAnnotations(byte[]? flags, byte context, string?[]? tupleNames)
    {
        _flags = flags ?? Array.Empty<byte>();
        _context = context;
        _tupleNames = tupleNames ?? Array.Empty<string?>();
    }

    public static readonly TypeAnnotations None = new(null, 0, null);

    public bool NextIsNullable()
    {
        byte value;
        if (_flags.Length == 1) value = _flags[0];
        else if (_flag < _flags.Length) value = _flags[_flag];
        else value = _context;
        _flag++;
        return value == 2;
    }

    public string? NextTupleName() => _tuple < _tupleNames.Length ? _tupleNames[_tuple++] : null;
}

/// <summary>Formats <see cref="TRef"/> trees as doc IDs and as C#.</summary>
internal static class TRefFormat
{
    public static string DocId(TRef t) => t switch
    {
        NamedTRef n => NamedDocId(n),
        GenParamTRef g => (g.IsMethod ? "``" : "`") + g.Index.ToString(CultureInfo.InvariantCulture),
        ArrayTRef a => DocId(a.Element) + (a.Rank == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", a.Rank)) + "]"),
        ByRefTRef r => DocId(r.Element) + "@",
        PointerTRef p => DocId(p.Element) + "*",
        InitOnlyTRef i => DocId(i.Inner),
        _ => "?",
    };

    private static string NamedDocId(NamedTRef n)
    {
        var sb = new StringBuilder();
        if (n.Namespace.Length > 0) sb.Append(n.Namespace).Append('.');
        if (n.Args.Count == 0)
        {
            sb.Append(string.Join(".", n.Path));
            return sb.ToString();
        }
        int argIndex = 0;
        for (int i = 0; i < n.Path.Count; i++)
        {
            if (i > 0) sb.Append('.');
            var (name, arity) = SplitArity(n.Path[i]);
            sb.Append(name);
            if (arity > 0)
            {
                sb.Append('{');
                sb.Append(string.Join(",", n.Args.Skip(argIndex).Take(arity).Select(DocId)));
                sb.Append('}');
                argIndex += arity;
            }
        }
        return sb.ToString();
    }

    /// <summary>The <c>T:</c> doc ID of the named type definition behind a reference, or null.</summary>
    public static string? DefinitionDocId(TRef t) => Strip(t) switch
    {
        NamedTRef n when n.Keyword is null => "T:" + n.FullName,
        ArrayTRef a => DefinitionDocId(a.Element),
        _ => null,
    };

    public static TRef Strip(TRef t) => t switch
    {
        ByRefTRef r => Strip(r.Element),
        InitOnlyTRef i => Strip(i.Inner),
        _ => t,
    };

    internal static (string Name, int Arity) SplitArity(string metadataName)
    {
        var tick = metadataName.IndexOf('`');
        if (tick < 0) return (metadataName, 0);
        return (metadataName[..tick], int.TryParse(metadataName[(tick + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : 0);
    }

    /// <summary>C# text for a type, applying nullable annotations in preorder.</summary>
    public static string CSharp(TRef t, TypeAnnotations ann)
    {
        switch (t)
        {
            case InitOnlyTRef i:
                return CSharp(i.Inner, ann);
            case ByRefTRef r:
                return CSharp(r.Element, ann);
            case PointerTRef p:
                return CSharp(p.Element, ann) + "*";
            case ArrayTRef a:
            {
                var nullable = ann.NextIsNullable();
                var elem = CSharp(a.Element, ann);
                return elem + (a.Rank == 1 ? "[]" : "[" + new string(',', a.Rank - 1) + "]") + (nullable ? "?" : string.Empty);
            }
            case GenParamTRef g:
            {
                var nullable = !g.IsValueType && ann.NextIsNullable();
                return g.Name + (nullable ? "?" : string.Empty);
            }
            case NamedTRef n:
            {
                var nullable = !n.IsValueType && ann.NextIsNullable();
                if (n.Keyword is not null) return n.Keyword + (nullable ? "?" : string.Empty);
                if (n.Namespace == "System" && n.Path.Count == 1 && n.Path[0] == "Nullable`1" && n.Args.Count == 1)
                    return CSharp(n.Args[0], ann) + "?";
                if (n.Namespace == "System" && n.Path.Count == 1 && n.Path[0].StartsWith("ValueTuple`", StringComparison.Ordinal) && n.Args.Count is > 1 and < 8)
                {
                    var names = n.Args.Select(_ => ann.NextTupleName()).ToList();
                    var parts = n.Args.Select((a, i) => CSharp(a, ann) + (names[i] is { } nm ? " " + nm : string.Empty));
                    return "(" + string.Join(", ", parts) + ")";
                }
                var sb = new StringBuilder();
                int argIndex = 0;
                for (int i = 0; i < n.Path.Count; i++)
                {
                    if (i > 0) sb.Append('.');
                    var (name, arity) = SplitArity(n.Path[i]);
                    sb.Append(name);
                    if (arity > 0 && n.Args.Count >= argIndex + arity)
                    {
                        sb.Append('<');
                        sb.Append(string.Join(", ", n.Args.Skip(argIndex).Take(arity).Select(a => CSharp(a, ann))));
                        sb.Append('>');
                        argIndex += arity;
                    }
                }
                return sb + (nullable ? "?" : string.Empty);
            }
        }
        return "?";
    }
}

/// <summary>Reads public types and members from metadata into the <see cref="ApiModel"/> shape.</summary>
internal sealed class MetadataModelReader
{
    private readonly MetadataReader _md;
    private readonly TRefProvider _provider;
    private readonly Dictionary<TypeDefinitionHandle, ApiType> _types = new();
    private readonly Dictionary<string, TypeDefinitionHandle> _enumsByFullName = new(StringComparer.Ordinal);

    public MetadataModelReader(MetadataReader md)
    {
        _md = md;
        _provider = new TRefProvider(md);
    }

    public List<ApiType> ReadTypes()
    {
        var visible = _md.TypeDefinitions.Where(h => IsVisible(_md.GetTypeDefinition(h))).ToList();

        // Enums first, so default values of enum parameters can name their field.
        foreach (var h in _md.TypeDefinitions)
        {
            var def = _md.GetTypeDefinition(h);
            if (TRefProvider.BaseTypeName(_md, def.BaseType) == "System.Enum")
                _enumsByFullName[FullMetadataName(def)] = h;
        }

        // Outer types before nested types so DeclaringType is set.
        foreach (var h in visible.OrderBy(h => NestingDepth(_md.GetTypeDefinition(h))))
        {
            var def = _md.GetTypeDefinition(h);
            var name = _md.GetString(def.Name);
            if (name.Contains('<') || name.Contains('$')) continue;
            if (IsCompilerGenerated(def.GetCustomAttributes()) || IsHidden(def.GetCustomAttributes())) continue;
            ApiType? outer = null;
            if (!def.GetDeclaringType().IsNil)
            {
                if (!_types.TryGetValue(def.GetDeclaringType(), out outer)) continue;
            }
            _types[h] = CreateType(h, def, outer);
        }

        foreach (var (h, type) in _types)
            ReadMembers(h, _md.GetTypeDefinition(h), type);

        return _types.Values.ToList();
    }

    private int NestingDepth(TypeDefinition def)
    {
        int depth = 0;
        while (!def.GetDeclaringType().IsNil)
        {
            depth++;
            def = _md.GetTypeDefinition(def.GetDeclaringType());
        }
        return depth;
    }

    private string FullMetadataName(TypeDefinition def)
    {
        var name = _md.GetString(def.Name);
        if (!def.GetDeclaringType().IsNil)
            return FullMetadataName(_md.GetTypeDefinition(def.GetDeclaringType())) + "." + name;
        var ns = _md.GetString(def.Namespace);
        return ns.Length > 0 ? ns + "." + name : name;
    }

    private bool IsVisible(TypeDefinition def)
    {
        var vis = def.Attributes & TypeAttributes.VisibilityMask;
        if (vis == TypeAttributes.Public) return true;
        if (vis is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem)
            return IsVisible(_md.GetTypeDefinition(def.GetDeclaringType()));
        return false;
    }

    private ApiType CreateType(TypeDefinitionHandle h, TypeDefinition def, ApiType? outer)
    {
        var metadataName = _md.GetString(def.Name);
        var (simple, _) = TRefFormat.SplitArity(metadataName);
        var allTypeParams = def.GetGenericParameters().Select(p => _md.GetString(_md.GetGenericParameter(p).Name)).ToList();
        var ownTypeParams = allTypeParams.Skip(outer?.TypeParameters.Count ?? 0).ToList();
        var display = (outer is null ? string.Empty : outer.DisplayName + ".") + simple +
                      (ownTypeParams.Count > 0 ? "<" + string.Join(", ", ownTypeParams) + ">" : string.Empty);

        var baseName = TRefProvider.BaseTypeName(_md, def.BaseType);
        var methods = def.GetMethods().Select(m => _md.GetString(_md.GetMethodDefinition(m).Name)).ToHashSet(StringComparer.Ordinal);
        bool isInterface = (def.Attributes & TypeAttributes.Interface) != 0;
        bool isStatic = (def.Attributes & TypeAttributes.Abstract) != 0 && (def.Attributes & TypeAttributes.Sealed) != 0 && !isInterface;
        ApiTypeKind kind = isInterface ? ApiTypeKind.Interface
            : baseName == "System.Enum" ? ApiTypeKind.Enum
            : baseName == "System.MulticastDelegate" ? ApiTypeKind.Delegate
            : baseName == "System.ValueType" ? (methods.Contains("PrintMembers") ? ApiTypeKind.RecordStruct : ApiTypeKind.Struct)
            : isStatic ? ApiTypeKind.StaticClass
            : methods.Contains("<Clone>$") ? ApiTypeKind.Record
            : ApiTypeKind.Class;

        var docId = "T:" + FullMetadataName(def);
        var type = new ApiType
        {
            DocId = docId,
            Namespace = outer?.Namespace ?? _md.GetString(def.Namespace),
            Name = simple,
            DisplayName = display,
            Kind = kind,
            DeclaringType = outer,
            TypeParameters = allTypeParams,
            ObsoleteMessage = ObsoleteMessage(def.GetCustomAttributes()),
        };

        if (kind is not (ApiTypeKind.Enum or ApiTypeKind.Delegate or ApiTypeKind.Interface or ApiTypeKind.Struct or ApiTypeKind.RecordStruct)
            && !def.BaseType.IsNil && baseName is not ("System.Object" or null))
        {
            var ctx = Context(def, default);
            var baseRef = DecodeHandle(def.BaseType, ctx);
            type.BaseDocId = TRefFormat.DefinitionDocId(baseRef);
        }
        return type;
    }

    private GenericNames Context(TypeDefinition def, MethodDefinition? method)
    {
        var tps = def.GetGenericParameters().Select(p => _md.GetGenericParameter(p)).ToList();
        var mps = method?.GetGenericParameters().Select(p => _md.GetGenericParameter(p)).ToList() ?? new List<GenericParameter>();
        return new GenericNames(
            tps.Select(p => _md.GetString(p.Name)).ToList(),
            mps.Select(p => _md.GetString(p.Name)).ToList(),
            tps.Select(p => (p.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0).ToList(),
            mps.Select(p => (p.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0).ToList());
    }

    private TRef DecodeHandle(EntityHandle handle, GenericNames ctx) => handle.Kind switch
    {
        HandleKind.TypeDefinition => _provider.GetTypeFromDefinition(_md, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => _provider.GetTypeFromReference(_md, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => _provider.GetTypeFromSpecification(_md, ctx, (TypeSpecificationHandle)handle, 0),
        _ => new NamedTRef(string.Empty, new[] { "?" }, Array.Empty<TRef>(), false, null),
    };

    // ── Members ───────────────────────────────────────────────────────────

    private void ReadMembers(TypeDefinitionHandle h, TypeDefinition def, ApiType type)
    {
        var typeContext = NullableContext(def.GetCustomAttributes(), h);
        var ctx = Context(def, null);
        var typeStem = type.DocId[2..];
        bool sealedType = (def.Attributes & TypeAttributes.Sealed) != 0;
        bool isRecord = type.Kind is ApiTypeKind.Record or ApiTypeKind.RecordStruct;

        var accessors = new HashSet<MethodDefinitionHandle>();
        foreach (var ph in def.GetProperties())
        {
            var a = _md.GetPropertyDefinition(ph).GetAccessors();
            if (!a.Getter.IsNil) accessors.Add(a.Getter);
            if (!a.Setter.IsNil) accessors.Add(a.Setter);
        }
        foreach (var eh in def.GetEvents())
        {
            var a = _md.GetEventDefinition(eh).GetAccessors();
            if (!a.Adder.IsNil) accessors.Add(a.Adder);
            if (!a.Remover.IsNil) accessors.Add(a.Remover);
            if (!a.Raiser.IsNil) accessors.Add(a.Raiser);
        }

        if (type.Kind == ApiTypeKind.Enum)
        {
            foreach (var fh in def.GetFields())
            {
                var f = _md.GetFieldDefinition(fh);
                var fname = _md.GetString(f.Name);
                if ((f.Attributes & FieldAttributes.Static) == 0 || fname == "value__") continue;
                type.EnumValues.Add(new ApiMember
                {
                    DocId = "F:" + typeStem + "." + fname,
                    Kind = ApiMemberKind.Field,
                    Name = fname,
                    DeclaringType = type,
                    Signature = fname,
                    ShortSignature = fname,
                    ConstantValue = ConstantText(f.GetDefaultValue(), null),
                });
            }
            type.Declaration = "public enum " + type.DisplayName;
            return;
        }

        if (type.Kind == ApiTypeKind.Delegate)
        {
            var invoke = def.GetMethods().Select(m => (h: m, d: _md.GetMethodDefinition(m)))
                .FirstOrDefault(m => _md.GetString(m.d.Name) == "Invoke");
            if (!invoke.h.IsNil)
            {
                var sig = BuildMethod(type, def, invoke.d, typeContext, ctx, typeStem, asDelegate: true);
                type.Declaration = sig.Signature;
            }
            else
            {
                type.Declaration = "public delegate " + type.DisplayName;
            }
            return;
        }

        foreach (var mh in def.GetMethods())
        {
            if (accessors.Contains(mh)) continue;
            var m = _md.GetMethodDefinition(mh);
            if (!IsVisibleMember(m.Attributes & MethodAttributes.MemberAccessMask, sealedType)) continue;
            var name = _md.GetString(m.Name);
            if (name == ".cctor") continue;
            if (name.Contains('<') || name.Contains('$')) continue;
            if (IsCompilerGenerated(m.GetCustomAttributes()) || IsHidden(m.GetCustomAttributes())) continue;
            if (isRecord && name is "PrintMembers" or "Deconstruct" or "Equals" or "GetHashCode" or "ToString" or "op_Equality" or "op_Inequality") continue;
            if (name is "Finalize") continue;
            var member = BuildMethod(type, def, m, typeContext, ctx, typeStem, asDelegate: false);
            // A record's copy constructor is protected plumbing, not something to call.
            if (member.Kind == ApiMemberKind.Constructor && member.Params.Count == 1 && member.Params[0].TypeDocId == type.DocId) continue;
            type.Members.Add(member);
        }

        foreach (var ph in def.GetProperties())
        {
            var p = _md.GetPropertyDefinition(ph);
            var name = _md.GetString(p.Name);
            if (name.Contains('<') || name.Contains('$') || name.Contains('.')) continue;
            if (isRecord && name == "EqualityContract") continue;
            var acc = p.GetAccessors();
            var getter = acc.Getter.IsNil ? (MethodDefinition?)null : _md.GetMethodDefinition(acc.Getter);
            var setter = acc.Setter.IsNil ? (MethodDefinition?)null : _md.GetMethodDefinition(acc.Setter);
            bool getVisible = getter is { } g && IsVisibleMember(g.Attributes & MethodAttributes.MemberAccessMask, sealedType);
            bool setVisible = setter is { } s && IsVisibleMember(s.Attributes & MethodAttributes.MemberAccessMask, sealedType);
            if (!getVisible && !setVisible) continue;
            if (IsHidden(p.GetCustomAttributes())) continue;
            type.Members.Add(BuildProperty(type, p, getter, setter, getVisible, setVisible, typeContext, ctx, typeStem));
        }

        foreach (var eh in def.GetEvents())
        {
            var e = _md.GetEventDefinition(eh);
            var acc = e.GetAccessors();
            if (acc.Adder.IsNil) continue;
            var adder = _md.GetMethodDefinition(acc.Adder);
            if (!IsVisibleMember(adder.Attributes & MethodAttributes.MemberAccessMask, sealedType)) continue;
            if (IsHidden(e.GetCustomAttributes())) continue;
            var name = _md.GetString(e.Name);
            var ann = Annotations(e.GetCustomAttributes(), NullableContext(adder.GetCustomAttributes(), null) ?? typeContext);
            var t = DecodeHandle(e.Type, ctx);
            var typeText = TRefFormat.CSharp(t, ann);
            var isStatic = (adder.Attributes & MethodAttributes.Static) != 0;
            type.Members.Add(new ApiMember
            {
                DocId = "E:" + typeStem + "." + name,
                Kind = ApiMemberKind.Event,
                Name = name,
                DeclaringType = type,
                Signature = Access(adder.Attributes) + (isStatic ? "static " : string.Empty) + "event " + typeText + " " + name,
                ShortSignature = name,
                ReturnType = typeText,
                ReturnDocId = TRefFormat.DefinitionDocId(t),
                IsStatic = isStatic,
                ObsoleteMessage = ObsoleteMessage(e.GetCustomAttributes()),
            });
        }

        foreach (var fh in def.GetFields())
        {
            var f = _md.GetFieldDefinition(fh);
            var name = _md.GetString(f.Name);
            if (name.Contains('<') || name.Contains('$')) continue;
            if (!IsVisibleField(f.Attributes & FieldAttributes.FieldAccessMask, sealedType)) continue;
            if (IsCompilerGenerated(f.GetCustomAttributes()) || IsHidden(f.GetCustomAttributes())) continue;
            var t = f.DecodeSignature(_provider, ctx);
            var ann = Annotations(f.GetCustomAttributes(), typeContext ?? 0);
            var typeText = TRefFormat.CSharp(t, ann);
            bool isConst = (f.Attributes & FieldAttributes.Literal) != 0;
            bool isStatic = (f.Attributes & FieldAttributes.Static) != 0;
            bool isReadOnly = (f.Attributes & FieldAttributes.InitOnly) != 0;
            var constant = isConst ? ConstantText(f.GetDefaultValue(), t) : null;
            var mods = isConst ? "const " : (isStatic ? "static " : string.Empty) + (isReadOnly ? "readonly " : string.Empty);
            type.Members.Add(new ApiMember
            {
                DocId = "F:" + typeStem + "." + name,
                Kind = ApiMemberKind.Field,
                Name = name,
                DeclaringType = type,
                Signature = FieldAccess(f.Attributes) + mods + typeText + " " + name + (constant is not null ? " = " + constant : string.Empty),
                ShortSignature = name,
                ReturnType = typeText,
                ReturnDocId = TRefFormat.DefinitionDocId(t),
                IsStatic = isStatic || isConst,
                ConstantValue = constant,
                ObsoleteMessage = ObsoleteMessage(f.GetCustomAttributes()),
            });
        }

        // Primary constructor of a positional record: the widest public constructor
        // whose parameters all name a property.
        if (isRecord)
        {
            var propNames = type.Members.Where(m => m.Kind == ApiMemberKind.Property).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            type.PrimaryConstructor = type.Members
                .Where(m => m.Kind == ApiMemberKind.Constructor && m.Params.Count > 0 && m.Params.All(p => propNames.Contains(p.Name)))
                .OrderByDescending(m => m.Params.Count)
                .ThenBy(m => m.DocId, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        type.Declaration = BuildTypeDeclaration(h, def, type, ctx);
    }

    private string BuildTypeDeclaration(TypeDefinitionHandle h, TypeDefinition def, ApiType type, GenericNames ctx)
    {
        var sb = new StringBuilder("public ");
        bool isAbstract = (def.Attributes & TypeAttributes.Abstract) != 0;
        bool isSealed = (def.Attributes & TypeAttributes.Sealed) != 0;
        switch (type.Kind)
        {
            case ApiTypeKind.Interface: sb.Append("interface "); break;
            case ApiTypeKind.StaticClass: sb.Append("static class "); break;
            case ApiTypeKind.Struct: sb.Append(IsReadOnlyStruct(def) ? "readonly struct " : "struct "); break;
            case ApiTypeKind.RecordStruct: sb.Append(IsReadOnlyStruct(def) ? "readonly record struct " : "record struct "); break;
            case ApiTypeKind.Record:
                if (isAbstract) sb.Append("abstract ");
                else if (isSealed) sb.Append("sealed ");
                sb.Append("record ");
                break;
            default:
                if (isAbstract) sb.Append("abstract ");
                else if (isSealed) sb.Append("sealed ");
                sb.Append("class ");
                break;
        }

        var ownTypeParams = type.TypeParameters.Skip(type.DeclaringType?.TypeParameters.Count ?? 0).ToList();
        var (simple, _) = TRefFormat.SplitArity(_md.GetString(def.Name));
        sb.Append(simple);
        if (ownTypeParams.Count > 0) sb.Append('<').Append(string.Join(", ", ownTypeParams)).Append('>');

        if (type.PrimaryConstructor is { } primary)
            sb.Append('(').Append(string.Join(", ", primary.Params.Select(FormatParam))).Append(')');

        var bases = new List<string>();
        if (!def.BaseType.IsNil && type.BaseDocId is not null)
        {
            var baseRef = DecodeHandle(def.BaseType, ctx);
            var ann = Annotations(def.GetCustomAttributes(), 0);
            bases.Add(TRefFormat.CSharp(baseRef, ann));
        }
        foreach (var ih in def.GetInterfaceImplementations())
        {
            var impl = _md.GetInterfaceImplementation(ih);
            if (impl.Interface.Kind == HandleKind.TypeDefinition && !IsVisible(_md.GetTypeDefinition((TypeDefinitionHandle)impl.Interface))) continue;
            var iref = DecodeHandle(impl.Interface, ctx);
            if (iref is NamedTRef { Namespace: "System", Path: [ "IEquatable`1" ] } eq && eq.Args.Count == 1 && TRefFormat.DefinitionDocId(eq.Args[0]) == type.DocId) continue;

            bases.Add(TRefFormat.CSharp(iref, TypeAnnotations.None));
        }
        if (bases.Count > 0) sb.Append(" : ").Append(string.Join(", ", bases));
        sb.Append(Constraints(def.GetGenericParameters().Skip(type.DeclaringType?.TypeParameters.Count ?? 0), ctx));
        return sb.ToString();
    }

    private bool IsReadOnlyStruct(TypeDefinition def) =>
        def.GetCustomAttributes().Any(ca => AttributeName(_md.GetCustomAttribute(ca)) == "System.Runtime.CompilerServices.IsReadOnlyAttribute");

    private string Constraints(IEnumerable<GenericParameterHandle> parameters, GenericNames ctx)
    {
        var sb = new StringBuilder();
        foreach (var gh in parameters)
        {
            var gp = _md.GetGenericParameter(gh);
            var items = new List<string>();
            var attrs = gp.Attributes & GenericParameterAttributes.SpecialConstraintMask;
            bool isStruct = (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            if ((attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0) items.Add("class");
            if (isStruct) items.Add("struct");
            foreach (var ch in gp.GetConstraints())
            {
                var c = _md.GetGenericParameterConstraint(ch);
                var cref = DecodeHandle(c.Type, ctx);
                if (isStruct && cref is NamedTRef { Namespace: "System", Path: [ "ValueType" ] }) continue;
                items.Add(TRefFormat.CSharp(cref, TypeAnnotations.None));
            }
            if ((attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0 && !isStruct) items.Add("new()");
            if (items.Count > 0)
                sb.Append(" where ").Append(_md.GetString(gp.Name)).Append(" : ").Append(string.Join(", ", items));
        }
        return sb.ToString();
    }

    private List<string> ConstraintDocIds(GenericParameterHandle gh, GenericNames ctx)
    {
        var gp = _md.GetGenericParameter(gh);
        var result = new List<string>();
        foreach (var ch in gp.GetConstraints())
        {
            var c = _md.GetGenericParameterConstraint(ch);
            if (TRefFormat.DefinitionDocId(DecodeHandle(c.Type, ctx)) is { } id) result.Add(id);
        }
        return result;
    }

    private ApiMember BuildMethod(ApiType type, TypeDefinition def, MethodDefinition m, byte? typeContext, GenericNames typeCtx, string typeStem, bool asDelegate)
    {
        var name = _md.GetString(m.Name);
        var ctx = Context(def, m);
        var sig = m.DecodeSignature(_provider, ctx);
        var context = NullableContext(m.GetCustomAttributes(), null) ?? typeContext ?? 0;
        var parameters = m.GetParameters().Select(ph => _md.GetParameter(ph)).ToList();
        var returnParam = parameters.FirstOrDefault(p => p.SequenceNumber == 0);
        var returnAnn = parameters.Any(p => p.SequenceNumber == 0)
            ? Annotations(returnParam.GetCustomAttributes(), context)
            : new TypeAnnotations(null, context, null);
        var returnText = TRefFormat.CSharp(sig.ReturnType, returnAnn);
        if (sig.ReturnType is ByRefTRef) returnText = "ref " + returnText;

        bool isCtor = name == ".ctor";
        bool isStatic = (m.Attributes & MethodAttributes.Static) != 0;
        bool isExtension = m.GetCustomAttributes().Any(ca => AttributeName(_md.GetCustomAttribute(ca)) == "System.Runtime.CompilerServices.ExtensionAttribute");
        bool isOperator = name.StartsWith("op_", StringComparison.Ordinal) && (m.Attributes & MethodAttributes.SpecialName) != 0;
        var methodTypeParams = ctx.MethodParams;

        var ps = new List<ApiParam>();
        for (int i = 0; i < sig.ParameterTypes.Length; i++)
        {
            var meta = parameters.FirstOrDefault(p => p.SequenceNumber == i + 1);
            bool hasMeta = parameters.Any(p => p.SequenceNumber == i + 1);
            var pname = hasMeta ? _md.GetString(meta.Name) : "arg" + (i + 1).ToString(CultureInfo.InvariantCulture);
            var pt = sig.ParameterTypes[i];
            var ann = hasMeta ? Annotations(meta.GetCustomAttributes(), context) : new TypeAnnotations(null, context, null);
            var ptext = TRefFormat.CSharp(pt, ann);
            string modifier = string.Empty;
            if (pt is ByRefTRef)
            {
                if (hasMeta && (meta.Attributes & ParameterAttributes.Out) != 0) modifier = "out";
                else if (hasMeta && meta.GetCustomAttributes().Any(ca => AttributeName(_md.GetCustomAttribute(ca)) is "System.Runtime.CompilerServices.IsReadOnlyAttribute" or "System.Runtime.CompilerServices.RequiresLocationAttribute")) modifier = "in";
                else modifier = "ref";
            }
            if (i == 0 && isExtension) modifier = "this";
            if (hasMeta && meta.GetCustomAttributes().Any(ca => AttributeName(_md.GetCustomAttribute(ca)) is "System.ParamArrayAttribute" or "System.Runtime.CompilerServices.ParamCollectionAttribute"))
                modifier = "params";
            string? defaultText = null;
            if (hasMeta && (meta.Attributes & ParameterAttributes.HasDefault) != 0)
                defaultText = ConstantText(meta.GetDefaultValue(), pt) ?? "default";
            else if (hasMeta && (meta.Attributes & ParameterAttributes.Optional) != 0)
                defaultText = "default";
            ps.Add(new ApiParam(pname, ptext, TRefFormat.DefinitionDocId(pt), defaultText, modifier));
        }

        var paramDocIds = sig.ParameterTypes.Select(TRefFormat.DocId).ToList();
        string docName = isCtor ? "#ctor" : name;
        if (methodTypeParams.Count > 0) docName += "``" + methodTypeParams.Count.ToString(CultureInfo.InvariantCulture);
        var docId = "M:" + typeStem + "." + docName + (paramDocIds.Count > 0 ? "(" + string.Join(",", paramDocIds) + ")" : string.Empty);
        if (name is "op_Implicit" or "op_Explicit") docId += "~" + TRefFormat.DocId(sig.ReturnType);

        var typeParamText = methodTypeParams.Count > 0 ? "<" + string.Join(", ", methodTypeParams) + ">" : string.Empty;
        var paramList = string.Join(", ", ps.Select(FormatParam));
        var shortParams = string.Join(", ", ps.Select(p => p.Type));
        var constraints = Constraints(m.GetGenericParameters(), ctx);
        string signature, shortSig, displayName;
        ApiMemberKind kind;
        if (asDelegate)
        {
            kind = ApiMemberKind.Method;
            var ownTypeParams = type.TypeParameters.Skip(type.DeclaringType?.TypeParameters.Count ?? 0).ToList();
            var tp = ownTypeParams.Count > 0 ? "<" + string.Join(", ", ownTypeParams) + ">" : string.Empty;
            signature = "public delegate " + returnText + " " + type.Name + tp + "(" + paramList + ")" + Constraints(def.GetGenericParameters(), typeCtx);
            shortSig = type.Name;
            displayName = type.Name;
        }
        else if (isCtor)
        {
            kind = ApiMemberKind.Constructor;
            signature = Access(m.Attributes) + type.Name + "(" + paramList + ")";
            shortSig = type.Name + "(" + shortParams + ")";
            displayName = type.Name;
        }
        else if (isOperator)
        {
            kind = ApiMemberKind.Operator;
            var op = OperatorToken(name);
            if (name is "op_Implicit" or "op_Explicit")
            {
                signature = "public static " + (name == "op_Implicit" ? "implicit" : "explicit") + " operator " + returnText + "(" + paramList + ")";
                shortSig = (name == "op_Implicit" ? "implicit" : "explicit") + " operator " + returnText + "(" + shortParams + ")";
            }
            else
            {
                signature = "public static " + returnText + " operator " + op + "(" + paramList + ")";
                shortSig = "operator " + op + "(" + shortParams + ")";
            }
            displayName = name;
        }
        else
        {
            kind = ApiMemberKind.Method;
            signature = Access(m.Attributes) + MethodModifiers(m.Attributes, def) + returnText + " " + name + typeParamText + "(" + paramList + ")" + constraints;
            shortSig = name + typeParamText + "(" + shortParams + ")";
            displayName = name;
        }

        var extended = new List<string>();
        if (isExtension && sig.ParameterTypes.Length > 0)
        {
            var first = TRefFormat.Strip(sig.ParameterTypes[0]);
            if (first is GenParamTRef { IsMethod: true } g)
            {
                var gph = m.GetGenericParameters()[g.Index];
                extended.AddRange(ConstraintDocIds(gph, ctx));
            }
            else if (TRefFormat.DefinitionDocId(first) is { } id)
            {
                extended.Add(id);
            }
        }

        return new ApiMember
        {
            DocId = docId,
            Kind = kind,
            Name = displayName,
            DeclaringType = type,
            Signature = signature,
            ShortSignature = shortSig,
            Params = ps,
            TypeParameters = methodTypeParams,
            ReturnType = isCtor ? null : returnText,
            ReturnDocId = isCtor ? null : TRefFormat.DefinitionDocId(sig.ReturnType),
            IsStatic = isStatic,
            IsExtension = isExtension,
            ExtendedTypeDocIds = extended,
            ObsoleteMessage = ObsoleteMessage(m.GetCustomAttributes()),
        };
    }

    private ApiMember BuildProperty(ApiType type, PropertyDefinition p, MethodDefinition? getter, MethodDefinition? setter,
        bool getVisible, bool setVisible, byte? typeContext, GenericNames ctx, string typeStem)
    {
        var name = _md.GetString(p.Name);
        var sig = p.DecodeSignature(_provider, ctx);
        var accessor = getVisible ? getter!.Value : setter!.Value;
        var context = NullableContext(accessor.GetCustomAttributes(), null) ?? typeContext ?? 0;
        var ann = Annotations(p.GetCustomAttributes(), context);
        var typeText = TRefFormat.CSharp(sig.ReturnType, ann);
        bool isStatic = (accessor.Attributes & MethodAttributes.Static) != 0;
        bool isInit = false;
        if (setter is { } s && setVisible)
        {
            var setSig = s.DecodeSignature(_provider, ctx);
            isInit = setSig.ReturnType is InitOnlyTRef;
        }
        var accessorsText = (getVisible ? "get" : string.Empty) +
                            (setVisible ? (getVisible ? ", " : string.Empty) + (isInit ? "init" : "set") : string.Empty);
        var ps = sig.ParameterTypes.Select((t, i) => new ApiParam("index" + (i == 0 ? string.Empty : (i + 1).ToString(CultureInfo.InvariantCulture)),
            TRefFormat.CSharp(t, TypeAnnotations.None), TRefFormat.DefinitionDocId(t), null, string.Empty)).ToList();
        var indexer = sig.ParameterTypes.Length > 0;
        var displayName = indexer ? "this[" + string.Join(", ", ps.Select(x => x.Type)) + "]" : name;
        var docId = "P:" + typeStem + "." + name + (indexer ? "(" + string.Join(",", sig.ParameterTypes.Select(TRefFormat.DocId)) + ")" : string.Empty);
        return new ApiMember
        {
            DocId = docId,
            Kind = ApiMemberKind.Property,
            Name = indexer ? "Item" : name,
            DeclaringType = type,
            Signature = Access(accessor.Attributes) + MethodModifiers(accessor.Attributes, null) + typeText + " " + displayName + "  // " + accessorsText,
            ShortSignature = displayName,
            Params = ps,
            ReturnType = typeText,
            ReturnDocId = TRefFormat.DefinitionDocId(sig.ReturnType),
            IsStatic = isStatic,
            ObsoleteMessage = ObsoleteMessage(p.GetCustomAttributes()),
        };
    }

    private static string FormatParam(ApiParam p) =>
        (p.Modifier.Length > 0 ? p.Modifier + " " : string.Empty) + p.Type + " " + p.Name + (p.Default is not null ? " = " + p.Default : string.Empty);

    private static bool IsVisibleMember(MethodAttributes access, bool sealedType) =>
        access == MethodAttributes.Public || (!sealedType && access is MethodAttributes.Family or MethodAttributes.FamORAssem);

    private static bool IsVisibleField(FieldAttributes access, bool sealedType) =>
        access == FieldAttributes.Public || (!sealedType && access is FieldAttributes.Family or FieldAttributes.FamORAssem);

    private static string Access(MethodAttributes attrs) =>
        (attrs & MethodAttributes.MemberAccessMask) == MethodAttributes.Public ? "public " : "protected ";

    private static string FieldAccess(FieldAttributes attrs) =>
        (attrs & FieldAttributes.FieldAccessMask) == FieldAttributes.Public ? "public " : "protected ";

    private static string MethodModifiers(MethodAttributes attrs, TypeDefinition? def)
    {
        if ((attrs & MethodAttributes.Static) != 0) return "static ";
        bool isInterface = def is { } d && (d.Attributes & TypeAttributes.Interface) != 0;
        if (isInterface) return string.Empty;
        if ((attrs & MethodAttributes.Abstract) != 0) return "abstract ";
        if ((attrs & MethodAttributes.Virtual) != 0)
        {
            if ((attrs & MethodAttributes.NewSlot) != 0)
                return (attrs & MethodAttributes.Final) != 0 ? string.Empty : "virtual ";
            return (attrs & MethodAttributes.Final) != 0 ? "sealed override " : "override ";
        }
        return string.Empty;
    }

    private static string OperatorToken(string name) => name switch
    {
        "op_Addition" => "+",
        "op_Subtraction" => "-",
        "op_Multiply" => "*",
        "op_Division" => "/",
        "op_Modulus" => "%",
        "op_Equality" => "==",
        "op_Inequality" => "!=",
        "op_LessThan" => "<",
        "op_GreaterThan" => ">",
        "op_LessThanOrEqual" => "<=",
        "op_GreaterThanOrEqual" => ">=",
        "op_UnaryNegation" => "-",
        "op_UnaryPlus" => "+",
        "op_LogicalNot" => "!",
        "op_BitwiseOr" => "|",
        "op_BitwiseAnd" => "&",
        "op_ExclusiveOr" => "^",
        "op_True" => "true",
        "op_False" => "false",
        _ => name.StartsWith("op_", StringComparison.Ordinal) ? name[3..] : name,
    };

    // ── Constants ─────────────────────────────────────────────────────────

    private string? ConstantText(ConstantHandle handle, TRef? declaredType)
    {
        if (handle.IsNil) return null;
        var c = _md.GetConstant(handle);
        var blob = _md.GetBlobReader(c.Value);
        object? value = c.TypeCode switch
        {
            ConstantTypeCode.Boolean => blob.ReadBoolean(),
            ConstantTypeCode.Char => blob.ReadChar(),
            ConstantTypeCode.SByte => blob.ReadSByte(),
            ConstantTypeCode.Byte => blob.ReadByte(),
            ConstantTypeCode.Int16 => blob.ReadInt16(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
            ConstantTypeCode.Int32 => blob.ReadInt32(),
            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
            ConstantTypeCode.Int64 => blob.ReadInt64(),
            ConstantTypeCode.UInt64 => blob.ReadUInt64(),
            ConstantTypeCode.Single => blob.ReadSingle(),
            ConstantTypeCode.Double => blob.ReadDouble(),
            ConstantTypeCode.String => blob.Length == 0 ? string.Empty : blob.ReadUTF16(blob.Length),
            _ => null,
        };
        var target = declaredType is null ? null : TRefFormat.Strip(declaredType);
        if (value is null)
        {
            if (c.TypeCode == ConstantTypeCode.String) return "\"\"";
            // `CancellationToken ct = default` is stored as a null class constant.
            return target is NamedTRef { IsValueType: true } vt && !(vt.Namespace == "System" && vt.Path is [ "Nullable`1" ])
                ? "default" : "null";
        }

        if (target is NamedTRef { Namespace: "System", Path: [ "Nullable`1" ] } nullable && nullable.Args.Count == 1)
            target = nullable.Args[0];
        if (target is NamedTRef { Keyword: null, IsValueType: true } enumType && value is not (bool or string or char or float or double))
            return EnumText(enumType, Convert.ToInt64(value, CultureInfo.InvariantCulture));

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"",
            char ch => "'" + ch + "'",
            double d => DoubleText(d, "double"),
            float f => DoubleText(f, "float") + (float.IsFinite(f) ? "f" : string.Empty),
            IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };
    }

    private static string DoubleText(double d, string keyword)
    {
        if (double.IsNaN(d)) return keyword + ".NaN";
        if (double.IsPositiveInfinity(d)) return keyword + ".PositiveInfinity";
        if (double.IsNegativeInfinity(d)) return keyword + ".NegativeInfinity";
        if (d == double.MaxValue) return "double.MaxValue";
        if (d == double.MinValue) return "double.MinValue";
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    private string EnumText(NamedTRef enumType, long value)
    {
        var shortName = TRefFormat.SplitArity(enumType.Path[^1]).Name;
        if (_enumsByFullName.TryGetValue(enumType.FullName, out var h))
        {
            var def = _md.GetTypeDefinition(h);
            foreach (var fh in def.GetFields())
            {
                var f = _md.GetFieldDefinition(fh);
                if ((f.Attributes & FieldAttributes.Literal) == 0) continue;
                var c = f.GetDefaultValue();
                if (c.IsNil) continue;
                var constant = _md.GetConstant(c);
                var reader = _md.GetBlobReader(constant.Value);
                long v = constant.TypeCode switch
                {
                    ConstantTypeCode.Byte => reader.ReadByte(),
                    ConstantTypeCode.SByte => reader.ReadSByte(),
                    ConstantTypeCode.Int16 => reader.ReadInt16(),
                    ConstantTypeCode.UInt16 => reader.ReadUInt16(),
                    ConstantTypeCode.Int32 => reader.ReadInt32(),
                    ConstantTypeCode.UInt32 => reader.ReadUInt32(),
                    ConstantTypeCode.Int64 => reader.ReadInt64(),
                    ConstantTypeCode.UInt64 => (long)reader.ReadUInt64(),
                    _ => long.MinValue,
                };
                if (v == value) return shortName + "." + _md.GetString(f.Name);
            }
        }
        if (value == 0) return "default";
        return "(" + shortName + ")" + value.ToString(CultureInfo.InvariantCulture);
    }

    // ── Attributes ────────────────────────────────────────────────────────

    private string AttributeName(CustomAttribute ca)
    {
        EntityHandle parent;
        switch (ca.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                parent = _md.GetMemberReference((MemberReferenceHandle)ca.Constructor).Parent;
                break;
            case HandleKind.MethodDefinition:
                parent = _md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor).GetDeclaringType();
                break;
            default:
                return string.Empty;
        }
        return parent.Kind switch
        {
            HandleKind.TypeReference => _md.GetString(_md.GetTypeReference((TypeReferenceHandle)parent).Namespace) + "." +
                                        _md.GetString(_md.GetTypeReference((TypeReferenceHandle)parent).Name),
            HandleKind.TypeDefinition => _md.GetString(_md.GetTypeDefinition((TypeDefinitionHandle)parent).Namespace) + "." +
                                         _md.GetString(_md.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
            _ => string.Empty,
        };
    }

    private CustomAttribute? FindAttribute(CustomAttributeHandleCollection attrs, string fullName)
    {
        foreach (var h in attrs)
        {
            var ca = _md.GetCustomAttribute(h);
            if (AttributeName(ca) == fullName) return ca;
        }
        return null;
    }

    private bool IsCompilerGenerated(CustomAttributeHandleCollection attrs) =>
        FindAttribute(attrs, "System.Runtime.CompilerServices.CompilerGeneratedAttribute") is not null;

    private bool IsHidden(CustomAttributeHandleCollection attrs)
    {
        if (FindAttribute(attrs, "System.ComponentModel.EditorBrowsableAttribute") is not { } ca) return false;
        var blob = _md.GetBlobReader(ca.Value);
        if (blob.Length < 6 || blob.ReadUInt16() != 1) return false;
        return blob.ReadInt32() == 1; // EditorBrowsableState.Never
    }

    private string? ObsoleteMessage(CustomAttributeHandleCollection attrs)
    {
        if (FindAttribute(attrs, "System.ObsoleteAttribute") is not { } ca) return null;
        var blob = _md.GetBlobReader(ca.Value);
        if (blob.Length < 2 || blob.ReadUInt16() != 1) return string.Empty;
        if (blob.RemainingBytes <= 2) return string.Empty;
        try { return blob.ReadSerializedString() ?? string.Empty; }
        catch (BadImageFormatException) { return string.Empty; }
    }

    private byte? NullableContext(CustomAttributeHandleCollection attrs, TypeDefinitionHandle? owner)
    {
        if (FindAttribute(attrs, "System.Runtime.CompilerServices.NullableContextAttribute") is { } ca)
        {
            var blob = _md.GetBlobReader(ca.Value);
            if (blob.Length >= 3 && blob.ReadUInt16() == 1) return blob.ReadByte();
        }
        if (owner is { } h)
        {
            var def = _md.GetTypeDefinition(h);
            if (!def.GetDeclaringType().IsNil)
            {
                var outer = def.GetDeclaringType();
                return NullableContext(_md.GetTypeDefinition(outer).GetCustomAttributes(), outer);
            }
        }
        return null;
    }

    private TypeAnnotations Annotations(CustomAttributeHandleCollection attrs, byte? context)
    {
        byte[]? flags = null;
        if (FindAttribute(attrs, "System.Runtime.CompilerServices.NullableAttribute") is { } ca)
        {
            var blob = _md.GetBlobReader(ca.Value);
            if (blob.Length >= 3 && blob.ReadUInt16() == 1)
            {
                if (blob.Length == 5)
                {
                    flags = new[] { blob.ReadByte() };
                }
                else
                {
                    var count = blob.ReadInt32();
                    if (count >= 0 && count <= blob.RemainingBytes)
                        flags = blob.ReadBytes(count);
                }
            }
        }

        string?[]? tupleNames = null;
        if (FindAttribute(attrs, "System.Runtime.CompilerServices.TupleElementNamesAttribute") is { } tn)
        {
            var blob = _md.GetBlobReader(tn.Value);
            if (blob.Length >= 6 && blob.ReadUInt16() == 1)
            {
                var count = blob.ReadInt32();
                if (count >= 0)
                {
                    tupleNames = new string?[count];
                    for (int i = 0; i < count; i++) tupleNames[i] = blob.ReadSerializedString();
                }
            }
        }
        return new TypeAnnotations(flags, context ?? 0, tupleNames);
    }
}
