using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// The syntactic shape a reset is written in. Kept on every <see cref="CleanElementReset"/>
/// because the three are not interchangeable to a reader: <see cref="ClearValue"/> restores the
/// dependency-property default and lets Style setters win again (issue #952), while
/// <see cref="Assignment"/> pins a hardcoded literal and <see cref="CollectionClear"/> empties a
/// live collection. All three drop whatever the previous renter left behind, which is the fact
/// the pool consistency invariants are stated over — so all three must be scanned.
/// </summary>
internal enum ResetShape
{
    /// <summary><c>receiver.ClearValue(Owner.PROPProperty)</c>.</summary>
    ClearValue,

    /// <summary><c>receiver.PROP = value</c> — e.g. <c>tb.FontSize = 14</c>.</summary>
    Assignment,

    /// <summary><c>receiver.PROP.Clear()</c> — e.g. <c>panel.Children.Clear()</c>.</summary>
    CollectionClear,
}

/// <summary>
/// One property reset performed by <c>ElementPool.CleanElement</c>, attributed to the WinUI type
/// it actually runs on.
/// </summary>
/// <param name="Receiver">
/// The simple name of the type the reset executes against — <c>FrameworkElement</c> for a
/// statement at method scope, otherwise the type bound by the enclosing <c>is</c> pattern or
/// <c>case</c> label. This is the fact <c>REACTOR_POOL_001</c>'s <c>poolResetGate</c> mirrors,
/// and the one nothing derived before issue #1193.
/// </param>
/// <param name="Owner">
/// The type that declares the dependency property, for <see cref="ResetShape.ClearValue"/> only;
/// <see langword="null"/> for the other two shapes, which name no owner. Needed to tell
/// <c>AutomationProperties.Name</c> from <c>FrameworkElement.Name</c>.
/// </param>
/// <param name="Property">The bare property name, as written inside a <c>.Set</c> lambda.</param>
/// <param name="Shape">Which of the three reset forms this is.</param>
internal readonly record struct CleanElementReset(
    string Receiver,
    string? Owner,
    string Property,
    ResetShape Shape)
{
    /// <summary>The <c>Receiver.Property</c> key the consistency invariants are stated over.</summary>
    public string Key => Receiver + "." + Property;

    public override string ToString() => Shape switch
    {
        ResetShape.ClearValue => $"{Receiver}: ClearValue({Owner}.{Property}Property)",
        ResetShape.CollectionClear => $"{Receiver}: {Property}.Clear()",
        _ => $"{Receiver}: {Property} = …",
    };
}

/// <summary>
/// Reads <em>every</em> property reset in <c>ElementPool.CleanElement</c> — the FE-common block
/// <b>and</b> the type-specific dispatch below it — and attributes each to the receiver type it
/// runs on.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the FE-common-block-only scan that both <c>PoolResetSetConsistencyTests</c> and
/// <c>ModifierUnsetClearValueTests</c> used to carry a copy of. That scan stopped at
/// <c>switch (fe)</c>, so every reset in a type-specific arm was invisible to every invariant
/// built on it — issue #1193. The blind spot was known and worked around rather than closed:
/// #985 and #950 <em>relocated</em> clears up into the FE-common block specifically so the scan
/// could see them, which works only for properties declared on a shared base. <c>TextBlock</c>'s
/// font and text DPs exist on <c>TextBlock</c> alone and cannot be relocated, so they stayed in
/// the <c>case TextBlock tb:</c> arm and were never checked against <c>ModifierTable</c>.
/// </para>
/// <para>
/// <b>Roslyn, not regex.</b> Its predecessors were regexes over raw file text, which forced them
/// to re-implement C# lexing badly: a hand-written comment/string blanker (this method's comments
/// discuss the very properties being scanned), a line anchor so a comment mentioning the dispatch
/// could not masquerade as the region boundary, and brace counting to delimit the body. Parsing
/// with <see cref="CSharpSyntaxTree"/> deletes all three problems — trivia is trivia, literals are
/// literals, and the body is a node — and it is what this assembly already does for the same class
/// of problem (<c>ModifierUnsetClearValueTests.ReconcilerRoot</c>, <c>ModifierTableIntegrityTests</c>).
/// It also removes categories of silent mis-parse the regex version had to special-case by
/// keyword: <c>if (fe.Style is not null)</c> is a <c>UnaryPattern</c>, not a declaration, so it
/// cannot be read as a type binding, and <c>a &gt;= b</c> can never be read as an assignment.
/// </para>
/// <para>
/// <b>How the receiver is resolved.</b> Every reset names its receiver as a local, and every local
/// in this method is introduced by exactly one binding: the method parameter, an <c>is T name</c>
/// pattern, or a <c>case T name:</c> label — both of the latter being a
/// <see cref="DeclarationPatternSyntax"/>. So the map from local name to type is recovered from the
/// bindings and each reset resolved through it. A receiver that resolves to nothing is reported
/// rather than defaulted (see
/// <c>CleanElementScanIntegrityTests.Every_Reset_Receiver_Resolves_To_A_Bound_Type</c>), because
/// defaulting is what would let a renamed local silently re-attribute a whole arm to
/// <c>FrameworkElement</c> and widen every derived gate.
/// </para>
/// <para>
/// <b>Direction of error.</b> A scan that returns <em>fewer</em> resets cannot fail an
/// absence-shaped assertion — a smaller set holds fewer offenders — so under-matching is the
/// silent failure and is guarded presence-shaped in <c>CleanElementScanIntegrityTests</c>, which
/// pins named pairs the scan must find, requires every <c>case</c> label to be represented, and
/// floors the total.
/// </para>
/// </remarks>
internal static class CleanElementScan
{
    /// <summary>
    /// The receiver name used for a reset at method scope — the declared type of
    /// <c>CleanElement</c>'s parameter, and the widest receiver any reset can have.
    /// </summary>
    internal const string RootReceiver = "FrameworkElement";

