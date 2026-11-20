using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Architecture;

/// <summary>
/// Spec 062 §7 — pins the <c>InternalsVisibleTo("Reactor.Advanced")</c> surface.
///
/// Track B moved charting, docking, markdown and the data grid out of core into
/// <c>Reactor.Advanced</c>. Those subsystems still reach a handful of core
/// internals, so core grants Advanced a friend reference. An <c>InternalsVisibleTo</c>
/// grant is unbounded by nature: it lets the friend assembly touch <i>every</i>
/// internal in core, and nothing tells a reviewer when that reach grows.
///
/// This test converts the open-ended grant into a reviewed contract. It reads the
/// compiled <c>Reactor.Advanced.dll</c> metadata, resolves every reference that
/// lands in <c>Reactor.dll</c>, keeps the ones whose definition is <b>not</b>
/// public, and compares that set against <see cref="ApprovedSurface"/>. Adding a
/// new internal dependency fails the build until the entry is added here — which
/// is the point: each entry should be a deliberate decision, not an accident of
/// the two assemblies having once been one.
///
/// The comparison is exact in both directions. Removing an internal dependency
/// also fails, prompting the baseline to be trimmed, so the list stays an honest
/// description of the coupling rather than drifting into a stale over-grant.
///
/// See also <see cref="CoreControlFamilyBoundaryTests"/>, which pins the
/// complementary invariant (core must not reference Advanced at all).
/// </summary>
public class AdvancedInternalSurfaceTests
{
    private const string CoreAssemblyName = "Reactor";

