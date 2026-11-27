using System.Text;
using Microsoft.UI.Reactor.Analyzers.Modifiers;

namespace Microsoft.UI.Reactor.ModifierGen;

/// <summary>
/// Spec 067 §4.3: turns <see cref="ModifierSchema"/> into checked-in C#. Each output is a
/// repo-relative path and its full text with <c>\n</c> line endings. <c>Program</c> writes them, and
/// <c>ModifierMatrixSchemaAgreementTests.Generated_Files_Are_Fresh</c> (Reactor.Tests) compares them
/// with the checked-in files, ignoring line endings.
/// </summary>
internal static class ModifierEmitter
{
    internal const string ProbeTablePath = "tests/Reactor.AppTests.Host/SelfTest/ModifierMatrix/ModifierProbes.Generated.cs";

    internal const string ElementModifiersPath = "src/Reactor/Core/Element.Modifiers.Generated.cs";
    internal const string AppliedModifierMapPath = "src/Reactor/Core/Diagnostics/AppliedModifierMap.Generated.cs";
    internal const string ThemeBindingsPath = "src/Reactor/Core/ThemeBindingApplier.Generated.cs";
    internal const string ReconcilerModifiersPath = "src/Reactor/Core/Reconciler.Modifiers.Generated.cs";

    /// <summary>Every generated file.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> EmitAll() =>
    [
        (ProbeTablePath, EmitProbeTable()),
        (ElementModifiersPath, EmitElementModifiers()),
        (AppliedModifierMapPath, EmitAppliedModifierMap()),
        (ThemeBindingsPath, EmitThemeBindings()),
        (ReconcilerModifiersPath, EmitReconcilerModifiers()),
    ];

    /// <summary>One <c>ApplyModifiers</c> family helper layer 3 generates whole.</summary>
    /// <param name="Name">The helper's name. It keeps its call site in <c>ApplyModifiers</c>, so the apply order does not move.</param>
    /// <param name="Summary">Its doc summary.</param>
    /// <param name="First">The first <see cref="ModifierSchema.Slots"/> entry it writes.</param>
    /// <param name="Last">The last; every slot between them, in schema (apply) order, belongs to the helper.</param>
    /// <param name="SharedGate">
    /// Slots gated on one receiver type that share a single <c>if (fe is T Variable) { … }</c> block,
    /// as the hand-written helper does.
    /// </param>
    /// <param name="InlineGates">
    /// Slots gated on one receiver type whose arms carry the type test in each condition,
    /// <c>… &amp;&amp; fe is T Set</c> and <c>… &amp;&amp; fe is T Clear</c> (issue #986). A gated slot must be
    /// declared in exactly one of the two; the shape is never inferred. The pattern-variable names
    /// are declared rather than derived so the output stays token-identical to the hand-written
    /// arms it replaces, which is what reviewers diff it against.
    /// </param>
    /// <param name="ReceiverChains">
    /// Slots written to several receivers, tried in apply order inside the set and the reset arm:
    /// <c>if (fe is T1 v1) … else if (fe is T2 v2) …</c>. Each is declared with one pattern-variable
    /// name per Apply receiver, in schema order.
    /// </param>
    internal sealed record GeneratedHelper(string Name, string Summary, string First, string Last,
        (string Variable, string[] Slots)? SharedGate = null,
        IReadOnlyDictionary<string, (string Set, string Clear)>? InlineGates = null,
        IReadOnlyDictionary<string, string[]>? ReceiverChains = null);

    /// <summary>The helpers layer 3 generates (spec 067 §11 3a–3e), one family PR at a time.</summary>
    internal static readonly GeneratedHelper[] GeneratedHelpers =
    [
        new("ApplySizeAndAlignmentModifiers", "Width/Height, Min/Max sizes, and the (content) alignments.",
            First: "Width", Last: "VerticalContentAlignment",
            SharedGate: ("contentAlignmentControl", ["HorizontalContentAlignment", "VerticalContentAlignment"])),
        new("ApplyAutomationAndFocusModifiers", "AutomationName/Id, IsDragRegion, ElementSoundMode, and the Tier 1 accessibility and focus properties.",
            First: "AutomationName", Last: "XYFocusKeyboardNavigation",
            InlineGates: new Dictionary<string, (string Set, string Clear)>
            {
                ["ElementSoundMode"] = ("ctrl", "ctrlSnd"),
                ["TabIndex"] = ("tabIdxCtrl", "tabIdxCtrl2"),
            }),
        new("ApplyTypographyModifiers", "FontFamily, FontSize, and FontWeight.",
            First: "FontFamily", Last: "FontWeight",
            ReceiverChains: new Dictionary<string, string[]>
            {
                ["FontFamily"] = ["ffCtrl", "ffTb"],
                ["FontSize"] = ["fsCtrl", "fsTb"],
                ["FontWeight"] = ["fwCtrl", "fwTb"],
            }),
        new("ApplyIsEnabledAndCornerRadiusModifiers", "IsEnabled and CornerRadius.",
            First: "IsEnabled", Last: "CornerRadius",
            InlineGates: new Dictionary<string, (string Set, string Clear)> { ["IsEnabled"] = ("enCtrl", "enCtrl2") },
            ReceiverChains: new Dictionary<string, string[]>
            {
                ["CornerRadius"] = ["crCtrl", "crBdr", "crGrid", "crStack", "crRelative"],
            }),
    ];