    private static readonly Lazy<ScanResult> Result = new(Scan);

    /// <summary>Every reset the scan recognized, in source order.</summary>
    internal static IReadOnlyList<CleanElementReset> Resets => Result.Value.Resets;

    /// <summary>
    /// The resets naming an <em>instance</em> dependency property — the exact complement of
    /// <see cref="AttachedResets"/> over the same matches, so the two partition
    /// <see cref="Resets"/> by construction.
    /// </summary>
    internal static IReadOnlyList<CleanElementReset> InstanceResets => Result.Value.Instance;

    /// <summary>The resets naming an <em>attached</em> dependency property.</summary>
    internal static IReadOnlyList<CleanElementReset> AttachedResets => Result.Value.Attached;

    /// <summary>The name of <c>CleanElement</c>'s <c>FrameworkElement</c> parameter.</summary>
    internal static string ParameterName => Result.Value.ParameterName;

    /// <summary>Local name → bound type name, for every pattern binding in the method.</summary>
    internal static IReadOnlyDictionary<string, string> BoundReceivers => Result.Value.Bindings;

    /// <summary>The types named by the <c>case T x:</c> labels of the type-specific dispatch.</summary>
    internal static IReadOnlyList<string> SwitchCaseReceivers => Result.Value.SwitchCases;

    /// <summary>Every <c>ClearValue</c> invocation in the method, however it is written.</summary>
    internal static int TotalClearValueCalls => Result.Value.TotalClearValueCalls;

    /// <summary>The subset of those the scan parsed into a <see cref="CleanElementReset"/>.</summary>
    internal static int RecognizedClearValueCalls => Result.Value.RecognizedClearValueCalls;

    /// <summary>
    /// Receiver names appearing in a reset that no binding accounts for. Always empty in a
    /// healthy tree; surfaced so the failure is reported rather than absorbed by a default.
    /// </summary>
    internal static IReadOnlyList<string> UnresolvedReceivers => Result.Value.Unresolved;

    /// <summary>
    /// The receiver types <c>CleanElement</c> resets <paramref name="property"/> on, as instance
    /// dependency properties.
    /// </summary>
    /// <remarks>
    /// This is the source of truth <c>ModifierInfo.PoolResetGate</c> mirrors. Deriving it from
    /// <c>ControlGate ∩ PoolableTypes</c> instead — as the parity test did before issue #1193 —
    /// asserts that "<c>ApplyModifiers</c> writes it here" implies "<c>CleanElement</c> clears it
    /// here", which holds for the #985 border-box family and is false in general.
    /// </remarks>
    internal static IReadOnlyCollection<string> ReceiversResetting(string property) =>
        InstanceResets
            .Where(reset => string.Equals(reset.Property, property, StringComparison.Ordinal))
            .Select(reset => reset.Receiver)
            .ToHashSet(StringComparer.Ordinal);

