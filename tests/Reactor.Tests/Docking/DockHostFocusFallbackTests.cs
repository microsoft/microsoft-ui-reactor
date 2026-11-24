using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 045 §2.22 — regression guard for the docking focus hand-off.
///
/// <para>
/// <see cref="DockHostLiveAnnouncer"/>'s non-<c>Control</c> host arm used to call
/// <c>TryMoveFocusAsync(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = host })</c>.
/// WinUI rejects that pairing during parameter validation — <i>"Focus navigation
/// directions Next and Previous are not supported when using
/// FindNextElementOptions"</i> — so every hand-off threw an
/// <see cref="ArgumentException"/>, and because the call was a bare <c>_ =</c>
/// discard the failure was invisible and focus never moved at all.
/// </para>
///
/// <para>
/// Headless unit tests cannot construct any <c>Microsoft.UI.Xaml</c> object, so the
/// live behaviour is pinned by the
/// <c>NativeDocking_A11y_FocusFallback_LandsInsideHostSubtree</c> self-test fixture.
/// What these tests pin is the <i>call shape</i>, read straight out of the shipped
/// <c>Microsoft.UI.Reactor.Advanced.dll</c> with <see cref="MetadataReader"/> — no type loading,
/// no COM activation. Between them they fail if the illegal pairing comes back, if
/// the host-scoped search is dropped, or if the focus move is removed.
/// </para>
/// </summary>
public sealed class DockHostFocusFallbackTests
{
    private const string AnnouncerNamespace = "Microsoft.UI.Reactor.Docking.Native";
    private const string AnnouncerType = "DockHostLiveAnnouncer";
    private const string WinUiFocusManager = "Microsoft.UI.Xaml.Input.FocusManager";

    /// <summary>
    /// The pairing that caused the bug. <c>FindNextElementOptions</c> is only
    /// accepted alongside the four directional values, and the announcer has no
    /// legitimate use for a directional search — so neither the options type nor
    /// any <c>TryMoveFocus*</c> overload should appear in its IL at all.
    /// </summary>
    [Fact]
    public void Announcer_never_pairs_a_directional_move_with_FindNextElementOptions()
    {
        var scan = ScanAnnouncer();

        Assert.DoesNotContain(
            scan.CalledMembers,
            m => m.EndsWith(".TryMoveFocus", StringComparison.Ordinal)
              || m.EndsWith(".TryMoveFocusAsync", StringComparison.Ordinal));

        Assert.DoesNotContain(
            scan.ReferencedTypes,
            t => t.EndsWith(".FindNextElementOptions", StringComparison.Ordinal));
    }

    /// <summary>
    /// The replacement must keep the search scoped to the registered host.
    /// Fails the "just delete the options" regression, where a bare
    /// <c>TryMoveFocusAsync(Next)</c> walks the global tab order and can hand
    /// focus to something outside the dock host entirely.
    /// </summary>
    [Fact]
    public void Announcer_scopes_the_focusable_search_to_the_registered_host()
    {
        var scan = ScanAnnouncer();

        Assert.Contains(
            $"{WinUiFocusManager}.FindFirstFocusableElement",
            scan.CalledMembers);
    }

    /// <summary>
    /// …and must still actually move focus onto whatever that search found.
    /// Fails if the focus move is deleted and the fallback silently degrades to
    /// a lookup that throws its result away.
    /// </summary>
    [Fact]
    public void Announcer_moves_focus_onto_the_element_it_found()
    {
        var scan = ScanAnnouncer();

        Assert.Contains(
            $"{WinUiFocusManager}.TryFocusAsync",
            scan.CalledMembers);
    }