    /// <summary>
    /// Spec 067 §11 layer 3: the row A, B and D apply arms, in today's shape (§7): an ungated
    /// set/unset pair on <c>fe</c> (a property write, or an attached owner's <c>SetX</c>), a declared
    /// <see cref="GeneratedHelper.SharedGate"/> run inside one <c>if (fe is T x) { … }</c> block, a
    /// declared <see cref="GeneratedHelper.InlineGates"/> arm, or a declared
    /// <see cref="GeneratedHelper.ReceiverChains"/> arm. Anything else (converted, hand-coded, or a
    /// gated slot or chain left undeclared) throws, so a family whose arms look different today gets
    /// its emitter support in its own PR instead of being silently reshaped.
    /// </summary>
    internal static string EmitReconcilerModifiers() => EmitReconcilerModifiers(GeneratedHelpers);

    /// <summary>Emits <paramref name="helpers"/>; tests pass invalid declarations to pin the validation.</summary>
    internal static string EmitReconcilerModifiers(IReadOnlyList<GeneratedHelper> helpers)
    {
        var sb = new StringBuilder();
        sb.Append("// <auto-generated>\n");
        sb.Append("// Generated by tools/Reactor.ModifierGen from src/Reactor.Analyzers/Modifiers/ModifierSchema.cs.\n");
        sb.Append("// Do not edit. Regenerate with: dotnet run --project tools/Reactor.ModifierGen\n");
        sb.Append("// </auto-generated>\n");
        sb.Append("#nullable enable\n\n");
        sb.Append("using Microsoft.UI.Xaml;\n");
        sb.Append("using WinUI = Microsoft.UI.Xaml.Controls;\n\n");
        sb.Append("namespace Microsoft.UI.Reactor.Core;\n\n");
        sb.Append("public sealed partial class Reconciler\n{");
        foreach (var helper in helpers)
        {
            var first = Array.FindIndex(ModifierSchema.Slots, s => s.Name == helper.First);
            var last = Array.FindIndex(ModifierSchema.Slots, s => s.Name == helper.Last);
            if (first < 0 || last < first)
                throw new InvalidOperationException($"{helper.Name}: First '{helper.First}' and Last '{helper.Last}' must name slots in ModifierSchema.Slots, in that order.");
            var slots = ModifierSchema.Slots.Skip(first).Take(last - first + 1).ToList();
            var shared = helper.SharedGate?.Slots ?? [];
            var inline = helper.InlineGates ?? new Dictionary<string, (string Set, string Clear)>();
            var chains = helper.ReceiverChains ?? new Dictionary<string, string[]>();
            foreach (var slot in slots)
            {
                if (slot.Shape is not (ApplyShape.Simple or ApplyShape.Attached))
                    throw new InvalidOperationException($"{helper.Name}: {slot.Name} is {slot.Shape}, and only Simple and Attached slots are emitted so far. "
                        + "Add its shape to EmitReconcilerModifiers in the PR that generates it, or end the helper's slot range before it.");
                if (slot.Write == WriteEquality.Reference && !ReferenceTypes.Contains(slot.ClrType))
                    throw new InvalidOperationException($"{helper.Name}: {slot.Name} compares by reference, but {slot.ClrType} is not in ReferenceTypes. "
                        + "Add it there if it is a reference type.");
                var receivers = slot.ReceiversWith(ReceiverRole.Apply).ToList();
                var inGate = shared.Contains(slot.Name) || inline.ContainsKey(slot.Name);
                if (receivers.Count > 1 || chains.ContainsKey(slot.Name))
                {
                    if (!chains.TryGetValue(slot.Name, out var names) || names.Length != receivers.Count || receivers.Count < 2 || inGate)
                        throw new InvalidOperationException($"{helper.Name}: {slot.Name} has {receivers.Count} Apply receiver{(receivers.Count == 1 ? "" : "s")} ({string.Join(", ", receivers.Select(r => r.SimpleType))}). "
                            + "A chain of two or more is declared in ReceiverChains only, with one pattern-variable name per receiver in schema order.");
                    continue;
                }
                var receiver = receivers.Single();
                var declared = (shared.Contains(slot.Name) ? 1 : 0) + (inline.ContainsKey(slot.Name) ? 1 : 0);
                if ((receiver.Type == ModifierSchema.FrameworkElement) != (declared == 0) || declared > 1)
                    throw new InvalidOperationException(receiver.Type == ModifierSchema.FrameworkElement
                        ? $"{helper.Name}: {slot.Name} is ungated, so it cannot be in SharedGate or InlineGates."
                        : $"{helper.Name}: {slot.Name} is gated on {receiver.SimpleType}; declare its arm shape in exactly one of SharedGate or InlineGates.");
            }
            foreach (var name in shared.Concat(inline.Keys).Concat(chains.Keys))
                if (!slots.Any(s => s.Name == name))
                    throw new InvalidOperationException($"{helper.Name}: '{name}' is declared as gated but is not in the helper's slot range.");

            sb.Append("\n    /// <summary>").Append(helper.Summary).Append("</summary>\n");
            sb.Append("    private static void ").Append(helper.Name).Append("(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)\n    {\n");
            for (var i = 0; i < slots.Count;)
            {
                if (i > 0) sb.Append('\n');
                var slot = slots[i];
                var (set, unset, value) = Guards(slot);
                var indent = "        ";
                var armSeparator = "\n" + indent + "    ";
                if (chains.TryGetValue(slot.Name, out var chainNames))
                {
                    var chain = slot.ReceiversWith(ReceiverRole.Apply).ToList();
                    sb.Append(indent).Append("if (").Append(set).Append(")\n");
                    AppendChain(sb, indent, chain, chainNames, (r, v) => $"{v}.{r.Property} = {value}");
                    sb.Append(indent).Append("else if (").Append(unset).Append(")\n");
                    AppendChain(sb, indent, chain, chainNames, (r, v) => Clear(r, r.Type, v));
                    i++;
                    continue;
                }
                var receiver = slot.ReceiversWith(ReceiverRole.Apply).Single();
                if (receiver.Type == ModifierSchema.FrameworkElement)
                {
                    sb.Append(indent).Append("if (").Append(set).Append(')').Append(armSeparator).Append(Write(slot, receiver, "fe", value)).Append(";\n");
                    sb.Append(indent).Append("else if (").Append(unset).Append(')').Append(armSeparator).Append(Clear(receiver, receiver.DpOwner, "fe")).Append(";\n");
                    i++;
                    continue;
                }
                if (inline.TryGetValue(slot.Name, out var vars))
                {
                    var type = TypeRef(receiver.Type);
                    sb.Append(indent).Append("// The reset arm re-tests the type under its own variable: reaching the else only proves the\n");
                    sb.Append(indent).Append("// set condition was false, which every non-").Append(receiver.SimpleType).Append(" satisfies, and dropping the modifier\n");
                    sb.Append(indent).Append("// must not clear a value a non-").Append(receiver.SimpleType).Append(" carries on its own (issue #986).\n");
                    sb.Append(indent).Append("if (").Append(set).Append(" && fe is ").Append(type).Append(' ').Append(vars.Set).Append(')').Append(armSeparator)
                        .Append(vars.Set).Append('.').Append(receiver.Property).Append(" = ").Append(value).Append(";\n");
                    sb.Append(indent).Append("else if (").Append(unset).Append(" && fe is ").Append(type).Append(' ').Append(vars.Clear).Append(')').Append(armSeparator)
                        .Append(Clear(receiver, receiver.Type, vars.Clear)).Append(";\n");
                    i++;
                    continue;
                }

                var run = slots.Skip(i).TakeWhile(s => shared.Contains(s.Name)).ToList();
                if (run.Count != shared.Length || run.Any(s => s.ReceiversWith(ReceiverRole.Apply).Single().Type != receiver.Type))
                    throw new InvalidOperationException($"{helper.Name}: the SharedGate slots must be consecutive and gated on one receiver type.");
                var variable = helper.SharedGate!.Value.Variable;
                sb.Append(indent).Append("if (fe is ").Append(TypeRef(receiver.Type)).Append(' ').Append(variable).Append(")\n").Append(indent).Append("{\n");
                for (var j = 0; j < run.Count; j++)
                {
                    var gated = run[j].ReceiversWith(ReceiverRole.Apply).Single();
                    var (gSet, gUnset, gValue) = Guards(run[j]);
                    if (j > 0) sb.Append('\n');
                    sb.Append("            if (").Append(gSet).Append(")\n                ").Append(variable).Append('.').Append(gated.Property).Append(" = ").Append(gValue).Append(";\n");
                    sb.Append("            else if (").Append(gUnset).Append(")\n                ").Append(Clear(gated, gated.Type, variable)).Append(";\n");
                }
                sb.Append(indent).Append("}\n");
                i += run.Count;
            }
            sb.Append("    }\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    // Reference-typed field types; the rest are value types read through Nullable<T>.
    private static readonly HashSet<string> ReferenceTypes = new(StringComparer.Ordinal) { "string", "Microsoft.UI.Xaml.Media.FontFamily" };

    // The changed / released guards and the value expression of a set/unset pair (spec 067 §6.6).
    // A reference-compared field (WriteEquality.Reference) is changed when it is a different instance.
    private static (string Set, string Unset, string Value) Guards(ModifierSlot slot)
    {
        var x = slot.Name;
        if (!ReferenceTypes.Contains(slot.ClrType))
            return ($"m.{x}.HasValue && m.{x} != oldM?.{x}", $"!m.{x}.HasValue && oldM?.{x}.HasValue == true", $"m.{x}.Value");
        var changed = slot.Write == WriteEquality.Reference ? $"!ReferenceEquals(m.{x}, oldM?.{x})" : $"m.{x} != oldM?.{x}";
        return ($"m.{x} is not null && {changed}", $"m.{x} is null && oldM?.{x} is not null", $"m.{x}");
    }

    // One arm body of a receiver chain: the first receiver the element is wins.
    private static void AppendChain(StringBuilder sb, string indent, List<ModifierReceiver> chain, string[] names, Func<ModifierReceiver, string, string> body)
    {
        sb.Append(indent).Append("{\n");
        for (var k = 0; k < chain.Count; k++)
        {
            sb.Append(indent).Append("    ").Append(k > 0 ? "else if (fe is " : "if (fe is ").Append(TypeRef(chain[k].Type)).Append(' ').Append(names[k]).Append(") ")
                .Append(body(chain[k], names[k])).Append(";\n");
        }
        sb.Append(indent).Append("}\n");
    }

    private static string Write(ModifierSlot slot, ModifierReceiver receiver, string target, string value) =>
        slot.AttachedOwner is { } owner
            ? $"{TypeRef(owner)}.Set{receiver.Property}({target}, {value})"
            : $"{target}.{receiver.Property} = {value}";

    // An ungated arm clears the DP through its declaring type; a gated one through the receiver it
    // tested (WinUI.Control.TabIndexProperty resolves to UIElement's), as the hand-written arms do.
    private static string Clear(ModifierReceiver receiver, string owner, string target) =>
        $"{target}.ClearValue({TypeRef(owner)}.{receiver.Property}Property)";
    // The names the hand-written reconciler uses, through the generated file's usings: a type
    // directly in Microsoft.UI.Xaml.Controls as WinUI.X, directly in Microsoft.UI.Xaml as X.
    private static string TypeRef(string fullName)
    {
        var ns = fullName[..fullName.LastIndexOf('.')];
        return ns switch
        {
            "Microsoft.UI.Xaml.Controls" => "WinUI." + Simple(fullName),
            "Microsoft.UI.Xaml" => Simple(fullName),
            _ => fullName,
        };
    }
    private const string ControlsNamespace = "Microsoft.UI.Xaml.Controls.";

    /// <summary>
    /// Spec 067 §11 layer 2: <c>AppliedModifierMap.Entries</c> and its owner helpers. A slot is
    /// reported against its <see cref="ModifierSlot.ReportTarget"/>, its hand-written owner, or its
    /// Apply receivers: an ungated <c>FrameworkElement</c> receiver as <c>&lt;DpOwner&gt;.&lt;Property&gt;</c>, a
    /// gated chain as <c>&lt;Receiver&gt;.&lt;Property&gt;</c> tested in apply order. Entries follow
    /// <see cref="ModifierSchema.AppliedReportOrder"/>.
    /// </summary>
    internal static string EmitAppliedModifierMap()
    {
        var reported = ModifierSchema.Slots
            .Where(s => s.ReportTarget is not null || s.ReportHandWritten || s.ReceiversWith(ReceiverRole.Apply).Any())
            .ToDictionary(s => s.Name, StringComparer.Ordinal);
        var order = ModifierSchema.AppliedReportOrder;
        var missing = reported.Keys.Except(order).ToList();
        var extra = order.Except(reported.Keys).ToList();
        var duplicated = order.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (missing.Count > 0 || extra.Count > 0 || duplicated.Count > 0)
            throw new InvalidOperationException(
                $"ModifierSchema.AppliedReportOrder must list every reported slot once. Missing: [{string.Join(", ", missing)}]; "
                + $"not reported: [{string.Join(", ", extra)}]; duplicated: [{string.Join(", ", duplicated)}].");

        var sb = Header("Microsoft.UI.Reactor.Core.Diagnostics");
        sb.Append("internal static partial class AppliedModifierMap\n{\n");
        var owners = new List<(string Name, List<ModifierReceiver> Chain)>();
        var entries = new List<string>();
        foreach (var slot in order.Select(name => reported[name]))
        {
            var modifier = slot.Bucket == ModifierBucket.Accessibility ? "Accessibility." + slot.Name : slot.Name;
            var read = slot.Bucket == ModifierBucket.Accessibility ? $"m.Accessibility?.{slot.Name}" : $"m.{slot.Name}";
            if (slot.ReportSuppressedBy is { } suppressor)
                read = $"m.{suppressor} is null ? {read} : null";
            entries.Add($"new(\"{modifier}\", m => {read}, {Owner(slot, owners)}),");
        }
        sb.Append("    internal static readonly Entry[] Entries =\n    [\n");
        foreach (var entry in entries) sb.Append("        ").Append(entry).Append('\n');
        sb.Append("    ];\n");
        foreach (var (ownerName, chain) in owners)
        {
            sb.Append("\n    private static string? ").Append(ownerName).Append("(global::System.Type t) =>\n        ");
            foreach (var receiver in chain)
                sb.Append("Is<global::").Append(receiver.Type).Append(">(t) ? \"").Append(receiver.SimpleType).Append('.')
                    .Append(receiver.Property).Append("\"\n        : ");
            sb.Append("null;\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string Owner(ModifierSlot slot, List<(string Name, List<ModifierReceiver> Chain)> owners)
    {
        if (slot.ReportTarget is { } target) return $"Always(\"{target}\")";
        if (slot.ReportHandWritten) return slot.Name + "Owner";
        var receivers = slot.ReceiversWith(ReceiverRole.Apply).ToList();
        if (receivers is [{ Type: ModifierSchema.FrameworkElement } only])
            return $"Always(\"{Simple(only.DpOwner)}.{only.Property}\")";
        if (receivers.Any(r => r.Type == ModifierSchema.FrameworkElement || !r.Type.StartsWith(ControlsNamespace, StringComparison.Ordinal)))
            throw new InvalidOperationException($"{slot.Name}: a gated report chain must name Microsoft.UI.Xaml.Controls receivers only. "
                + "Declare .Report(target: \"Owner.Property\") on the slot, or .Report(handWritten: true) and write "
                + $"{slot.Name}Owner in AppliedModifierMap.cs.");
        var key = string.Join("|", receivers.Select(r => r.Type + "." + r.Property));
        foreach (var (name, chain) in owners)
            if (string.Join("|", chain.Select(r => r.Type + "." + r.Property)) == key)
                return name;
        owners.Add((slot.Name + "Owner", receivers));
        return slot.Name + "Owner";
    }

    /// <summary>
    /// Spec 067 §11 layer 2: the common-modifier rows of <c>GetDependencyPropertyName</c>, from the
    /// receivers with the <see cref="ReceiverRole.Theme"/> role. The element-specific rows
    /// (<c>PaneBackground</c>) stay hand-written in <c>ThemeBindingApplier</c>.
    /// </summary>
    internal static string EmitThemeBindings()
    {
        var themed = ModifierSchema.Slots.Where(s => s.ReceiversWith(ReceiverRole.Theme).Any()).ToList();
        if (themed.Count == 0) throw new InvalidOperationException("No Theme-role receivers: the ThemeRef path would reach nothing.");
        foreach (var slot in themed)
            if (slot.ReceiversWith(ReceiverRole.Theme).Any(r => r.Property != slot.Name))
                throw new InvalidOperationException($"{slot.Name}: a ThemeRef binding names the property it sets, so every Theme receiver must write {slot.Name}.");

        var sb = Header("Microsoft.UI.Reactor.Core");
        sb.Append("internal static partial class ThemeBindingApplier\n{\n");
        sb.Append("    private static string? GetModifierDependencyPropertyName(global::Microsoft.UI.Xaml.FrameworkElement fe, string property)\n    {\n");
        foreach (var slot in themed)
        {
            sb.Append("        if (property == \"").Append(slot.Name).Append("\" && (")
                .Append(string.Join(" || ", slot.ReceiversWith(ReceiverRole.Theme).Select(r => "fe is global::" + r.Type)))
                .Append("))\n            return \"").Append(slot.Name).Append("\";\n");
        }
        sb.Append("        return null;\n    }\n}\n");
        return sb.ToString();
    }

    private static StringBuilder Header(string ns)
    {
        var sb = new StringBuilder();
        sb.Append("// <auto-generated>\n");
        sb.Append("// Generated by tools/Reactor.ModifierGen from src/Reactor.Analyzers/Modifiers/ModifierSchema.cs.\n");
        sb.Append("// Do not edit. Regenerate with: dotnet run --project tools/Reactor.ModifierGen\n");
        sb.Append("// </auto-generated>\n");
        sb.Append("#nullable enable\n\n");
        sb.Append("namespace ").Append(ns).Append(";\n\n");
        return sb;
    }

    /// <summary>The slots the §10.2 matrix covers: rows A–D (simple, converted, attached).</summary>
    internal static IEnumerable<ModifierSlot> MatrixSlots() =>
        ModifierSchema.Slots.Where(s => s.Shape is ApplyShape.Simple or ApplyShape.Converted or ApplyShape.Attached);

    /// <summary>
    /// Spec 067 §8 / §10.2: a typed probe per (slot, Apply receiver) — the receiver type, the DP the
    /// schema says it writes, and a setter that puts a value into an <c>ElementModifiers</c> — so the
    /// matrix fixture needs no reflection and runs in the NativeAOT selftest shards too. The fixture
    /// checks each probe's DP name against its own frozen matrix before using it.
    /// </summary>
    internal static string EmitProbeTable()
    {
        var sb = new StringBuilder();
        sb.Append("// <auto-generated>\n");
        sb.Append("// Generated by tools/Reactor.ModifierGen from src/Reactor.Analyzers/Modifiers/ModifierSchema.cs.\n");
        sb.Append("// Do not edit. Regenerate with: dotnet run --project tools/Reactor.ModifierGen\n");
        sb.Append("// </auto-generated>\n");
        sb.Append("#nullable enable\n\n");
        sb.Append("namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.ModifierMatrix;\n\n");
        sb.Append("internal static partial class ModifierProbes\n{\n");
        sb.Append("    internal static readonly ModifierProbe[] All =\n    [\n");
        foreach (var slot in MatrixSlots())
        {
            foreach (var receiver in slot.ReceiversWith(ReceiverRole.Apply))
            {
                sb.Append("        new(\"").Append(slot.Name).Append("\", typeof(global::").Append(receiver.Type).Append("), \"")
                    .Append(Simple(receiver.DpOwner)).Append('.').Append(receiver.Property).Append("Property\",\n");
                sb.Append("            static () => global::").Append(receiver.DpOwner).Append('.').Append(receiver.Property).Append("Property,\n");
                sb.Append("            static (m, v) => ").Append(WithExpression(slot)).Append(",\n");
                sb.Append("            ").Append(slot.Converter is null ? "null" : "\"" + slot.Converter + "\"").Append("),\n");
            }
        }
        sb.Append("    ];\n}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Spec 067 §11 layer 2: the shallow-skip comparison (<c>ModifierEquality</c>) and the four
    /// <c>Merge</c> bodies. Same shape as the hand-written code they replace (§7): the Layout and
    /// Accessibility buckets compare by record <c>Equals</c>, Visual and root fields one by one,
    /// and the routed handlers through <c>ModifierCallbacksEqual</c>. A schema declaration that
    /// shape cannot express throws here rather than emitting something different.
    /// </summary>
    internal static string EmitElementModifiers()
    {
        var root = Bucket(ModifierBucket.Root);
        var layout = Bucket(ModifierBucket.Layout);
        var visual = Bucket(ModifierBucket.Visual);
        var accessibility = Bucket(ModifierBucket.Accessibility);

        Require(layout, s => s.Skip == SkipEquality.Value, "Layout compares by record Equals, so every Layout slot must be SkipEquality.Value");
        // Record Equals compares the ref fields with their own Equals, which for ElementRef and the
        // ref lists is reference equality, so Reference is what record Equals already does.
        Require(accessibility, s => s.Skip is SkipEquality.Value or SkipEquality.Reference,
            "Accessibility compares by record Equals, so its slots must be SkipEquality.Value or Reference");
        Require(visual, s => s.Skip is SkipEquality.Value or SkipEquality.BrushValue, "Visual slots compare with == or BrushesEqual");
        Require(root, s => s.Skip != SkipEquality.BrushValue, "Root slots hold no brushes");
        // A callback compares by reference and is ignored in visible mode; any other declaration
        // would be emitted with a different meaning, so refuse it.
        Require(root, s => !s.CallbackSlot || s.Skip == SkipEquality.Reference, "Callback slots compare by reference");
        Require(layout.Concat(visual).Concat(accessibility).ToList(), s => !s.CallbackSlot,
            "Callback slots live on the root bag; record Equals would compare them in visible mode too");

        var sb = new StringBuilder();
        sb.Append("// <auto-generated>\n");
        sb.Append("// Generated by tools/Reactor.ModifierGen from src/Reactor.Analyzers/Modifiers/ModifierSchema.cs.\n");
        sb.Append("// Do not edit. Regenerate with: dotnet run --project tools/Reactor.ModifierGen\n");
        sb.Append("// </auto-generated>\n");
        sb.Append("#nullable enable\n\n");
        sb.Append("namespace Microsoft.UI.Reactor.Core;\n\n");

        // ── Skip equality ──
        sb.Append("internal static partial class ModifierEquality\n{\n");
        sb.Append("    internal static partial bool ModifiersEqualCore(ElementModifiers? a, ElementModifiers? b, bool visibleOnly)\n    {\n");
        sb.Append("        if (ReferenceEquals(a, b)) return true;\n");
        sb.Append("        if (a is null || b is null)\n        {\n");
        sb.Append("            if (!visibleOnly) return false;\n");
        sb.Append("            a ??= EmptyModifiers;\n            b ??= EmptyModifiers;\n        }\n\n");
        sb.Append("        var al = a.Layout ?? EmptyLayout;\n        var bl = b.Layout ?? EmptyLayout;\n");
        sb.Append("        if (!ReferenceEquals(al, bl) && !al.Equals(bl)) return false;\n\n");
        sb.Append("        if (!VisualModifiersEqual(a.Visual, b.Visual)) return false;\n\n");
        var notCompared = root.Where(s => s.Skip == SkipEquality.NotCompared).Select(s => s.Name).ToList();
        if (notCompared.Count > 0)
            sb.Append("        // Not compared (ModifierSlot.SkipReason): ").Append(string.Join(", ", notCompared)).Append(".\n");
        var terms = new List<string>();
        var handlersPlaced = false;
        foreach (var slot in root)
        {
            if (slot.CallbackSlot && slot.Family == ModifierFamily.Behavior)
            {
                if (!handlersPlaced) terms.Add("(visibleOnly || ModifierCallbacksEqual(a, b))");
                handlersPlaced = true;
                continue;
            }
            var term = slot.Skip switch
            {
                SkipEquality.NotCompared => null,
                SkipEquality.Value => $"a.{slot.Name} == b.{slot.Name}",
                SkipEquality.FontFamilyValue => $"Element.FontFamiliesEqual(a.{slot.Name}, b.{slot.Name})",
                SkipEquality.Reference when slot.CallbackSlot => $"(visibleOnly || ReferenceEquals(a.{slot.Name}, b.{slot.Name}))",
                SkipEquality.Reference => $"ReferenceEquals(a.{slot.Name}, b.{slot.Name})",
                SkipEquality.BlocksSkipWhenSet =>
                    $"(visibleOnly ? (a.{slot.Name} is null) == (b.{slot.Name} is null) : a.{slot.Name} is null && b.{slot.Name} is null)",
                _ => throw new InvalidOperationException($"{slot.Name}: no root comparison for {slot.Skip}."),
            };
            if (term is not null) terms.Add(term);
        }
        terms.Add("AccessibilityEqual(a.Accessibility, b.Accessibility)");
        sb.Append("        return ").Append(string.Join("\n            && ", terms)).Append(";\n    }\n\n");

        sb.Append("    private static partial bool VisualModifiersEqual(VisualModifiers? a, VisualModifiers? b)\n    {\n");
        sb.Append("        if (ReferenceEquals(a, b)) return true;\n");
        sb.Append("        a ??= EmptyVisual;\n        b ??= EmptyVisual;\n");
        sb.Append("        return ").Append(string.Join("\n            && ", visual.Select(s => s.Skip == SkipEquality.BrushValue
            ? $"BrushHelper.BrushesEqual(a.{s.Name}, b.{s.Name})"
            : $"a.{s.Name} == b.{s.Name}"))).Append(";\n    }\n\n");

        var handlers = root.Where(s => s.CallbackSlot && s.Family == ModifierFamily.Behavior).ToList();
        if (handlers.Count == 0) throw new InvalidOperationException("No routed-handler slots: ModifierCallbacksEqual would be empty.");
        sb.Append("    private static partial bool ModifierCallbacksEqual(ElementModifiers a, ElementModifiers b)\n    {\n");
        sb.Append("        return ").Append(string.Join("\n            && ", handlers.Select(s => $"ReferenceEquals(a.{s.Name}, b.{s.Name})"))).Append(";\n    }\n}\n\n");

        // ── Merge ──
        sb.Append("public partial record ElementModifiers\n{\n");
        sb.Append("    public partial ElementModifiers Merge(ElementModifiers other)\n    {\n");
        sb.Append("        // Merge buckets at sub-record level. Naming a shim'd property inside the with { } here\n");
        sb.Append("        // would clone the bucket once per moved field (each shim's init re-runs).\n");
        sb.Append("        var mergedLayout = other.Layout is not null\n            ? (Layout is not null ? Layout.Merge(other.Layout) : other.Layout)\n            : Layout;\n");
        sb.Append("        var mergedVisual = other.Visual is not null\n            ? (Visual is not null ? Visual.Merge(other.Visual) : other.Visual)\n            : Visual;\n\n");
        sb.Append("        return this with\n        {\n");
        sb.Append("            Layout = mergedLayout,\n            Visual = mergedVisual,\n");
        foreach (var slot in root) sb.Append("            ").Append(MergeLine(slot)).Append('\n');
        sb.Append("            Accessibility = other.Accessibility is not null\n");
        sb.Append("                ? (Accessibility is not null ? Accessibility.Merge(other.Accessibility) : other.Accessibility)\n");
        sb.Append("                : Accessibility,\n        };\n    }\n}\n");
        EmitBucketMerge(sb, "LayoutModifiers", layout);
        EmitBucketMerge(sb, "VisualModifiers", visual);
        EmitBucketMerge(sb, "AccessibilityModifiers", accessibility);
        return sb.ToString();
    }

    private static void EmitBucketMerge(StringBuilder sb, string record, List<ModifierSlot> slots)
    {
        sb.Append("\npublic partial record ").Append(record).Append("\n{\n");
        sb.Append("    public partial ").Append(record).Append(" Merge(").Append(record).Append(" other) => this with\n    {\n");
        foreach (var slot in slots) sb.Append("        ").Append(MergeLine(slot)).Append('\n');
        sb.Append("    };\n}\n");
    }

    private static string MergeLine(ModifierSlot slot) => $"{slot.Name} = other.{slot.Name} ?? {slot.Name},";

    private static List<ModifierSlot> Bucket(ModifierBucket bucket) =>
        ModifierSchema.Slots.Where(s => s.Bucket == bucket).ToList();

    private static void Require(List<ModifierSlot> slots, Func<ModifierSlot, bool> ok, string rule)
    {
        foreach (var slot in slots)
            if (!ok(slot))
                throw new InvalidOperationException($"{slot.Name} ({slot.Bucket}, {slot.Skip}): {rule}. Change the emitter in the same PR as the behaviour.");
    }

    // `m with { X = (T?)v }` through the root shims; accessibility fields go through their bucket.
    private static string WithExpression(ModifierSlot slot)
    {
        var cast = "(" + Global(slot.ClrType) + "?)v";
        return slot.Bucket == ModifierBucket.Accessibility
            ? "m with { Accessibility = (m.Accessibility ?? new global::Microsoft.UI.Reactor.Core.AccessibilityModifiers()) with { "
                + slot.Name + " = " + cast + " } }"
            : "m with { " + slot.Name + " = " + cast + " }";
    }

    private static string Global(string clrType) =>
        clrType is "bool" or "int" or "double" or "float" or "string" or "object" ? clrType : "global::" + clrType;

    private static string Simple(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];
}