    private sealed record ScanResult(
        string ParameterName,
        IReadOnlyList<CleanElementReset> Resets,
        IReadOnlyList<CleanElementReset> Instance,
        IReadOnlyList<CleanElementReset> Attached,
        IReadOnlyDictionary<string, string> Bindings,
        IReadOnlyList<string> SwitchCases,
        IReadOnlyList<string> Unresolved,
        int TotalClearValueCalls,
        int RecognizedClearValueCalls);

    private static ScanResult Scan()
    {
        var method = ReadCleanElementMethod();
        var parameterName = method.ParameterList.Parameters[0].Identifier.Text;

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [parameterName] = RootReceiver,
        };

        // A DeclarationPatternSyntax is both `fe is Control c` and `case WinUI.Panel panel:`, so
        // one walk collects the FE-common narrowing chain (including the nested Grid / StackPanel
        // arms) and the type dispatch.
        foreach (var pattern in method.DescendantNodes().OfType<DeclarationPatternSyntax>())
        {
            if (pattern.Designation is SingleVariableDesignationSyntax designation)
                Bind(bindings, designation.Identifier.Text, SimpleTypeName(pattern.Type));
        }

        var switchCases = method.DescendantNodes()
            .OfType<SwitchStatementSyntax>()
            .Where(node => node.Expression is IdentifierNameSyntax id
                           && string.Equals(id.Identifier.Text, parameterName, StringComparison.Ordinal))
            .SelectMany(node => node.Sections)
            .SelectMany(section => section.Labels)
            .OfType<CasePatternSwitchLabelSyntax>()
            .Select(label => label.Pattern)
            .OfType<DeclarationPatternSyntax>()
            .Select(pattern => SimpleTypeName(pattern.Type))
            .ToList();

        var resets = new List<(int Position, CleanElementReset Reset)>();
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        var totalClearValueCalls = 0;
        var recognizedClearValueCalls = 0;

        foreach (var node in method.DescendantNodes())
        {
            switch (node)
            {
                // `receiver.PROP = value` — the shape `tb.FontSize = 14` and `fe.Tag = null` use.
                // `SimpleAssignmentExpression` only: a compound assignment (`x.P += v`) is not a
                // reset to a fixed value, and matching the kind through the pattern rather than a
                // separate `IsKind` call keeps the pattern variables definitely assigned.
                case AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                    Left: MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax assignReceiver,
                        Name: { } assignedProperty,
                    },
                } assignment:
                    Add(assignment.SpanStart, assignReceiver.Identifier.Text, owner: null,
                        assignedProperty.Identifier.Text, ResetShape.Assignment);
                    break;

                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax call:

                    if (string.Equals(call.Name.Identifier.Text, "ClearValue", StringComparison.Ordinal))
                    {
                        totalClearValueCalls++;

                        // `receiver.ClearValue(Owner.PROPProperty)`. The owner may carry any amount
                        // of qualification (Microsoft.UI.Xaml.Automation.AutomationProperties,
                        // WinUI.Border, Layout.FlexPanel); only its rightmost segment is kept,
                        // which is how ModifierTable.AttachedProperties is keyed and how the
                        // analyzer sees the owner at a call site.
                        if (call.Expression is IdentifierNameSyntax clearReceiver
                            && invocation.ArgumentList.Arguments.Count == 1
                            && invocation.ArgumentList.Arguments[0].Expression is MemberAccessExpressionSyntax dp
                            && dp.Name.Identifier.Text.EndsWith("Property", StringComparison.Ordinal))
                        {
                            recognizedClearValueCalls++;
                            var dependencyProperty = dp.Name.Identifier.Text;
                            Add(invocation.SpanStart, clearReceiver.Identifier.Text,
                                SimpleTypeName(dp.Expression),
                                dependencyProperty.Substring(0, dependencyProperty.Length - "Property".Length),
                                ResetShape.ClearValue);
                        }
                    }
                    // `receiver.PROP.Clear()` — Panel.Children, RichTextBlock.Blocks.
                    else if (string.Equals(call.Name.Identifier.Text, "Clear", StringComparison.Ordinal)
                             && invocation.ArgumentList.Arguments.Count == 0
                             && call.Expression is MemberAccessExpressionSyntax
                             {
                                 Expression: IdentifierNameSyntax collectionReceiver,
                             } collection)
                    {
                        Add(invocation.SpanStart, collectionReceiver.Identifier.Text, owner: null,
                            collection.Name.Identifier.Text, ResetShape.CollectionClear);
                    }

