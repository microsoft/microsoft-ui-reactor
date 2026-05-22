using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 Phase 11.1 — public-API surface guard.
///
/// Walks every element record reflectively and asserts that every
/// <c>Action</c>/<c>Action&lt;T&gt;</c>/<c>Action&lt;T1,T2&gt;</c> callback
/// property (i.e. one whose name begins with <c>On</c>) has a matching
/// public extension method on a <c>static partial class</c> in the Reactor
/// assembly whose:
/// <list type="bullet">
///   <item><description>Name equals the property name with the leading
///   <c>On</c> dropped (the Phase 1 / spec §15 Q1 fluent convention), OR
///   equals the property name verbatim if it lacks an <c>On</c> prefix.</description></item>
///   <item><description>First parameter is assignable from the element type
///   (so it actually targets the right record).</description></item>
///   <item><description>Second parameter (the handler) is a nullable delegate
///   type compatible with the property's declared delegate type (matches the
///   null-clear contract from spec §15 Q2).</description></item>
/// </list>
///
/// Failures are collected and reported in one assertion so adding several
/// callbacks at once surfaces every missing fluent in a single failure.
/// </summary>
public class PublicApiSurfaceGuardTests
{
    /// <summary>
    /// Known callback properties that are intentionally NOT exposed via a
    /// matching fluent. Each entry is (TypeName, PropertyName).
    ///
    /// Rationale per entry:
    ///   - <c>VirtualListElement.Ref</c> — a ref-capture callback (analogous to
    ///     React's <c>ref</c>) wired at construction. The fluent surface already
    ///     has a generic <c>.Ref&lt;T&gt;()</c> modifier with different semantics
    ///     (typed element reference capture); a record-specific <c>.Ref(...)</c>
    ///     fluent would clash. Spec 039 §15 Q1 carves these out.
    ///   - <c>XamlHostElement.Updater</c> — a constructor-time updater function,
    ///     not an event handler. Set via the positional record parameter; a
    ///     <c>.Updater(...)</c> fluent would imply mid-chain rebinding which the
    ///     interop reconciler does not support (the updater runs once on mount
    ///     and again on element replacement; see XamlInterop.cs).
    /// </summary>
    private static readonly HashSet<(string Type, string Property)> KnownExceptions = new()
    {
        ("VirtualListElement", "Ref"),
        ("XamlHostElement", "Updater"),
    };

    [Fact]
    public void EveryCallbackPropertyHasMatchingFluent()
    {
        var reactor = typeof(Element).Assembly;
        var elementRecords = reactor.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(Element).IsAssignableFrom(t))
            // Records produce nested compiler types; filter those out.
            .Where(t => t.FullName is not null && !t.FullName.Contains('+'))
            // Public-API surface guard — only walk publicly-visible element
            // records. Internal element shapes (overlay primitives, splitter
            // wires, etc.) drive UI through the public elements that compose
            // them, so callers can't bind to their callbacks directly and
            // don't need fluent extensions.
            .Where(t => t.IsPublic)
            .ToList();