    /// <summary>
    /// The original bug survived because the call was a bare <c>_ =</c> discard,
    /// so the <see cref="ArgumentException"/> was never surfaced. Both halves of
    /// the outcome must now be observed: the synchronous throw (a catch handler
    /// around the focus work) and the asynchronous fault (a continuation on the
    /// returned operation), each routed through <c>ReportFocusFailure</c> to
    /// <c>ReactorEventSource</c>'s <c>SwallowedError</c> event.
    /// </summary>
    [Fact]
    public void Announcer_observes_focus_failures_instead_of_discarding_them()
    {
        var scan = ScanAnnouncer();

        // Synchronous half: the focus work sits inside a catch handler that
        // reports rather than discards. Scoped to TryFocus so a stray handler
        // elsewhere in the type cannot satisfy it.
        Assert.Contains("TryFocus", scan.MethodsWithExceptionHandlers);
        Assert.Contains(
            scan.CallsFrom("TryFocus"),
            m => m.EndsWith(".ReportFocusFailure", StringComparison.Ordinal));

        // Asynchronous half: TryFocus hands the returned IAsyncOperation to the
        // observer (not to a `_ =` discard), the observer chains its outcome,
        // and the continuation body reports it. Every link is asserted, so dead
        // code cannot satisfy any of them.
        Assert.Contains(
            scan.CallsFrom("TryFocus"),
            m => m.EndsWith(".ObserveFocusMove", StringComparison.Ordinal));
        Assert.Contains(
            scan.CallsFrom("ObserveFocusMove"),
            m => m.EndsWith(".ContinueWith", StringComparison.Ordinal));

        // Roslyn emits the `static` continuation lambda as
        // `<ObserveFocusMove>b__N_M` on a nested closure type, so the enclosing
        // method's name is still in the metadata — match on that rather than
        // hard-coding the mangled suffix.
        Assert.Contains(
            scan.CallsByMethod,
            kv => kv.Key.Contains("ObserveFocusMove", StringComparison.Ordinal)
               && kv.Value.Any(m => m.EndsWith(".ReportFocusFailure", StringComparison.Ordinal)));

        // …and the reporter really reaches the trace.
        Assert.Contains(
            scan.CallsFrom("ReportFocusFailure"),
            m => m.EndsWith(".SwallowedError", StringComparison.Ordinal));
    }

    /// <summary>
    /// The announcer must reach the ETW provider directly rather than through
    /// <c>DiagnosticLog</c>. Going through the helper pulls <c>DiagnosticLog</c>
    /// and <c>LogCategory</c> into the reviewed core-internal allowlist that
    /// <see cref="Architecture.AdvancedInternalSurfaceTests"/> pins (spec 062
    /// §7), for no change in the emitted payload. <c>ReactorEventSource</c> is
    /// already on that allowlist and already used by docking's
    /// <c>DockLayoutSerializer</c>, so this keeps the surface flat.
    /// </summary>
    [Fact]
    public void Announcer_traces_without_widening_the_core_internal_surface()
    {
        var scan = ScanAnnouncer();

        Assert.Contains(
            "Microsoft.UI.Reactor.Core.Diagnostics.ReactorEventSource.SwallowedError",
            scan.CalledMembers);

        Assert.DoesNotContain(
            scan.ReferencedTypes,
            t => t.EndsWith(".DiagnosticLog", StringComparison.Ordinal)
              || t.EndsWith(".LogCategory", StringComparison.Ordinal));
    }

    /// <summary>
    /// Spec 044 §6.7.2 — the focus swallow is audited as **Narrow**, not Keep
    /// (see <c>docs/specs/044/swallowed-error-audit.md</c>). A broad
    /// <c>catch (Exception)</c> here is what hid the R4 bug: an
    /// <see cref="ArgumentException"/> on every hand-off that nobody saw.
    /// Surprise exception types must propagate, so no handler in the announcer
    /// may catch <see cref="Exception"/> itself.
    /// </summary>
    [Fact]
    public void Announcer_narrows_the_focus_catch()
    {
        var scan = ScanAnnouncer();

        // Sanity: there *is* a handler to inspect, so a rename can't make this
        // pass by finding nothing.
        Assert.Contains("TryFocus", scan.MethodsWithExceptionHandlers);
        Assert.NotEmpty(scan.CaughtTypes);

        Assert.DoesNotContain("System.Exception", scan.CaughtTypes);

        // …and not laundered through `catch (Exception) when (...)`, which hides
        // the declared type from metadata.
        Assert.DoesNotContain("<filter>", scan.CaughtTypes);

        // The three classes the audit entry names as actually reachable here.
        Assert.Contains("System.ArgumentException", scan.CaughtTypes);
        Assert.Contains("System.Runtime.InteropServices.COMException", scan.CaughtTypes);
        Assert.Contains("System.InvalidOperationException", scan.CaughtTypes);
    }