                    break;
            }
        }

        var ordered = resets.OrderBy(entry => entry.Position).Select(entry => entry.Reset).ToList();

        return new ScanResult(
            parameterName,
            ordered,
            ordered.Where(reset => !IsAttachedReset(reset)).ToList(),
            ordered.Where(IsAttachedReset).ToList(),
            bindings,
            switchCases,
            unresolved.ToList(),
            totalClearValueCalls,
            recognizedClearValueCalls);

        void Add(int position, string receiverName, string? owner, string property, ResetShape shape)
        {
            if (!bindings.TryGetValue(receiverName, out var receiverType))
            {
                unresolved.Add(receiverName);
                return;
            }

            resets.Add((position, new CleanElementReset(receiverType, owner, property, shape)));
        }
    }

    /// <summary>
    /// The rightmost segment of a possibly-qualified type or owner name — <c>Panel</c> for
    /// <c>WinUI.Panel</c>, <c>AutomationProperties</c> for
    /// <c>Microsoft.UI.Xaml.Automation.AutomationProperties</c>.
    /// </summary>
    private static string SimpleTypeName(Microsoft.CodeAnalysis.SyntaxNode node) => node switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        _ => node.ToString(),
    };

    /// <summary>
    /// Record a local → type binding, refusing to overwrite a different one.
    /// </summary>
    /// <remarks>
    /// Two bindings of one name to two types would make every reset naming it ambiguous, and the
    /// scan would silently attribute some of them to the wrong receiver — the exact failure this
    /// class exists to end. Fail at the binding instead, where the message can name both types.
    /// Legal C# (two disjoint scopes reusing a name) trips this, which is deliberate: the scan is
    /// flat, so a reused name is genuinely unresolvable here and must be renamed rather than
    /// silently resolved to whichever binding was walked first.
    /// </remarks>
    private static void Bind(Dictionary<string, string> bindings, string name, string type)
    {
        if (bindings.TryGetValue(name, out var existing) && !string.Equals(existing, type, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ElementPool.CleanElement binds the local '{name}' to both '{existing}' and " +
                $"'{type}'. Every reset naming '{name}' would be attributed to whichever binding " +
                "was walked first, so the REACTOR_POOL_001 consistency invariants would silently " +
                "check the wrong receiver. Rename one of the pattern variables.");
        }

        bindings[name] = type;
    }

    /// <summary>
    /// <c>CleanElement</c>'s declaration, parsed from <c>ElementPool.cs</c>.
    /// </summary>
    /// <remarks>
    /// Selected by name plus a single <c>FrameworkElement</c> parameter rather than by the literal
    /// <c>(FrameworkElement fe)</c> text, so a harmless rename of the parameter does not blind the
    /// scan — the parameter's own name is read back off the node. Requiring exactly one match is
    /// what stops an added overload from silently halving the scanned surface.
    /// </remarks>
    private static MethodDeclarationSyntax ReadCleanElementMethod()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        // Path.Join (vs Path.Combine) avoids the "rooted segment silently discards the base path"
        // behavior flagged by CodeQL cs/path-combine. All segments here are hardcoded literals.
        var path = Path.Join(root!, "src", "Reactor", "Core", "ElementPool.cs");
        Assert.True(File.Exists(path), $"ElementPool.cs not found at {path}");

        var methods = CSharpSyntaxTree.ParseText(File.ReadAllText(path))
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method =>
                method.Identifier.Text == "CleanElement"
                && method.ParameterList.Parameters.Count == 1
                && method.ParameterList.Parameters[0].Type is { } type
                && SimpleTypeName(type) == "FrameworkElement")
            .ToList();

        Assert.True(
            methods.Count == 1,
            $"Expected exactly one CleanElement(FrameworkElement) in ElementPool.cs, found {methods.Count}. " +
            "Every REACTOR_POOL_001 consistency invariant is stated over that method's body, so a " +
            "rename, an added overload, or a signature change silently empties all of them.");

        return methods[0];
    }

    // ── Attached vs. instance ───────────────────────────────────────────────

    private static bool IsAttachedReset(CleanElementReset reset) =>
        // Only a ClearValue names an owner. An assignment or collection clear is written against
        // the receiver itself, which is the instance shape by construction — an attached property
        // is written through the owner's static setter, never as `x.Prop = v`.
        reset.Owner is { } owner && IsAttachedReset(owner, reset.Property);

    /// <summary>
    /// Whether <c>OWNER.PROPProperty</c> names an <em>attached</em> dependency property — the one
    /// discriminator that splits the reset scan in two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owner membership alone cannot answer this, which is issue #1067: <c>Grid</c> is a
    /// <em>mixed</em> owner. <c>Grid.Padding</c> / <c>Grid.CornerRadius</c> are ordinary instance
    /// DPs (which is why <c>Grid</c> is in <see cref="InstancePropertyOwnerProbes"/>) while
    /// <c>Grid.Row</c> / <c>Column</c> / <c>RowSpan</c> / <c>ColumnSpan</c> are genuinely attached.
    /// Keyed by owner, a <c>Grid.Row</c> clear was absorbed by the instance bucket and
    /// <c>Every_Reset_Attached_Property_Is_Classified</c> never saw it — no failure, so no triage
    /// moment to get wrong. That is not hypothetical: <c>PanelAttachedHooks.ApplyGridAttached</c>
    /// already clears all four for pooled reuse, just outside the scanned region.
    /// </para>
    /// <para>
    /// So ask the property, not the owner, and ask it the same question the analyzer asks: an
    /// attached property is one whose owner declares the static
    /// <c>Owner.SetPROP(DependencyObject, value)</c> that <c>PoolResetSetAnalyzer</c> matches
    /// inside a <c>.Set(...)</c> lambda. <c>Grid.SetRow</c> exists, so <c>Grid.Row</c> is attached;
    /// there is no <c>Grid.SetPadding</c>, so <c>Grid.Padding</c> stays instance. A future mixed
    /// owner needs no edit here.
    /// </para>
    /// <para>
    /// Both error directions are not equal, and the bias is deliberate. Calling an instance
    /// property attached fails <c>Every_Reset_Attached_Property_Is_Classified</c> loudly and
    /// someone triages it; calling an attached property instance is silent — the whole bug. So
    /// anything unresolvable resolves to attached: an owner absent from the probe table is
    /// attached by default.
    /// </para>
    /// </remarks>
    internal static bool IsAttachedReset(string owner, string property) =>
        !InstancePropertyOwnerProbes.TryGetValue(owner, out var probe)
        || probe.DeclaresAttachedSetter(property);

    /// <summary>
    /// The <c>DependencyObject</c> base types that back <em>instance</em> properties in
    /// <c>CleanElement</c>, each paired with a metadata probe over the real type so
    /// <see cref="IsAttachedReset(string, string)"/> can carve the attached DPs back out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #985: CleanElement's FE-common block clears the Padding / CornerRadius /
    /// BorderThickness / BorderBrush / Background family through a Control | Border |
    /// Panel/Grid/StackPanel | TextBlock chain that mirrors ApplyModifiers' receivers.
    /// <c>Border.PaddingProperty</c> and friends are ordinary instance properties; without them
    /// here the attached scan would claim them and
    /// <c>Every_Reset_Attached_Property_Is_Classified</c> would fail on owners that have no
    /// business being in the attached table. <c>Grid</c> arrived with #1003, which widened those
    /// gates to the concrete panels; <c>TextBlock</c> because #985 moved TextBlock.Padding (added
    /// by #950) into the scanned block. <c>Viewbox</c>, <c>TextBox</c> and <c>ToggleSwitch</c>
    /// arrived with #1193, which extended the scan past the type dispatch and so reached their
    /// arms' clears for the first time.
    /// </para>
    /// <para>
    /// Two hazards live here and this list only ever closed the first. Route the family through
    /// <c>DeliberatelyExcludedAttached</c> instead and a future attached <c>Grid.*</c> reset does
    /// fail the classification test — but the two existing <c>Grid.*</c> suppression rows sitting
    /// right there invite the wrong triage ("add another row"). Route it through owner membership,
    /// as this list does, and that same future reset produces <em>no failure at all</em>, because
    /// bare-owner membership cannot express that <c>Grid</c> owns instance <em>and</em> attached
    /// DPs (#1067). Naming the owners is therefore necessary but not sufficient:
    /// <see cref="IsAttachedReset(string, string)"/> asks per property, and this list supplies the
    /// type it asks.
    /// </para>
    /// <para>
    /// Hand-listed as <c>typeof(...)</c> literals rather than resolved from a name via
    /// <c>Type.GetType</c>, so the probes stay statically analyzable (IL2057/IL2072) and adding an
    /// owner is a deliberate edit — the same rationale as <c>ModifierTableIntegrityTests</c>'
    /// <c>KnownAttachedOwners</c>. The key must equal the type's simple name, which is exactly how
    /// the scan spells an owner; <c>Every_Instance_Owner_Key_Names_Its_Own_Type</c> pins that.
    /// Reflection reads metadata only — no WinUI object is constructed, which this headless suite
    /// cannot do.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, (Type OwnerType, Func<string, bool> DeclaresAttachedSetter)>
        InstancePropertyOwnerProbes =
            new Dictionary<string, (Type, Func<string, bool>)>(StringComparer.Ordinal)
            {
                ["FrameworkElement"] = ProbeFor(typeof(Microsoft.UI.Xaml.FrameworkElement)),
                ["UIElement"] = ProbeFor(typeof(Microsoft.UI.Xaml.UIElement)),
                ["Control"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Control)),
                ["Border"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Border)),
                ["Panel"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Panel)),
                ["StackPanel"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.StackPanel)),
                ["Grid"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Grid)),
                ["TextBlock"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.TextBlock)),
                ["Viewbox"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Viewbox)),
                ["TextBox"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.TextBox)),
                ["ToggleSwitch"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.ToggleSwitch)),
            };

    /// <summary>
    /// One entry of <see cref="InstancePropertyOwnerProbes"/>, with the probe derived from the very
    /// <see cref="Type"/> stored beside it.
    /// </summary>
    /// <remarks>
    /// Naming the owner twice per entry — once for <c>OwnerType</c>, once inside the probe — would
    /// let the two drift apart, and <c>Every_Instance_Owner_Key_Names_Its_Own_Type</c> would not
    /// notice: it pins the key to <c>OwnerType</c> and never looks at what the probe reads. A pair
    /// like <c>(typeof(Grid), AttachedSetterProbe(typeof(StackPanel)))</c> answers "instance" for
    /// every attached <c>Grid</c> property, which is #1067 restored. For <c>Grid</c> and
    /// <c>Control</c> the probe theory catches it, but the other owners carry no
    /// attached-expecting row, so there the whole class of mistake is silent. Taking the type once
    /// and deriving both from it makes the mismatch inexpressible rather than merely tested for.
    /// </remarks>
    private static (Type OwnerType, Func<string, bool> DeclaresAttachedSetter) ProbeFor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
        => (type, AttachedSetterProbe(type));

    /// <summary>
    /// A probe over <paramref name="type"/>'s <c>public static void SetPROP(target, value)</c>
    /// declarations — the shape <c>PoolResetSetAnalyzer</c> matches, so this asks the rule's own
    /// question about a property rather than an approximation of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reflection pass runs once per owner, when this map initializes; the returned probe is
    /// an ordinal set lookup, so classifying a <c>ClearValue</c> match costs no reflection and the
    /// per-owner method list is never re-materialized.
    /// </para>
    /// <para>
    /// <c>FlattenHierarchy</c> is deliberate: an attached setter inherited from a base is still an
    /// attached setter, and the direction it can err in (instance read as attached) is the loud
    /// one. The one shape this misses is an attached property with <em>no</em> static setter — the
    /// <c>AutomationProperties.DescribedBy</c> collection form, which WinUI exposes as
    /// <c>GetXxx(...)</c> returning a mutable list. None of the owners above has one, and it is
    /// precisely the shape the <c>Owner.SetPROP(x, v)</c> rule cannot match either, which is why
    /// those three live in <c>ModifierTable.DeliberatelyExcludedAttached</c>.
    /// </para>
    /// <para>
    /// Deliberately <em>not</em> shared with <c>ModifierTableIntegrityTests.HasStaticTwoArgMethod</c>,
    /// which looks similar but asks a weaker question: it omits <c>FlattenHierarchy</c> and checks
    /// neither the <c>void</c> return nor that the first parameter is a <c>DependencyObject</c> —
    /// enough for the pure attached-property holders it runs against (<c>AutomationProperties</c>,
    /// <c>ToolTipService</c>, <c>TitleBar</c>, <c>FlexPanel</c>), where no instance member can
    /// collide. This probe runs against <em>mixed</em> owners, where a loose match silently
    /// reclassifies a property, so the extra constraints are the point. Reusing the looser helper
    /// here would reintroduce #1067 by a different route; unifying them would have to tighten it
    /// for callers that do not need it.
    /// </para>
    /// </remarks>
    private static Func<string, bool> AttachedSetterProbe(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
    {
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        var setterNames = type.GetMethods(Flags)
            .Where(method =>
                method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 2 } parameters
                && typeof(Microsoft.UI.Xaml.DependencyObject).IsAssignableFrom(parameters[0].ParameterType))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        return propertyName => setterNames.Contains("Set" + propertyName);
    }
}