    /// <summary>
    /// The reviewed set of core internals that <c>Reactor.Advanced</c> is allowed
    /// to touch. Entries are <c>kind: name</c>, where <c>kind</c> is:
    /// <list type="bullet">
    ///   <item><c>type</c> — an internal core type referenced by Advanced.</item>
    ///   <item><c>member</c> — a non-public member of an otherwise public core type.</item>
    ///   <item><c>override</c> — Advanced overrides a non-public virtual declared in core.
    ///     These emit no member reference, so they are detected separately (see
    ///     <see cref="CollectInternalVirtualOverrides"/>).</item>
    /// </list>
    ///
    /// Names are raw metadata names, so a property appears as its accessor
    /// (<c>get_HasCallbacks</c>, <c>get_Context</c>) — that is what the scanner sees,
    /// and it keeps the entry unambiguous when only one accessor is touched.
    ///
    /// Each entry exists for one of four reasons, none of which is "convenience":
    /// <list type="number">
    ///   <item><b>Inverted-dependency seams (issue #627).</b> Core deliberately must not
    ///     name charting/docking, so the subsystem registers itself through a seam:
    ///     <c>IChartingHostBridge</c>, <c>ChartingActivation</c>,
    ///     <c>ReactorHost.RegisterChartingBridge</c>, <c>IScanExtension</c>,
    ///     <c>IScanContext</c>, <c>AccessibilityScanner.RegisterScanExtension</c>, and
    ///     <c>Element.OwnPropsEqualOverride</c>. Making these public would put
    ///     subsystem-shaped extension points on core's permanent API surface;
    ///     <c>IScanExtension</c> in particular is stored in a single static slot
    ///     (last-write-wins), so publishing it would advertise a plug-in contract
    ///     that silently drops all but one registrant.</item>
    ///   <item><b>Framework-level operations.</b> <c>ValidationContext.ClearInternal</c>
    ///     is the framework-owned counterpart to the public <c>Clear</c>/<c>ClearExternal</c>;
    ///     <c>ReactorApp.OpenWindowCore</c> carries the shutdown-policy flag the public
    ///     <c>OpenWindow</c> overloads deliberately don't expose;
    ///     <c>ReflectionTypeMetadataProvider.BuildInitOnlySetter</c> and
    ///     <c>Component.Context</c> (settable so the reconciler can transplant a live
    ///     <c>RenderContext</c> on Hot Reload, spec 049 §7) are internal plumbing.</item>
    ///   <item><b>Performance hooks.</b> <c>Element.HasCallbacks</c> gates the
    ///     <c>ReactorState</c> allocation (spec 047 §4.4) and
    ///     <c>ElementFactory{T}.ShouldSkipRefresh</c> drives row memoization.
    ///     <c>HasCallbacks</c> is additionally overridden by ~50 core element records,
    ///     so publishing the base would force all of them onto the public surface.</item>
    ///   <item><b>Known gaps.</b> <c>Element.GetAttached</c>/<c>SetAttached</c> and
    ///     <c>ElementExtensions.OnUpdateAdd</c> are the attached-data and update-hook
    ///     primitives. The first pair is documented in the user guide as the way to
    ///     author a custom attached property, which makes its internal accessibility a
    ///     genuine defect rather than a deliberate boundary — tracked separately.</item>
    /// </list>
    ///
    /// <b>Before adding an entry</b>, check whether the public surface already covers
    /// the need. Track B removed five entries that way: <c>Reg&lt;&gt;</c> (superseded by
    /// the public <c>ControlRegistry.Register</c>, the same seam a third-party control
    /// library uses) and four per-record <c>Setters</c> writes (superseded by the public
    /// <c>.Set(...)</c> modifier).
    /// </summary>
    private static readonly ImmutableSortedSet<string> ApprovedSurface = ImmutableSortedSet.Create(
        StringComparer.Ordinal,

        // ── Inverted-dependency seams (issue #627) ────────────────────────────
        "type: Microsoft.UI.Reactor.Core.IScanContext",
        "type: Microsoft.UI.Reactor.Core.IScanExtension",
        "type: Microsoft.UI.Reactor.Hosting.ChartingActivation",
        "type: Microsoft.UI.Reactor.Hosting.IChartingHostBridge",
        "member: Microsoft.UI.Reactor.Core.AccessibilityScanner::RegisterScanExtension",
        "member: Microsoft.UI.Reactor.Hosting.ReactorHost::RegisterChartingBridge",
        "override: Microsoft.UI.Reactor.Core.Element::OwnPropsEqualOverride",

        // ── Framework-level operations ────────────────────────────────────────
        "type: Microsoft.UI.Reactor.Core.Diagnostics.ReactorEventSource",
        // Markdown's missing-Scope warning uses the framework's Release-visible ETW
        // helper and DEBUG mirror; there is no public warning-emission API.
        "type: Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog",
        "type: Microsoft.UI.Reactor.Core.Diagnostics.LogCategory",
        "member: Microsoft.UI.Reactor.Controls.ReflectionTypeMetadataProvider::BuildInitOnlySetter",
        "member: Microsoft.UI.Reactor.Controls.Validation.ValidationContext::ClearInternal",
        "member: Microsoft.UI.Reactor.Core.Component::get_Context",
        "member: Microsoft.UI.Reactor.ReactorApp::OpenWindowCore",

        // ── Performance hooks ─────────────────────────────────────────────────
        "member: Microsoft.UI.Reactor.Core.ElementFactory`1::ShouldSkipRefresh",
        "override: Microsoft.UI.Reactor.Core.Element::get_HasCallbacks",

        // ── Known gaps (see the GetAttached/SetAttached note above) ───────────
        "member: Microsoft.UI.Reactor.Core.Element::GetAttached",
        "member: Microsoft.UI.Reactor.Core.Element::SetAttached",
        "member: Microsoft.UI.Reactor.ElementExtensions::OnUpdateAdd");

    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Test-only: reads the on-disk Location of Reactor.dll / Reactor.Advanced.dll to feed the metadata scanner (PEReader). IL3000 only affects single-file publish (Location is empty there); this metadata-scanning test cannot run single-file and this host is not single-file-published. Behaviour-neutral.")]
    public void Advanced_TouchesOnlyTheApprovedCoreInternalSurface()
    {
        var actual = ScanInternalSurface(out var coreTypesIndexed, out var advancedTypesScanned);

        // Vacuity guards: a misconfigured scan (wrong assembly, empty metadata)
        // would otherwise report an empty set and "pass".
        Assert.True(coreTypesIndexed > 200,
            $"Indexed only {coreTypesIndexed} core types — the scan is misconfigured and would pass vacuously.");
        Assert.True(advancedTypesScanned > 50,
            $"Scanned only {advancedTypesScanned} Reactor.Advanced types — the scan is misconfigured and would pass vacuously.");

        var added = actual.Except(ApprovedSurface).ToArray();
        var removed = ApprovedSurface.Except(actual).ToArray();

        Assert.True(added.Length == 0,
            "Reactor.Advanced reaches core internals that are not in the reviewed allowlist " +
            "(spec 062 §7). Before widening the InternalsVisibleTo surface, check whether the " +
            "PUBLIC API already covers the need — e.g. ControlRegistry.Register for element " +
            "registration, or the .Set(...) modifier for imperative control configuration. " +
            "If the internal really is required, add it to ApprovedSurface with a rationale.\n  New: " +
            string.Join("\n  New: ", added));

        Assert.True(removed.Length == 0,
            "Reactor.Advanced no longer reaches these core internals, so the reviewed allowlist " +
            "is stale (spec 062 §7). This is the good direction — remove the entries from " +
            "ApprovedSurface so the list keeps describing the real coupling.\n  Gone: " +
            string.Join("\n  Gone: ", removed));
    }