    /// <summary>
    /// Scanner self-check. Without this, a scan that silently matched nothing
    /// (renamed type, stripped IL, wrong assembly) would make every
    /// <c>DoesNotContain</c> above pass vacuously.
    /// </summary>
    [Fact]
    public void Scanner_actually_reached_the_announcer_il()
    {
        var scan = ScanAnnouncer();

        Assert.True(scan.MethodsWithIl >= 5,
            $"Expected to decode the announcer's method bodies; saw {scan.MethodsWithIl}.");
        Assert.NotEmpty(scan.CalledMembers);

        // An unrelated, stable call the announcer has always made. Proves the
        // decoder resolves WinUI member references (and not just intra-assembly
        // ones) on this machine's metadata.
        Assert.Contains(
            scan.CalledMembers,
            m => m.EndsWith(".RaiseNotificationEvent", StringComparison.Ordinal));
    }

    // ── metadata scan ────────────────────────────────────────────────────────

    // The scan is a full PEReader + metadata + IL walk plus a reflected opcode
    // table, and every [Fact] needs the same read-only result — so do it once.
    // Lazy's default ExecutionAndPublication mode makes this safe under xUnit's
    // parallel collections, and a failure inside the factory (e.g. the assembly
    // not being on disk) is cached and rethrown for every test, which is the
    // reporting behaviour we want anyway.
    private static readonly Lazy<AnnouncerScan> _scan = new(ScanAnnouncerCore);

    private static AnnouncerScan ScanAnnouncer() => _scan.Value;

