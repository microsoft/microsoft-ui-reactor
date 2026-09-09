using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// The syntactic shape a reset is written in. Kept on every <see cref="CleanElementReset"/>
/// because the three are not interchangeable to a reader: <see cref="ClearValue"/> restores the
/// dependency-property default and lets Style setters win again (issue #952), while
/// <see cref="Assignment"/> pins a hardcoded literal and <see cref="CollectionClear"/> empties a
/// live collection. All three lose a user's <c>.Set(...)</c> write on pool return, which is the
/// only fact <c>REACTOR_POOL_001</c> reports — so all three must be scanned.
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
/// the <c>case TextBlock tb:</c> arm and were never checked against <c>ModifierTable</c> — and
/// <c>REACTOR_POOL_001</c> silently under-reported them for as long as they sat there.
/// </para>
/// <para>
/// <b>How the receiver is resolved.</b> Not by parsing regions, which would have to track brace
/// depth, <c>else if</c> chains, braceless single-statement <c>if</c> bodies and fallthrough-free
/// <c>case</c> arms, and would fail <em>quietly</em> by attributing a statement to the wrong
/// enclosing type. Instead every reset names its receiver as a local, and every local in this
/// method is introduced by exactly one binding: the method parameter, an <c>is T name</c>
/// pattern, or a <c>case T name:</c> label. So the map from local name to type is recovered
/// directly from the bindings and each reset is resolved through it. A receiver that resolves to
/// nothing is reported rather than defaulted — see
/// <see cref="CleanElementScanIntegrityTests.Every_Reset_Receiver_Resolves_To_A_Bound_Type"/> —
/// because defaulting is what would let a renamed local silently re-attribute a whole arm to
/// <c>FrameworkElement</c> and widen every derived gate.
/// </para>
/// <para>
/// <b>Comments are stripped before matching.</b> An assignment or clear written inside a comment
/// is not a reset, and this method's comments discuss the very properties being scanned
/// (<c>"Padding / CornerRadius / … / IsEnabled"</c>). Stripping is also what lets the scan drop
/// the old <c>^\s*switch</c> line anchor, which existed only to stop a comment mentioning the
/// dispatch from masquerading as the region boundary.
/// </para>
/// <para>
/// <b>Direction of error.</b> A scan that returns <em>fewer</em> resets cannot fail an
/// absence-shaped assertion — a smaller set holds fewer offenders — so under-matching is the
/// silent failure and is guarded presence-shaped in <see cref="CleanElementScanIntegrityTests"/>,
/// which pins named pairs the scan must find, requires every <c>case</c> label to be represented,
/// and floors the total.
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
    /// The resets naming an <em>instance</em> dependency property — every reset that is not
    /// <see cref="AttachedResets"/>, over the same matches, so the two partition
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

    /// <summary>Every <c>.ClearValue(</c> call in the method, however it is written.</summary>
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
    /// here", which holds for the #985 border-box family and is false for the fonts:
    /// <c>FontSize</c>'s gate is <c>Control | TextBlock</c> and only <c>TextBlock</c> is cleared.
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

    // ── Reset shapes ────────────────────────────────────────────────────────
    //
    // `(?:[\w.]+\.)?` before the owner absorbs whatever qualification the source uses
    // (Microsoft.UI.Xaml.Automation.AutomationProperties, WinUI.Border, Layout.FlexPanel); the
    // rightmost segment is how ModifierTable.AttachedProperties is keyed and how the analyzer
    // sees the owner at a call site.
    private const string ClearValuePattern =
        @"\b(\w+)\.ClearValue\(\s*(?:[\w.]+\.)?(\w+)\.(\w+)Property\s*\)";

    // `=` not followed by `=` and not preceded by one of `=!<>+-*/%&|^` excludes ==, !=, <=, >=,
    // => and every compound assignment, none of which is a reset to a fixed value.
    private const string AssignmentPattern =
        @"\b(\w+)\.(\w+)\s*(?<![=!<>+\-*/%&|^])=(?!=)";

    private const string CollectionClearPattern = @"\b(\w+)\.(\w+)\.Clear\(\s*\)";

    private static ScanResult Scan()
    {
        var body = ReadCleanElementBody(out var parameterName);
        var code = StripCommentsAndStrings(body);

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [parameterName] = RootReceiver,
        };

        // `is <Qualified.>Type name` — the FE-common narrowing chain, including the nested
        // Grid / StackPanel arms inside the Panel branch.
        //
        // The lookahead excludes the pattern-combinator keywords. `if (fe.Style is not null)` is
        // in this method today, and without it that reads as type `not` bound to name `null` — a
        // junk binding that a later `null.Something = …` could never use, but which would also
        // mask a genuine collision if a real type were ever named in a combinator position.
        // Rejecting at the binding keeps BoundReceivers a faithful list of what the method binds.
        foreach (Match match in Regex.Matches(code, @"\bis\s+(?!not\b|and\b|or\b|null\b)(?:[\w.]+\.)?(\w+)\s+(\w+)\b"))
            Bind(bindings, match.Groups[2].Value, match.Groups[1].Value);

        // `case <Qualified.>Type name:` — the type-specific dispatch.
        var switchCases = new List<string>();
        foreach (Match match in Regex.Matches(code, @"\bcase\s+(?:[\w.]+\.)?(\w+)\s+(\w+)\s*:"))
        {
            Bind(bindings, match.Groups[2].Value, match.Groups[1].Value);
            switchCases.Add(match.Groups[1].Value);
        }

        var resets = new List<(int Position, CleanElementReset Reset)>();
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        var recognizedClears = 0;

        foreach (Match match in Regex.Matches(code, ClearValuePattern))
        {
            recognizedClears++;
            Add(match.Index, match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, ResetShape.ClearValue);
        }

        foreach (Match match in Regex.Matches(code, AssignmentPattern))
        {
            // `receiver.ClearValue(...)` never matches (a call, not an assignment), but a
            // collection clear's own receiver segment would be picked up as `panel.Children`
            // by neither pattern — Clear() is a call too. Nothing to exclude here beyond the
            // operator filter already in the pattern.
            Add(match.Index, match.Groups[1].Value, owner: null, match.Groups[2].Value, ResetShape.Assignment);
        }

        foreach (Match match in Regex.Matches(code, CollectionClearPattern))
            Add(match.Index, match.Groups[1].Value, owner: null, match.Groups[2].Value, ResetShape.CollectionClear);

        var ordered = resets.OrderBy(entry => entry.Position).Select(entry => entry.Reset).ToList();

        return new ScanResult(
            parameterName,
            ordered,
            ordered.Where(reset => !IsAttachedReset(reset)).ToList(),
            ordered.Where(IsAttachedReset).ToList(),
            bindings,
            switchCases,
            unresolved.ToList(),
            Regex.Matches(code, @"\.ClearValue\s*\(").Count,
            recognizedClears);

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
    /// Record a local → type binding, refusing to overwrite a different one.
    /// </summary>
    /// <remarks>
    /// Two bindings of one name to two types would make every reset naming it ambiguous, and the
    /// scan would silently attribute some of them to the wrong receiver — the exact failure this
    /// class exists to end. Fail at the binding instead, where the message can name both types.
    /// </remarks>
    private static void Bind(Dictionary<string, string> bindings, string name, string type)
    {
        if (bindings.TryGetValue(name, out var existing) && !string.Equals(existing, type, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ElementPool.CleanElement binds the local '{name}' to both '{existing}' and " +
                $"'{type}'. Every reset naming '{name}' would be attributed to whichever binding " +
                "was scanned first, so the REACTOR_POOL_001 consistency invariants would silently " +
                "check the wrong receiver. Rename one of the pattern variables.");
        }

        bindings[name] = type;
    }

    /// <summary>
    /// Replace every comment with equivalent whitespace and every string/character literal with a
    /// blank of the same length, so match offsets stay faithful to the original text.
    /// </summary>
    /// <remarks>
    /// Blanking rather than deleting keeps <c>Regex.Match.Index</c> usable for source ordering.
    /// String contents are blanked as well as comments: <c>tb.Text = ""</c> is a reset the scan
    /// must see, but nothing inside a literal ever is, and a literal containing <c>x.Y =</c>
    /// would otherwise be matched as one.
    /// </remarks>
    internal static string StripCommentsAndStrings(string source)
    {
        var output = new StringBuilder(source.Length);
        var index = 0;

        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                while (index < source.Length && source[index] != '\n')
                    output.Append(source[index++] == '\t' ? '\t' : ' ');
                continue;
            }

            if (current == '/' && next == '*')
            {
                output.Append("  ");
                index += 2;
                while (index < source.Length && !(source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/'))
                {
                    output.Append(source[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                if (index < source.Length) { output.Append("  "); index += 2; }
                continue;
            }

            if (current == '@' && next == '"')
            {
                output.Append("  ");
                index += 2;
                while (index < source.Length)
                {
                    if (source[index] == '"')
                    {
                        // A doubled quote escapes itself inside a verbatim literal.
                        if (index + 1 < source.Length && source[index + 1] == '"') { output.Append("  "); index += 2; continue; }
                        output.Append(' '); index++; break;
                    }

                    output.Append(source[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                var quote = current;
                output.Append(' ');
                index++;
                while (index < source.Length && source[index] != quote)
                {
                    if (source[index] == '\\' && index + 1 < source.Length) { output.Append("  "); index += 2; continue; }
                    output.Append(source[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                if (index < source.Length) { output.Append(' '); index++; }
                continue;
            }

            output.Append(current);
            index++;
        }

        return output.ToString();
    }

    /// <summary>
    /// <c>CleanElement</c>'s complete body, brace-matched from the method's opening brace to its
    /// closing one, plus the name of its parameter.
    /// </summary>
    /// <remarks>
    /// Brace matching runs over comment- and string-blanked text so a brace inside either cannot
    /// unbalance the count. Matching the signature by shape rather than by the literal
    /// <c>(FrameworkElement fe)</c> keeps the scan robust to a harmless rename.
    /// </remarks>
    private static string ReadCleanElementBody(out string parameterName)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        // Path.Join (vs Path.Combine) avoids the "rooted segment silently discards the base path"
        // behavior flagged by CodeQL cs/path-combine. All segments here are hardcoded literals.
        var path = Path.Join(root!, "src", "Reactor", "Core", "ElementPool.cs");
        Assert.True(File.Exists(path), $"ElementPool.cs not found at {path}");
        var source = File.ReadAllText(path);

        var signature = Regex.Match(source,
            @"static\s+void\s+CleanElement\s*\(\s*FrameworkElement\s+(\w+)\s*\)");
        Assert.True(signature.Success,
            "Could not locate CleanElement(FrameworkElement) in ElementPool.cs — has it been " +
            "removed or had its parameter type changed?");
        parameterName = signature.Groups[1].Value;

        var open = source.IndexOf('{', signature.Index + signature.Length);
        Assert.True(open > signature.Index, "CleanElement opening brace not found");

        var blanked = StripCommentsAndStrings(source);
        var depth = 0;
        for (var index = open; index < blanked.Length; index++)
        {
            if (blanked[index] == '{') depth++;
            else if (blanked[index] == '}' && --depth == 0)
                return source.Substring(open, index - open + 1);
        }

        Assert.Fail("CleanElement's body is not brace-balanced — the scan cannot delimit it.");
        return string.Empty;
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
