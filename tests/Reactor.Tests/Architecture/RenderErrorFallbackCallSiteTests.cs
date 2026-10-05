using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Architecture;

/// <summary>
/// Issue #1291 — the detailed render-error fallbacks show the full exception text
/// (<see cref="Exception.ToString"/>), which is exactly what an app's
/// <see cref="RenderErrorHandler"/> exists to keep off screen. They must therefore only be
/// built after the handler has been asked, which today means only from
/// <see cref="RenderErrorDispatch"/>'s "no handler / handler returned null" branches.
/// <para>
/// Nothing in the language enforces that: <c>ErrorFallback.BuildPanel</c> /
/// <c>BuildElement</c> are reachable from anywhere in <c>Reactor.dll</c> and from every
/// <c>InternalsVisibleTo</c> friend. This test reads the IL of <c>Reactor.dll</c> and
/// <c>Reactor.Advanced.dll</c> and fails if any type other than
/// <see cref="RenderErrorDispatch"/> (or its compiler-generated nested types) calls or
/// takes a delegate to either builder. A new caller should route through the dispatch
/// instead, or — if it is genuinely a "no handler configured" path — be added here with
/// a reason.
/// </para>
/// </summary>
public class RenderErrorFallbackCallSiteTests
{
    private const string FallbackTypeNamespace = "Microsoft.UI.Reactor.Core";
    private const string FallbackTypeName = "ErrorFallback";
    private static readonly HashSet<string> DetailedBuilders = new(StringComparer.Ordinal) { "BuildPanel", "BuildElement" };
    private static readonly HashSet<string> AllowedCallers = new(StringComparer.Ordinal)
    {
        "Microsoft.UI.Reactor.Core.RenderErrorDispatch",
    };

    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Test-only: reads the assemblies' on-disk Location to feed the IL/metadata scanner (PEReader). IL3000 only affects single-file publish; this metadata-scanning test can't run single-file and this host is not single-file-published. Behaviour-neutral.")]
    public void Detailed_Fallbacks_Are_Only_Built_By_The_Render_Error_Dispatch()
    {
        var callers = new List<(string Assembly, string Caller, string Builder)>();
        callers.AddRange(Scan(typeof(Element).Assembly.Location));
        callers.AddRange(Scan(typeof(global::Microsoft.UI.Reactor.Advanced.Factories).Assembly.Location));

        // Positive control: the dispatch itself builds both. If the scan finds neither, it
        // is misconfigured (wrong assembly, renamed type, broken decoder) and an empty
        // violation list below would prove nothing.
        Assert.Contains(callers, c => AllowedCallers.Contains(c.Caller) && c.Builder == "BuildElement");
        Assert.Contains(callers, c => AllowedCallers.Contains(c.Caller) && c.Builder == "BuildPanel");

        var violations = callers
            .Where(c => !AllowedCallers.Contains(c.Caller))
            .Select(c => $"{c.Assembly}: {c.Caller} -> ErrorFallback.{c.Builder}")
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(violations.Count == 0,
            "The detailed render-error fallbacks (full exception text) may only be built by " +
            "RenderErrorDispatch, after the app's RenderErrorHandler has been asked (issue #1291). " +
            "Route these through RenderErrorDispatch instead:\n  " + string.Join("\n  ", violations));
    }