    private sealed record AnnouncerScan(
        IReadOnlySet<string> CalledMembers,
        IReadOnlySet<string> ReferencedTypes,
        IReadOnlySet<string> MethodsWithExceptionHandlers,
        IReadOnlySet<string> CaughtTypes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> CallsByMethod,
        int MethodsWithIl)
    {
        /// <summary>Members called from the named method's own IL body.</summary>
        public IReadOnlySet<string> CallsFrom(string methodName)
        {
            var found = CallsByMethod.TryGetValue(methodName, out var calls);
            Assert.True(found,
                $"{AnnouncerType} has no method named '{methodName}' with a decoded body.");
            return calls!;
        }
    }

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile", "IL3000",
        Justification = "Test-only: reads Microsoft.UI.Reactor.Advanced.dll's on-disk Location to feed the IL/metadata scanner (PEReader) — the assertion below fails loudly on an empty path. IL3000 only affects single-file publish; this metadata-scanning test cannot run single-file and this host is not single-file-published. Behaviour-neutral.")]
    private static AnnouncerScan ScanAnnouncerCore()
    {
        var assemblyPath = typeof(DockHostLiveAnnouncer).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(assemblyPath),
            "Could not locate Microsoft.UI.Reactor.Advanced.dll on disk.");

        var opcodes = BuildOpCodeTable();
        var members = new SortedSet<string>(StringComparer.Ordinal);
        var types = new SortedSet<string>(StringComparer.Ordinal);
        var withHandlers = new SortedSet<string>(StringComparer.Ordinal);
        var caughtTypes = new SortedSet<string>(StringComparer.Ordinal);
        var callsByMethod = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        int methodsWithIl = 0;

        using var stream = global::System.IO.File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var announcer = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Where(t => reader.GetString(t.Name) == AnnouncerType
                     && reader.GetString(t.Namespace) == AnnouncerNamespace)
            .ToList();
        Assert.True(announcer.Count == 1,
            $"Expected exactly one {AnnouncerNamespace}.{AnnouncerType}; found {announcer.Count}.");

        // Include nested types: `static` lambdas and any state machines the
        // compiler lifts out of the announcer's methods land there, so scanning
        // only the outer type would let a refactor hide a call site.
        var pending = new Queue<TypeDefinition>(announcer);
        while (pending.Count > 0)
        {
            var typeDef = pending.Dequeue();
            foreach (var nested in typeDef.GetNestedTypes())
                pending.Enqueue(reader.GetTypeDefinition(nested));

            foreach (var method in typeDef.GetMethods()
                .Select(reader.GetMethodDefinition)
                .Where(m => m.RelativeVirtualAddress != 0))
            {
                var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                var il = body.GetILBytes();
                if (il is null || il.Length == 0) continue;
                methodsWithIl++;
                var methodName = reader.GetString(method.Name);
                foreach (var region in body.ExceptionRegions)
                {
                    if (region.Kind == ExceptionRegionKind.Catch)
                    {
                        withHandlers.Add(methodName);
                        var caught = TypeNameOf(reader, region.CatchType);
                        if (caught is not null) caughtTypes.Add(caught);
                    }
                    else if (region.Kind == ExceptionRegionKind.Filter)
                    {
                        // `catch (T) when (...)` compiles to a filter region with
                        // a nil CatchType, so the declared type is invisible to a
                        // metadata scan. Record a sentinel: this audited site is
                        // pinned to plain typed catches, and a rewrite into the
                        // filter form must come back through review.
                        withHandlers.Add(methodName);
                        caughtTypes.Add("<filter>");
                    }
                }

                var perMethod = new SortedSet<string>(StringComparer.Ordinal);
                ScanMethodBody(reader, il, opcodes, perMethod, types);
                members.UnionWith(perMethod);
                // Overloads share a name; union them — the assertions only ask
                // whether the announcer's `<name>` code path performs a call.
                callsByMethod[methodName] = callsByMethod.TryGetValue(methodName, out var existing)
                    ? new SortedSet<string>(existing.Concat(perMethod), StringComparer.Ordinal)
                    : perMethod;
            }
        }

        return new AnnouncerScan(members, types, withHandlers, caughtTypes, callsByMethod, methodsWithIl);
    }

    private static void ScanMethodBody(
        MetadataReader reader, byte[] il, Dictionary<int, OperandKind> opcodes,
        SortedSet<string> members, SortedSet<string> types)
    {
        int pos = 0;
        while (pos < il.Length)
        {
            int opKey = il[pos++];
            if (opKey == 0xFE && pos < il.Length)
                opKey = 0xFE00 | il[pos++];

            if (!opcodes.TryGetValue(opKey, out var kind))
            {
                // Unknown opcode: the decode is out of sync, so anything after
                // this point would be garbage. Fail loudly rather than silently
                // truncating the scan and weakening the assertions.
                Assert.Fail($"Unknown IL opcode 0x{opKey:X} while decoding DockHostLiveAnnouncer.");
                return;
            }

            switch (kind)
            {
                case OperandKind.None:
                    break;
                case OperandKind.Int8:
                case OperandKind.ShortVar:
                case OperandKind.ShortBr:
                    pos += 1;
                    break;
                case OperandKind.Var:
                    pos += 2;
                    break;
                case OperandKind.Int32:
                case OperandKind.Float32:
                case OperandKind.Br:
                case OperandKind.StringToken:
                case OperandKind.SigToken:
                    pos += 4;
                    break;
                case OperandKind.Int64:
                case OperandKind.Float64:
                    pos += 8;
                    break;
                case OperandKind.Token:
                {
                    if (pos + 4 > il.Length) return;
                    int token = BitConverter.ToInt32(il, pos);
                    pos += 4;
                    RecordToken(reader, token, members, types);
                    break;
                }
                case OperandKind.Switch:
                {
                    if (pos + 4 > il.Length) return;
                    int n = BitConverter.ToInt32(il, pos);
                    pos += 4 + (4 * n);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Records an IL metadata token as <c>Namespace.Type.Member</c> (for member
    /// references / definitions) and/or <c>Namespace.Type</c> (for type tokens),
    /// reading names straight from metadata so no assembly ever has to load.
    /// </summary>
    private static void RecordToken(
        MetadataReader reader, int token, SortedSet<string> members, SortedSet<string> types)
    {
        if (token == 0) return;
        EntityHandle handle;
        try { handle = MetadataTokens.EntityHandle(token); }
        catch (ArgumentException) { return; }
        if (handle.IsNil) return;

        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
            {
                var mr = reader.GetMemberReference((MemberReferenceHandle)handle);
                var owner = TypeNameOf(reader, mr.Parent);
                if (owner is null) return;
                types.Add(owner);
                members.Add($"{owner}.{reader.GetString(mr.Name)}");
                break;
            }
            case HandleKind.MethodDefinition:
            {
                var md = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var owner = TypeNameOf(reader, md.GetDeclaringType());
                if (owner is null) return;
                members.Add($"{owner}.{reader.GetString(md.Name)}");
                break;
            }
            case HandleKind.FieldDefinition:
            {
                var fd = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                var owner = TypeNameOf(reader, fd.GetDeclaringType());
                if (owner is null) return;
                members.Add($"{owner}.{reader.GetString(fd.Name)}");
                break;
            }
            case HandleKind.TypeReference:
            case HandleKind.TypeDefinition:
            {
                var name = TypeNameOf(reader, handle);
                if (name is not null) types.Add(name);
                break;
            }
            case HandleKind.MethodSpecification:
            {
                var ms = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                RecordToken(reader, MetadataTokens.GetToken(ms.Method), members, types);
                break;
            }
        }
    }

    private static string? TypeNameOf(MetadataReader reader, EntityHandle handle)
    {
        if (handle.IsNil) return null;
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            {
                var tr = reader.GetTypeReference((TypeReferenceHandle)handle);
                var name = reader.GetString(tr.Name);
                // Nested type refs carry their declaring type as the scope.
                if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
                {
                    var outer = TypeNameOf(reader, tr.ResolutionScope);
                    return outer is null ? name : $"{outer}+{name}";
                }
                var ns = reader.GetString(tr.Namespace);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
            case HandleKind.TypeDefinition:
            {
                var td = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                var name = reader.GetString(td.Name);
                if (td.IsNested)
                {
                    var outer = TypeNameOf(reader, td.GetDeclaringType());
                    return outer is null ? name : $"{outer}+{name}";
                }
                var ns = reader.GetString(td.Namespace);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
            case HandleKind.TypeSpecification:
            {
                // Generic instantiation / array / pointer — decode the blob so a
                // member on e.g. `Task<FocusMovementResult>` is still attributed
                // to `System.Threading.Tasks.Task`1`, not silently dropped.
                var ts = reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                try { return ts.DecodeSignature(TypeNameSignatureProvider.Instance, null); }
                catch (BadImageFormatException) { return null; }
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Minimal signature decoder that yields a readable type name. Generic
    /// instantiations collapse to the open generic's name (<c>Task`1</c>),
    /// which is what member attribution needs.
    /// </summary>
    private sealed class TypeNameSignatureProvider : ISignatureTypeProvider<string, object?>
    {
        public static readonly TypeNameSignatureProvider Instance = new();

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var td = reader.GetTypeDefinition(handle);
            var ns = reader.GetString(td.Namespace);
            var name = reader.GetString(td.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var tr = reader.GetTypeReference(handle);
            var ns = reader.GetString(tr.Namespace);
            var name = reader.GetString(tr.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
        public string GetSZArrayType(string elementType) => elementType;
        public string GetArrayType(string elementType, ArrayShape shape) => elementType;
        public string GetByReferenceType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetFunctionPointerType(MethodSignature<string> signature) => "<fnptr>";
        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    }

    private enum OperandKind
    {
        None, Int8, ShortVar, ShortBr, Var, Int32, Float32, Br,
        Int64, Float64, Token, StringToken, SigToken, Switch,
    }

    private static Dictionary<int, OperandKind> BuildOpCodeTable()
    {
        var table = new Dictionary<int, OperandKind>();
        var opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetValue(null))
            .OfType<OpCode>();
        foreach (var op in opCodes)
        {
            ushort raw = unchecked((ushort)op.Value);
            int key = op.Size == 2 ? (0xFE00 | (raw & 0xFF)) : (raw & 0xFF);
            table[key] = MapOperand(op.OperandType);
        }
        return table;
    }

    private static OperandKind MapOperand(OperandType type) => type switch
    {
        OperandType.InlineNone => OperandKind.None,
        OperandType.ShortInlineI => OperandKind.Int8,
        OperandType.ShortInlineVar => OperandKind.ShortVar,
        OperandType.ShortInlineBrTarget => OperandKind.ShortBr,
        OperandType.InlineVar => OperandKind.Var,
        OperandType.InlineI => OperandKind.Int32,
        OperandType.ShortInlineR => OperandKind.Float32,
        OperandType.InlineBrTarget => OperandKind.Br,
        OperandType.InlineI8 => OperandKind.Int64,
        OperandType.InlineR => OperandKind.Float64,
        OperandType.InlineField => OperandKind.Token,
        OperandType.InlineMethod => OperandKind.Token,
        OperandType.InlineTok => OperandKind.Token,
        OperandType.InlineType => OperandKind.Token,
        OperandType.InlineString => OperandKind.StringToken,
        OperandType.InlineSig => OperandKind.SigToken,
        OperandType.InlineSwitch => OperandKind.Switch,
        _ => OperandKind.None,
    };
}