    /// <summary>
    /// Meta-test: proves the scanner resolves real references rather than silently
    /// returning nothing (which would make the pin above vacuous). Reactor.Advanced
    /// provably uses core internals today, so the scan must find some — and it must
    /// find both categories the two detection strategies cover, since member
    /// references and implicit virtual overrides are found by different code paths.
    /// </summary>
    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Test-only metadata scan; see the sibling test. Behaviour-neutral.")]
    public void Scanner_ResolvesBothMemberReferencesAndImplicitOverrides()
    {
        var actual = ScanInternalSurface(out _, out _);

        Assert.True(actual.Any(e => e.StartsWith("member: ", StringComparison.Ordinal)),
            "Scanner found no non-public member references — the member-reference path is broken.");

        // Implicit virtual overrides emit no MemberRef, so they are found by walking
        // Advanced's method table against core's base-type chain. If that path breaks,
        // the pin would silently stop covering Element.HasCallbacks / OwnPropsEqualOverride.
        Assert.True(actual.Any(e => e.StartsWith("override: ", StringComparison.Ordinal)),
            "Scanner found no internal-virtual overrides — the override-detection path is broken " +
            "(these emit no member reference and are invisible to a plain metadata scan).");
    }

    // ── Scanner ───────────────────────────────────────────────────────────────

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Test-only: reads the on-disk Location of Reactor.dll / Reactor.Advanced.dll to feed the metadata scanner (PEReader). IL3000 only affects single-file publish (Location is empty there); this metadata-scanning test cannot run single-file and this host is not single-file-published. Behaviour-neutral.")]
    private static ImmutableSortedSet<string> ScanInternalSurface(
        out int coreTypesIndexed,
        out int advancedTypesScanned)
    {
        var corePath = typeof(Element).Assembly.Location;
        var advancedPath = typeof(global::Microsoft.UI.Reactor.Advanced.Factories).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(corePath), "Could not locate Reactor.dll on disk.");
        Assert.False(string.IsNullOrEmpty(advancedPath), "Could not locate Reactor.Advanced.dll on disk.");

        using var coreStream = global::System.IO.File.OpenRead(corePath);
        using var corePe = new PEReader(coreStream);
        var core = corePe.GetMetadataReader();

        var coreIndex = CoreIndex.Build(core);
        coreTypesIndexed = coreIndex.Visibility.Count;

        using var advStream = global::System.IO.File.OpenRead(advancedPath);
        using var advPe = new PEReader(advStream);
        var adv = advPe.GetMetadataReader();
        advancedTypesScanned = adv.TypeDefinitions.Count;