        // Index of every public static extension method in the Reactor assembly,
        // keyed by (method name, first-parameter erased generic type definition or runtime type).
        // We include all such methods so the search across generic and non-generic
        // declaring types is uniform.
        var extensions = reactor.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true }) // static class
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.IsDefined(typeof(ExtensionAttribute), false))
            .ToList();

        var failures = new List<string>();

        foreach (var record in elementRecords.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            foreach (var prop in record.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsCallbackProperty(prop, out var delegateType)) continue;
                if (KnownExceptions.Contains((record.Name, prop.Name))) continue;

                // Spec convention: drop the leading "On" from the property name.
                // Known exceptions (none today) would have the property name == fluent name.
                var fluentName = prop.Name.StartsWith("On", StringComparison.Ordinal) && prop.Name.Length > 2
                    ? prop.Name[2..]
                    : prop.Name;

                if (!TryFindFluent(extensions, record, prop, fluentName, delegateType, out var why))
                {
                    failures.Add(
                        $"{record.Name}.{prop.Name} ({FormatDelegateType(delegateType)}) — " +
                        $"expected fluent `.{fluentName}(this {SimpleName(record)}, {FormatDelegateType(delegateType)}?)`: {why}");
                }
            }
        }

        if (failures.Count > 0)
        {
            var msg = new StringBuilder();
            msg.AppendLine($"Phase 11.1 surface guard: {failures.Count} callback property(ies) without a matching fluent.");
            msg.AppendLine();
            foreach (var f in failures.OrderBy(s => s, StringComparer.Ordinal))
                msg.AppendLine("  - " + f);
            msg.AppendLine();
            msg.AppendLine("Fix: add the extension in ElementExtensions.Events.cs following the");
            msg.AppendLine("`el with { OnX = handler }` pattern. See spec 039 §0.1 / §14 #1 / §15 Q1.");
            Assert.Fail(msg.ToString());
        }
    }

    /// <summary>
    /// Returns true if <paramref name="prop"/> is a callback (Action /
    /// Action&lt;T&gt;/Action&lt;T1,T2&gt;/Action&lt;T1,T2,T3&gt;) and emits
    /// the underlying delegate type via <paramref name="delegateType"/>.
    /// Nullable-annotated delegates unwrap to their non-null form.
    /// </summary>
    private static bool IsCallbackProperty(PropertyInfo prop, out Type delegateType)
    {
        var t = prop.PropertyType;
        delegateType = t;
        // Records project nullable-ref-typed callback fields as the same Type;
        // the annotation lives in NullableContextAttribute metadata, which we
        // do not need to inspect — `with { On = null }` works regardless.
        if (t == typeof(Action)) return true;
        if (!t.IsGenericType) return false;
        var def = t.GetGenericTypeDefinition();
        return def == typeof(Action<>)
            || def == typeof(Action<,>)
            || def == typeof(Action<,,>);
    }

    private static bool TryFindFluent(
        IReadOnlyCollection<MethodInfo> extensions,
        Type elementRecord,
        PropertyInfo prop,
        string fluentName,
        Type expectedDelegate,
        out string why)
    {
        // Pull candidates by name first; that's already a tight filter.
        var byName = extensions.Where(m => m.Name == fluentName).ToList();
        if (byName.Count == 0)
        {
            why = $"no extension method named `{fluentName}` found in the assembly.";
            return false;
        }

        foreach (var m in byName)
        {
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;

            // First parameter: the receiver type. For generic records (e.g. ItemsViewElement<T>),
            // the extension is typically generic too, so we compare against the open generic.
            var receiverParam = ps[0].ParameterType;
            if (!ReceiverMatches(receiverParam, elementRecord)) continue;

            // Second parameter: the handler delegate. For generic extensions the
            // method's generic arguments need to be substituted to match the property's
            // (closed) delegate type when the record is closed-generic; for the
            // open-generic case (records declared with their type parameter open) it's
            // sufficient that the open delegate shape matches.
            var handlerParam = ps[1].ParameterType;
            if (DelegateShapeMatches(handlerParam, expectedDelegate))
            {
                why = "";
                return true;
            }
        }

        why = $"found {byName.Count} candidate(s) named `{fluentName}` but none had a matching " +
              $"(this {SimpleName(elementRecord)}, {FormatDelegateType(expectedDelegate)}?) signature.";
        return false;
    }

    private static bool ReceiverMatches(Type extensionReceiver, Type elementRecord)
    {
        if (extensionReceiver == elementRecord) return true;

        // Open-generic record (e.g. ItemsViewElement<T>): the property is
        // declared on the open generic Type, and the extension's receiver
        // is also open-generic. Compare the generic type definitions.
        if (elementRecord.IsGenericTypeDefinition && extensionReceiver.IsGenericType)
        {
            if (extensionReceiver.GetGenericTypeDefinition() == elementRecord) return true;
        }
        if (extensionReceiver.IsGenericType && elementRecord.IsGenericType
            && extensionReceiver.GetGenericTypeDefinition() == elementRecord.GetGenericTypeDefinition())
            return true;

        // Allow derived types (e.g. TemplatedListElementBase descendants).
        if (extensionReceiver.IsAssignableFrom(elementRecord)) return true;

        return false;
    }

    private static bool DelegateShapeMatches(Type extensionHandler, Type propertyDelegate)
    {
        // Generic-parameter wildcards (the T from the extension's receiver
        // type or the property's owning record) match anything — those are
        // the open positions that the receiver-match resolves separately.
        if (extensionHandler.IsGenericParameter || propertyDelegate.IsGenericParameter)
            return true;

        if (extensionHandler == propertyDelegate) return true;

        // Both must be generic types for a structural comparison; otherwise
        // a plain `Action` (non-generic) compared to `Action<T>` is a mismatch.
        if (!extensionHandler.IsGenericType || !propertyDelegate.IsGenericType) return false;

        if (extensionHandler.GetGenericTypeDefinition() != propertyDelegate.GetGenericTypeDefinition()) return false;

        var lhs = extensionHandler.GetGenericArguments();
        var rhs = propertyDelegate.GetGenericArguments();
        if (lhs.Length != rhs.Length) return false;

        // Recursively compare each generic argument so a delegate with the
        // wrong payload (Action<IReadOnlySet<RowKey>> vs
        // Action<IReadOnlyList<int>>) is rejected even though both are
        // Action<> with arity 1.
        for (int i = 0; i < lhs.Length; i++)
            if (!DelegateShapeMatches(lhs[i], rhs[i])) return false;
        return true;
    }

    private static string SimpleName(Type t)
    {
        if (!t.IsGenericType) return t.Name;
        var stem = t.Name;
        var tick = stem.IndexOf('`');
        if (tick >= 0) stem = stem[..tick];
        return stem + "<" + string.Join(",", t.GetGenericArguments().Select(a => a.Name)) + ">";
    }

    private static string FormatDelegateType(Type t)
    {
        if (t == typeof(Action)) return "Action";
        if (!t.IsGenericType) return t.Name;
        var stem = t.Name;
        var tick = stem.IndexOf('`');
        if (tick >= 0) stem = stem[..tick];
        return stem + "<" + string.Join(",", t.GetGenericArguments().Select(a => a.IsGenericParameter ? a.Name : SimpleName(a))) + ">";
    }
}