    private static IEnumerable<(string Assembly, string Caller, string Builder)> Scan(string assemblyPath)
    {
        Assert.False(string.IsNullOrEmpty(assemblyPath), "Could not locate an assembly under test on disk.");
        var opcodes = BuildOpCodeTable();
        var results = new List<(string, string, string)>();
        var assemblyName = global::System.IO.Path.GetFileName(assemblyPath);

        using var stream = global::System.IO.File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var callerName = OutermostTypeName(reader, typeHandle);
            foreach (var method in reader.GetTypeDefinition(typeHandle).GetMethods().Select(reader.GetMethodDefinition))
            {
                if (method.RelativeVirtualAddress == 0) continue;
                var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                foreach (var token in MethodTokens(il, opcodes))
                {
                    if (TryGetDetailedBuilder(reader, token, out var builder))
                        results.Add((assemblyName, callerName, builder));
                }
            }
        }
        return results;
    }

    // Matches a call/ldftn target that is ErrorFallback.BuildPanel/BuildElement, whether
    // defined in this assembly (MethodDefinition) or referenced from a friend assembly
    // (MemberReference to a TypeReference).
    private static bool TryGetDetailedBuilder(MetadataReader reader, EntityHandle handle, out string builder)
    {
        builder = "";
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var m = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var name = reader.GetString(m.Name);
                var t = reader.GetTypeDefinition(m.GetDeclaringType());
                if (!DetailedBuilders.Contains(name) || !IsFallbackType(reader, t.Namespace, t.Name)) return false;
                builder = name;
                return true;
            }
            case HandleKind.MemberReference:
            {
                var mr = reader.GetMemberReference((MemberReferenceHandle)handle);
                var name = reader.GetString(mr.Name);
                if (!DetailedBuilders.Contains(name) || mr.Parent.Kind != HandleKind.TypeReference) return false;
                var tr = reader.GetTypeReference((TypeReferenceHandle)mr.Parent);
                if (!IsFallbackType(reader, tr.Namespace, tr.Name)) return false;
                builder = name;
                return true;
            }
            default:
                return false;
        }
    }

    private static bool IsFallbackType(MetadataReader reader, StringHandle ns, StringHandle name) =>
        reader.GetString(name) == FallbackTypeName && reader.GetString(ns) == FallbackTypeNamespace;

    // Lambdas, local functions and iterators compile into nested types; attribute them to the
    // type the source was written in.
    private static string OutermostTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        while (def.GetDeclaringType() is { IsNil: false } outer)
        {
            handle = outer;
            def = reader.GetTypeDefinition(handle);
        }
        var ns = reader.GetString(def.Namespace);
        var name = reader.GetString(def.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static IEnumerable<EntityHandle> MethodTokens(byte[]? il, Dictionary<int, int> operandSizes)
    {
        if (il is null) yield break;
        int pos = 0;
        while (pos < il.Length)
        {
            int key = il[pos++];
            if (key == 0xFE && pos < il.Length)
                key = 0xFE00 | il[pos++];
            if (!operandSizes.TryGetValue(key, out var size))
                yield break; // Unknown opcode — stop decoding this method defensively.

            if (size == TokenOperand)
            {
                if (pos + 4 > il.Length) yield break;
                int token = BitConverter.ToInt32(il, pos);
                pos += 4;
                EntityHandle handle;
                try { handle = MetadataTokens.EntityHandle(token); }
                catch (ArgumentException) { continue; }
                yield return handle;
            }
            else if (size == SwitchOperand)
            {
                if (pos + 4 > il.Length) yield break;
                pos += 4 + 4 * BitConverter.ToInt32(il, pos);
            }
            else
            {
                pos += size;
            }
        }
    }

    private const int TokenOperand = -1;
    private const int SwitchOperand = -2;

    // Operand sizes from the BCL's own OpCode table, so nothing is hand-maintained.
    private static Dictionary<int, int> BuildOpCodeTable()
    {
        var table = new Dictionary<int, int>();
        foreach (var op in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Select(f => f.GetValue(null)).OfType<OpCode>())
        {
            ushort raw = unchecked((ushort)op.Value);
            int key = op.Size == 2 ? (0xFE00 | (raw & 0xFF)) : (raw & 0xFF);
            table[key] = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.ShortInlineR or OperandType.InlineBrTarget
                    or OperandType.InlineString or OperandType.InlineSig => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok
                    or OperandType.InlineType => TokenOperand,
                OperandType.InlineSwitch => SwitchOperand,
                _ => 0,
            };
        }
        return table;
    }
}
