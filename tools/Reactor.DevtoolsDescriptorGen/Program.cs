// Emits src/Reactor.Devtools/ILLink.Descriptors.xml — the trimmer descriptor that
// keeps WinUI's DependencyProperty statics reflectable under NativeAOT so the
// devtools `properties` / `setProperty` tools can find them (issue #1109).
//
// Why a descriptor and not [DynamicallyAccessedMembers]: the Type flowing into the
// lookups comes from el.GetType() and Type.BaseType, which carry no annotation the
// trimmer can propagate from, so DAM has nothing to attach to. A descriptor roots
// the members directly.
//
// Why *only* the DP getters: rooting the whole Microsoft.WinUI assembly
// (preserve="all") costs +16.7 MB on the devtools-on AOT sample. Rooting just the
// 1851 DependencyProperty static getters costs +1.5 MB — same capability, a tenth
// of the size. Measured, see docs/aot-support.md.
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

const string DependencyPropertyTypeName = "Microsoft.UI.Xaml.DependencyProperty";
const string WinUiAssemblyName = "Microsoft.WinUI";

// The feature switch devtools already ships behind. Gating the descriptor on it means
// an app that references the Devtools package but leaves the switch off pays exactly
// zero — verified byte-identical, see docs/aot-support.md.
const string FeatureSwitch = "Reactor.DevtoolsSupport";

if (args.Length < 2)
{
    Console.Error.WriteLine("""
        usage: Reactor.DevtoolsDescriptorGen <Microsoft.WinUI.dll> <descriptor.xml> [--check]

          Writes the descriptor, or with --check verifies the file on disk matches what
          would be generated and exits 1 if it is stale.
        """);
    return 2;
}

var winuiPath = args[0];
var descriptorPath = args[1];
var checkOnly = args.Contains("--check", StringComparer.Ordinal);

if (!File.Exists(winuiPath))
{
    Console.Error.WriteLine($"error: assembly not found: {winuiPath}");
    return 2;
}

var generated = Generate(winuiPath);

if (!checkOnly)
{
    File.WriteAllText(descriptorPath, generated);
    Console.WriteLine($"wrote {descriptorPath}");
    return 0;
}

if (!File.Exists(descriptorPath))
{
    Console.Error.WriteLine($"error: {descriptorPath} does not exist. Run the generator without --check.");
    return 1;
}

var onDisk = File.ReadAllText(descriptorPath);

// Compare with line endings normalised. The generator emits LF, but git's autocrlf
// hands a Windows clone CRLF, and a check that fails on that would be a spurious CI
// break on every fresh clone rather than a real staleness signal.
if (string.Equals(Normalise(onDisk), Normalise(generated), StringComparison.Ordinal))
{
    Console.WriteLine("descriptor is up to date");
    return 0;
}

// A stale descriptor is a silent failure: DPs added by a Windows App SDK bump simply
// stop being discoverable under AOT, and nothing else notices.
Console.Error.WriteLine(
    $"error: {descriptorPath} is stale relative to {Path.GetFileName(winuiPath)}. " +
    "A Windows App SDK update likely added or removed DependencyProperty statics. " +
    "Regenerate it (see docs/aot-support.md) and commit the result.");
return 1;

static string Normalise(string text) => text.Replace("\r\n", "\n");

static string Generate(string assemblyPath)
{
    using var stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    var md = pe.GetMetadataReader();
    var provider = new TypeNameSignatureProvider();

    // SortedDictionary/SortedSet with an ordinal comparer: the output has to be
    // byte-stable so --check can compare it, and metadata order is not guaranteed.
    var byType = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

    foreach (var handle in md.TypeDefinitions)
    {
        var type = md.GetTypeDefinition(handle);
        if ((type.Attributes & TypeAttributes.Public) == 0) continue;

        foreach (var propertyHandle in type.GetProperties())
        {
            var property = md.GetPropertyDefinition(propertyHandle);
            var getter = property.GetAccessors().Getter;
            if (getter.IsNil) continue;

            var method = md.GetMethodDefinition(getter);
            if ((method.Attributes & MethodAttributes.Static) == 0) continue;
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
            if (property.DecodeSignature(provider, null).ReturnType != DependencyPropertyTypeName) continue;

            var typeName = QualifiedName(md, type);
            if (!byType.TryGetValue(typeName, out var members))
            {
                members = new SortedSet<string>(StringComparer.Ordinal);
                byType[typeName] = members;
            }
            members.Add(md.GetString(property.Name));
        }
    }

    var total = byType.Sum(e => e.Value.Count);
    var sb = new StringBuilder();
    sb.Append("""
        <?xml version="1.0" encoding="utf-8"?>
        <!--
          GENERATED FILE. Do not edit by hand.

          Regenerate it with the tools/Reactor.DevtoolsDescriptorGen project, passing the
          resolved Microsoft.WinUI.dll and this file's path. The exact command line, and
          the reasoning below, are in docs/aot-support.md under "Devtools
          DependencyProperty discovery". (Spelling the command out here is not possible:
          a literal double hyphen is illegal inside an XML comment.)

          Keeps WinUI's DependencyProperty statics reflectable under trimming/NativeAOT so
          the devtools `properties` / `setProperty` tools can discover them (issue #1109).
          Without it those lookups find nothing on an AOT build, because CsWinRT projects
          the DP statics as static properties whose metadata the trimmer drops.

          Only the DP getters are rooted, not whole types: preserving everything in
          Microsoft.WinUI costs +16.7 MB on the devtools-on AOT sample, this costs +1.5 MB.
          Gated on the Reactor.DevtoolsSupport feature switch, so an app that references
          the Devtools package with devtools off pays nothing at all.
        -->

        """.Replace("\r\n", "\n"));
    sb.Append('\n');
    sb.Append("<linker>\n");
    sb.Append($"  <assembly fullname=\"{WinUiAssemblyName}\" feature=\"{FeatureSwitch}\" featurevalue=\"true\">\n");
    foreach (var (typeName, members) in byType)
    {
        sb.Append($"    <type fullname=\"{typeName}\">\n");
        foreach (var member in members)
            sb.Append($"      <method signature=\"{DependencyPropertyTypeName} get_{member}()\" />\n");
        sb.Append("    </type>\n");
    }
    sb.Append("  </assembly>\n");
    sb.Append("</linker>\n");

    Console.WriteLine($"{byType.Count} types, {total} DependencyProperty statics");
    return sb.ToString();
}

static string QualifiedName(MetadataReader md, TypeDefinition type)
{
    var ns = md.GetString(type.Namespace);
    var name = md.GetString(type.Name);
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

/// <summary>
/// Minimal signature decoder: we only ever compare the decoded return type against one
/// well-known name, so every shape that isn't a plain type reference can degrade to a
/// placeholder that will simply never match.
/// </summary>
internal sealed class TypeNameSignatureProvider : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "<fnptr>";
    public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) => genericType;
    public string GetGenericMethodParameter(object? genericContext, int index) => "<!!" + index + ">";
    public string GetGenericTypeParameter(object? genericContext, int index) => "<!" + index + ">";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => "<spec>";
}