        var results = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var entry in CollectTypeAndMemberReferences(adv, coreIndex))
            results.Add(entry);

        foreach (var entry in CollectInternalVirtualOverrides(adv, coreIndex))
            results.Add(entry);

        return results.ToImmutable();
    }

    private static IEnumerable<string> CollectTypeAndMemberReferences(MetadataReader adv, CoreIndex coreIndex)
    {
        var provider = new UnderlyingTypeNameProvider(adv);

        foreach (var handle in adv.TypeReferences)
        {
            var typeRef = adv.GetTypeReference(handle);
            if (!IsRootedInCore(adv, typeRef)) continue;

            var name = TypeRefFullName(adv, typeRef);
            if (coreIndex.Visibility.TryGetValue(name, out var visibility) && !IsPublicType(visibility))
                yield return "type: " + name;
        }

        foreach (var handle in adv.MemberReferences)
        {
            var memberRef = adv.GetMemberReference(handle);
            string? owner = null;

            if (memberRef.Parent.Kind == HandleKind.TypeReference)
            {
                var typeRef = adv.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                if (IsRootedInCore(adv, typeRef)) owner = TypeRefFullName(adv, typeRef);
            }
            else if (memberRef.Parent.Kind == HandleKind.TypeSpecification)
            {
                // A generic instantiation (e.g. ElementFactory<int>). Decode the
                // signature down to the generic definition; without this the member
                // is silently missed.
                var spec = adv.GetTypeSpecification((TypeSpecificationHandle)memberRef.Parent);
                var decoded = spec.DecodeSignature(provider, default(object?));
                if (decoded.RootedInCore) owner = decoded.Name;
            }

            if (owner is null) continue;

            // Members of an internal core type are already covered by the "type:"
            // entry; reporting both would double-count the same decision.
            if (coreIndex.Visibility.TryGetValue(owner, out var ownerVisibility) && !IsPublicType(ownerVisibility))
                continue;

            var memberName = adv.GetString(memberRef.Name);
            if (coreIndex.MemberAccess.TryGetValue(owner, out var members)
                && members.TryGetValue(memberName, out var access)
                && !IsPublicAccess(access))
            {
                yield return $"member: {owner}::{memberName}";
            }
        }
    }

    /// <summary>
    /// Finds methods on Reactor.Advanced types that override a non-public virtual
    /// declared in core. These are invisible to a reference scan — an override emits
    /// no <c>MemberRef</c> — so they are detected structurally: a method that is
    /// virtual but does not claim a new slot is an override, and its declaring type's
    /// base chain is resolved into core to find the matching non-public virtual.
    /// </summary>
    private static IEnumerable<string> CollectInternalVirtualOverrides(MetadataReader adv, CoreIndex coreIndex)
    {
        foreach (var typeHandle in adv.TypeDefinitions)
        {
            var typeDef = adv.GetTypeDefinition(typeHandle);

            var overrideNames = typeDef.GetMethods()
                .Select(adv.GetMethodDefinition)
                .Where(m => (m.Attributes & MethodAttributes.Virtual) != 0
                         && (m.Attributes & MethodAttributes.NewSlot) == 0)
                .Select(m => adv.GetString(m.Name))
                .ToArray();

            if (overrideNames.Length == 0) continue;

            var coreBase = ResolveCoreBaseName(adv, typeDef);
            if (coreBase is null) continue;

            foreach (var name in overrideNames)
            {
                var declaring = coreIndex.FindNonPublicVirtualDeclarer(coreBase, name);
                if (declaring is not null)
                    yield return $"override: {declaring}::{name}";
            }
        }
    }

    /// <summary>Resolves an Advanced type's base type to a core type name, or null.</summary>
    private static string? ResolveCoreBaseName(MetadataReader adv, TypeDefinition typeDef)
    {
        var baseHandle = typeDef.BaseType;
        if (baseHandle.IsNil) return null;

        if (baseHandle.Kind == HandleKind.TypeReference)
        {
            var baseRef = adv.GetTypeReference((TypeReferenceHandle)baseHandle);
            return IsRootedInCore(adv, baseRef) ? TypeRefFullName(adv, baseRef) : null;
        }

        if (baseHandle.Kind == HandleKind.TypeSpecification)
        {
            var spec = adv.GetTypeSpecification((TypeSpecificationHandle)baseHandle);
            var decoded = spec.DecodeSignature(new UnderlyingTypeNameProvider(adv), default(object?));
            return decoded.RootedInCore ? decoded.Name : null;
        }

        // A base inside Advanced itself: walk up so a two-level subclass of a core
        // type is still resolved.
        if (baseHandle.Kind == HandleKind.TypeDefinition)
        {
            var baseDef = adv.GetTypeDefinition((TypeDefinitionHandle)baseHandle);
            return ResolveCoreBaseName(adv, baseDef);
        }

        return null;
    }

    // ── Metadata helpers ──────────────────────────────────────────────────────

    private static bool IsPublicType(TypeAttributes visibility) =>
        visibility is TypeAttributes.Public
            or TypeAttributes.NestedPublic
            // Protected (and protected-internal) nested types are reachable by any
            // external subclass, so they are ordinary extensibility surface rather
            // than something the friend grant provides. Mirrors IsPublicAccess.
            or TypeAttributes.NestedFamily
            or TypeAttributes.NestedFamORAssem;

    /// <summary>
    /// Protected / protected-internal members are reachable by any external subclass,
    /// so they are part of the ordinary extensibility surface rather than something
    /// the friend grant provides.
    /// </summary>
    private static bool IsPublicAccess(string access) =>
        access is "Public" or "Family" or "FamORAssem";

    private static bool IsRootedInCore(MetadataReader adv, TypeReference typeRef)
    {
        var scope = typeRef.ResolutionScope;
        while (scope.Kind == HandleKind.TypeReference)
            scope = adv.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;

        return scope.Kind == HandleKind.AssemblyReference
            && string.Equals(
                adv.GetString(adv.GetAssemblyReference((AssemblyReferenceHandle)scope).Name),
                CoreAssemblyName,
                StringComparison.Ordinal);
    }

    private static string TypeRefFullName(MetadataReader adv, TypeReference typeRef)
    {
        var name = adv.GetString(typeRef.Name);
        var ns = adv.GetString(typeRef.Namespace);

        if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var declaring = adv.GetTypeReference((TypeReferenceHandle)typeRef.ResolutionScope);
            return TypeRefFullName(adv, declaring) + "+" + name;
        }

        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    /// <summary>Core's type visibility, member accessibility and base chain, keyed by full name.</summary>
    private sealed class CoreIndex
    {
        public required Dictionary<string, TypeAttributes> Visibility { get; init; }
        public required Dictionary<string, Dictionary<string, string>> MemberAccess { get; init; }
        public required Dictionary<string, string?> BaseName { get; init; }
        public required Dictionary<string, HashSet<string>> NonPublicVirtuals { get; init; }

        public static CoreIndex Build(MetadataReader core)
        {
            var visibility = new Dictionary<string, TypeAttributes>(StringComparer.Ordinal);
            var memberAccess = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            var baseName = new Dictionary<string, string?>(StringComparer.Ordinal);
            var nonPublicVirtuals = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var handle in core.TypeDefinitions)
            {
                var typeDef = core.GetTypeDefinition(handle);
                var name = FullName(core, typeDef);

                visibility[name] = typeDef.Attributes & TypeAttributes.VisibilityMask;
                baseName[name] = ResolveBaseName(core, typeDef);

                var members = new Dictionary<string, string>(StringComparer.Ordinal);
                var virtuals = new HashSet<string>(StringComparer.Ordinal);

                // Overloads share a metadata name. Record the LEAST permissive access
                // across them, so an internal overload is still reported when a public
                // overload of the same name exists — the conservative direction for a
                // guard whose job is to notice internal dependencies. ("Most permissive
                // wins" would let `public Clear(...)` mask `internal Clear(...)` and
                // silently drop a real friend-grant use.) The trade is a possible false
                // positive when Advanced only ever calls the public overload; that fails
                // loudly and is settled by review, whereas a false negative is invisible.
                //
                // Compiler-generated members are excluded first (see IsSynthesized):
                // every positional record emits a PRIVATE copy constructor alongside its
                // public primary one, so without that filter every record Advanced
                // constructs would be reported as an internal `::.ctor` dependency.
                void Record(string memberName, string access)
                {
                    if (!members.TryGetValue(memberName, out var existing) || Rank(access) < Rank(existing))
                        members[memberName] = access;
                }

                foreach (var method in typeDef.GetMethods().Select(core.GetMethodDefinition))
                {
                    if (IsSynthesizedConstructor(core, method)) continue;

                    var methodName = core.GetString(method.Name);
                    var access = (method.Attributes & MethodAttributes.MemberAccessMask).ToString();
                    Record(methodName, access);

                    if ((method.Attributes & MethodAttributes.Virtual) != 0 && !IsPublicAccess(access))
                        virtuals.Add(methodName);
                }

                foreach (var field in typeDef.GetFields().Select(core.GetFieldDefinition))
                {
                    Record(core.GetString(field.Name), (field.Attributes & FieldAttributes.FieldAccessMask).ToString());
                }

                memberAccess[name] = members;
                nonPublicVirtuals[name] = virtuals;
            }

            return new CoreIndex
            {
                Visibility = visibility,
                MemberAccess = memberAccess,
                BaseName = baseName,
                NonPublicVirtuals = nonPublicVirtuals,
            };
        }

        /// <summary>
        /// Walks core's base chain from <paramref name="typeName"/> looking for the type
        /// that declares a non-public virtual called <paramref name="methodName"/>.
        /// </summary>
        public string? FindNonPublicVirtualDeclarer(string typeName, string methodName)
        {
            var current = typeName;
            var guard = 0;

            while (current is not null && guard++ < 64)
            {
                if (NonPublicVirtuals.TryGetValue(current, out var virtuals) && virtuals.Contains(methodName))
                    return current;

                BaseName.TryGetValue(current, out current);
            }

            return null;
        }

        private static int Rank(string access) => access switch
        {
            "Public" => 5,
            "FamORAssem" => 4,
            "Family" => 3,
            "Assembly" => 2,
            "FamANDAssem" => 1,
            _ => 0,
        };

        /// <summary>
        /// True for a compiler-emitted <b>constructor</b> — specifically the private copy
        /// constructor every positional record gets alongside its public primary one.
        /// Without this, every record Advanced constructs looks like an internal
        /// <c>::.ctor</c> dependency once overloads are keyed least-permissive.
        ///
        /// Deliberately limited to constructors rather than "anything
        /// <c>[CompilerGenerated]</c>": auto-property accessors carry that attribute too,
        /// and those are real, reportable surface (<c>Component::get_Context</c> is one of
        /// the pinned entries).
        /// </summary>
        private static bool IsSynthesizedConstructor(MetadataReader core, MethodDefinition method)
        {
            if (core.GetString(method.Name) is not (".ctor" or ".cctor")) return false;

            foreach (var attribute in method.GetCustomAttributes().Select(core.GetCustomAttribute))
            {
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;

                var ctor = core.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (ctor.Parent.Kind != HandleKind.TypeReference) continue;

                var attributeType = core.GetTypeReference((TypeReferenceHandle)ctor.Parent);
                if (core.GetString(attributeType.Name) is "CompilerGeneratedAttribute")
                    return true;
            }

            return false;
        }

        private static string FullName(MetadataReader core, TypeDefinition typeDef)
        {
            var name = core.GetString(typeDef.Name);
            var ns = core.GetString(typeDef.Namespace);

            if (typeDef.IsNested)
                return FullName(core, core.GetTypeDefinition(typeDef.GetDeclaringType())) + "+" + name;

            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static string? ResolveBaseName(MetadataReader core, TypeDefinition typeDef)
        {
            var baseHandle = typeDef.BaseType;
            if (baseHandle.IsNil) return null;

            return baseHandle.Kind switch
            {
                HandleKind.TypeDefinition => FullName(core, core.GetTypeDefinition((TypeDefinitionHandle)baseHandle)),
                _ => null, // A base outside core ends the walk; core internals are what we're after.
            };
        }
    }

    /// <summary>
    /// Decodes a type-specification signature to the underlying generic definition's
    /// name, so a member referenced through <c>ElementFactory&lt;int&gt;</c> resolves
    /// to <c>ElementFactory`1</c>.
    /// </summary>
    private readonly record struct TypeName(string Name, bool RootedInCore);

    private sealed class UnderlyingTypeNameProvider(MetadataReader adv)
        : ISignatureTypeProvider<TypeName, object?>
    {
        public TypeName GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var typeRef = reader.GetTypeReference(handle);
            return new TypeName(TypeRefFullName(adv, typeRef), IsRootedInCore(adv, typeRef));
        }

        public TypeName GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var ns = reader.GetString(typeDef.Namespace);
            var name = reader.GetString(typeDef.Name);
            return new TypeName(string.IsNullOrEmpty(ns) ? name : ns + "." + name, false);
        }

        public TypeName GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        // The instantiation's identity is its generic definition, which is what the
        // member lookup needs.
        public TypeName GetGenericInstantiation(TypeName genericType, ImmutableArray<TypeName> typeArguments) => genericType;

        public TypeName GetSZArrayType(TypeName elementType) => elementType;
        public TypeName GetArrayType(TypeName elementType, ArrayShape shape) => elementType;
        public TypeName GetByReferenceType(TypeName elementType) => elementType;
        public TypeName GetPointerType(TypeName elementType) => elementType;
        public TypeName GetPinnedType(TypeName elementType) => elementType;
        public TypeName GetModifiedType(TypeName modifier, TypeName unmodifiedType, bool isRequired) => unmodifiedType;
        public TypeName GetPrimitiveType(PrimitiveTypeCode typeCode) => new(typeCode.ToString(), false);
        public TypeName GetGenericMethodParameter(object? genericContext, int index) => new("!!" + index, false);
        public TypeName GetGenericTypeParameter(object? genericContext, int index) => new("!" + index, false);
        public TypeName GetFunctionPointerType(MethodSignature<TypeName> signature) => new("fnptr", false);
    }
}
