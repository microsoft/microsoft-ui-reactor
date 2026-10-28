using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;
using static Microsoft.UI.Reactor.SourceMap.Tests.GaugeStacks;
using WinUI = Microsoft.UI.Xaml.Controls;
using GaugeAlias = Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — overload sets where the wrapper record is preferred over <c>Element</c>.
///
/// <para>The source-map generator re-binds a call that takes a wrapper factory's result with
/// an <c>Element</c>-typed placeholder, because the record's <c>: Element</c> base does not
/// exist yet in its view. For each method group below the real compilation picks an overload
/// the placeholder cannot reach; an interceptor for the overload the placeholder DOES reach
/// would be CS9144 and break this project's build. So the primary oracle is that this file
/// compiles; each test then pins that the real overload ran and that the call reads as
/// "location unknown" rather than as a wrongly-attributed site.</para>
///
/// <para>A separate file because the competing <c>using static GaugeStacks</c> changes
/// overload resolution for every <c>VStack</c> call in scope.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperFactoryOverloadTests : IDisposable
{
    public WrapperFactoryOverloadTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([global::System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;

    [ReactorSourceTransparent]
    internal static Element Pick(global::System.IEquatable<FakeGaugeElement> gauge) => HStack((FakeGaugeElement)gauge);

    [ReactorSourceTransparent]
    internal static Element Pick(object value) => VStack();

    [Fact]
    public void InterfaceTheRecordImplements_WinsOverObject()
    {
        var picked = Pick(FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)picked).Orientation);
        Assert.Null(picked.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Later(global::System.Func<FakeGaugeElement> make) => HStack(make());

    [ReactorSourceTransparent]
    internal static Element Later(global::System.Func<Element> make) => VStack(make());

    [Fact]
    public void DelegateReturningTheRecord_WinsOverDelegateReturningElement()
    {
        var later = Later(() => FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)later).Orientation);
        Assert.Null(later.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Soon(GaugeMaker<int> make) => HStack(make());

    [ReactorSourceTransparent]
    internal static Element Soon(global::System.Func<Element> make) => VStack(make());

    [Fact]
    public void GenericDelegateWhoseSignatureReturnsTheRecord_WinsOverFuncOfElement()
    {
        // `GaugeMaker<int>` mentions the record only in its invoke signature.
        var soon = Soon(() => FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)soon).Orientation);
        Assert.Null(soon.CallSite);
    }

    [Fact]
    public void MoreDerivedOverloadOnAQualifier_WinsOverTheInheritedOne()
    {
        // DerivedFrames.Frame(FakeGaugeElement) beats the inherited BaseFrames.Frame(Element),
        // which is all an Element placeholder can reach.
        var framed = DerivedFrames.Frame(FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)framed).Orientation);
        Assert.Null(framed.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Badge(GaugeBadge badge) => HStack(badge.Gauge);

    [ReactorSourceTransparent]
    internal static Element Badge(object value) => VStack();

    [Fact]
    public void UserDefinedConversionFromTheRecord_WinsOverObject()
    {
        var badge = Badge(FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)badge).Orientation);
        Assert.Null(badge.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Present(object value) => VStack();

    [ReactorSourceTransparent]
    internal static Element Present(Element child) => HStack(child);

    [Fact]
    public void ExplicitlyTypedWrapperValue_IsReBoundToTheOverloadTheRecordReaches()
    {
        // Here `typed` is a record that is not an Element yet, so Present(object) binds; the
        // real compilation picks Present(Element). The call is re-bound and stamped correctly.
        FakeGaugeElement typed = FakeGauge();
        var presented = Present(typed); var line = Line();

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)presented).Orientation);
        Assert.NotNull(presented.CallSite);
        Assert.Equal(line, presented.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void TypedDefaultAndCastOfTheRecord_AreReBoundToo()
    {
        // No NAME in these arguments has the record's type — only the expressions do.
        var fromDefault = Present(default(FakeGaugeElement)!); var defaultLine = Line();
        var fromCast = Present((FakeGaugeElement)FakeGauge()); var castLine = Line();

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)fromDefault).Orientation);
        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)fromCast).Orientation);
        Assert.Equal(defaultLine, fromDefault.CallSite!.Value.LineNumber);
        Assert.Equal(castLine, fromCast.CallSite!.Value.LineNumber);
    }

    internal static string Choose(Element value) => "element";

    internal static int Choose(FakeGaugeElement value) => 1;

    [ReactorSourceTransparent]
    internal static Element Show(string text) => VStack();

    [ReactorSourceTransparent]
    internal static Element Show(int number) => HStack();

    [Fact]
    public void NestedCallWhoseResultTypeDependsOnTheRecord_IsNotReBound()
    {
        // Choose(placeholder) would return string and pick Show(string); the real Choose
        // returns int and Show(int) runs.
        var shown = Show(Choose(FakeGauge()));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    [Fact]
    public void InferredWrapperLocalInsideANestedCall_IsPositionCheckedToo()
    {
        // `g` is inferred as the record, not a plain Element, so its use inside Choose(...) is
        // checked like a direct wrapper call would be: Choose's result type depends on it.
        var g = FakeGauge();
        var shown = Show(Choose(g));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    internal static string Measure(string text) => text;

    internal static int Measure<T>(T value) => 1;

    [Fact]
    public void ErrorDeepInsideAnArgument_IsNotTrustedEvenIfTheArgumentLooksTyped()
    {
        // The real call is Measure<FakeGaugeElement>, returning int, so Show(int) runs. Whatever
        // this compilation's error recovery makes of Measure(FakeGauge()), the generator must
        // decline rather than guess (today Roslyn leaves it ambiguous; the accounting invariant
        // in BindAroundWrapperFactoryCalls declines even if a future Roslyn picks one).
        var shown = Show(Measure(FakeGauge()));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Accept(string text) => VStack();

    [ReactorSourceTransparent]
    internal static Element Accept<T>(T value) => HStack();

    [Fact]
    public void UninferableWrapperLocal_DeclinesEvenWhenNothingElseChanged()
    {
        // A deconstructed local is not a declarator this generator infers through, so it stays
        // an error type. The real call is Accept<FakeGaugeElement>; the generator must decline
        // rather than trust whatever error recovery resolves Accept(...) to.
        var (deconstructed, _) = (FakeGauge(), 0);
        var accepted = Accept(deconstructed);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)accepted).Orientation);
        Assert.Null(accepted.CallSite);
    }

    internal static FakeGaugeElement Pick2(object value) => FakeGauge();

    internal static string Pick2(Element value) => "element";

    [ReactorSourceTransparent]
    internal static Element Display(Element child) => HStack(child);

    [ReactorSourceTransparent]
    internal static Element Display(string text) => VStack();

    [Fact]
    public void CallThatOnlyLooksLikeTheRecord_IsNotReplacedWhole()
    {
        // Here Pick2(typed) can only bind Pick2(object), which returns the record; in the real
        // compilation Pick2(Element) wins and returns a string, so Display(string) runs. Replacing
        // Pick2(typed) whole by its apparent type would bind Display(Element).
        FakeGaugeElement typed = FakeGauge();
        var shown = Display(Pick2(typed));

        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    [Fact]
    public void VarLocalThatIsTheRecordOnlyHere_IsNotTrusted()
    {
        // `intermediate` is the record here (Pick2(object) is all that binds) and a string in
        // the real compilation (Pick2(Element) wins), so Display(string) runs.
        FakeGaugeElement typed = FakeGauge();
        var intermediate = Pick2(typed);
        var shown = Display(intermediate);

        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    internal static FakeGaugeElement MakeGauge() => FakeGauge();

    [ReactorSourceTransparent]
    internal static Element From(global::System.Func<object> make) => VStack();

    [ReactorSourceTransparent]
    internal static Element From(global::System.Func<Element> make) => HStack();

    [Fact]
    public void MethodGroupWhoseSignatureNamesTheRecord_IsNotTrusted()
    {
        // MakeGauge converts to Func<object> only, here; to the better Func<Element> once the
        // record is an Element. A method group has no type, so only its signature tells.
        var made = From(MakeGauge);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)made).Orientation);
        Assert.Null(made.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Select<T>(int value) where T : Element => VStack();

    [ReactorSourceTransparent]
    internal static Element Select<T>(object value) => HStack();

    [Fact]
    public void ExplicitTypeArgumentNamingTheRecord_IsNotTrusted()
    {
        // `where T : Element` rules Select<T>(int) out here and in once the record is an
        // Element, where the exact int match wins.
        var selected = Select<FakeGaugeElement>(0);

        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)selected).Orientation);
        Assert.Null(selected.CallSite);
    }

    internal static string Content(object value) => value.ToString()!;

    internal static Element Content(Element value) => value;

    [Fact]
    public void VarLocalOfAnOrdinaryTypeDerivedFromTheRecord_IsNotTrusted()
    {
        // `content` is a string here (Content(object) is all that binds) and an Element in the
        // real compilation, where Button(Element, ...) is chosen over Button(string, ...).
        FakeGaugeElement gauge = FakeGauge();
        var content = Content(gauge);
        var button = Button(content);

        Assert.Null(button.CallSite);
    }

    [Fact]
    public void LambdaParameterTypedFromAWrapperDerivedSequence_IsNotTrusted()
    {
        // `item` is a string here and an Element in the real compilation.
        FakeGaugeElement gauge = FakeGauge();
        var content = Content(gauge);
        var buttons = new[] { content }.Select(item => Button(item)).ToArray();
        var parenthesized = new[] { content }.Select((item => Button(item))).ToArray();

        Assert.Null(buttons[0].CallSite);
        Assert.Null(parenthesized[0].CallSite);
    }

    internal static int UseEither(global::System.Func<string, global::System.DateTime> f) { f("text"); return 1; }

    internal static int UseEither(global::System.Func<Element, int> f) { f(TextBlock("text")); return 2; }

    internal static global::System.DateTime Produce(object value) => default;

    internal static int Produce(Element value) => 0;

    [Fact]
    public void LambdaParameterTypedByTheLambdasOwnBody_IsNotTrusted()
    {
        // Here Produce(gauge) is a DateTime, so UseEither(Func<string, DateTime>) is chosen and
        // `item` is a string; once the record is an Element, Produce(gauge) is an int, only
        // UseEither(Func<Element, int>) fits, and Display(Element) runs.
        FakeGaugeElement gauge = FakeGauge();
        Element? shown = null;
        var which = UseEither(item => { shown = Display(item); return Produce(gauge); });

        Assert.Equal(2, which);
        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown!).Orientation);
        Assert.Null(shown!.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element ShowNumber(byte value) => VStack();

    [ReactorSourceTransparent]
    internal static Element ShowNumber(long value) => HStack();

    [Fact]
    public void InferredIntegralLocal_IsNotTreatedAsAConstant()
    {
        // `n` is a non-constant int, so ShowNumber(long) is chosen; a constant zero standing
        // in for it would convert to byte and pick ShowNumber(byte) instead.
        FakeGaugeElement gauge = FakeGauge();
        var n = gauge.GetHashCode();
        var line = Line(); var shown = ShowNumber(n);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Equal(line, shown.CallSite?.LineNumber);
    }

    [ReactorSourceTransparent]
    internal static Element PickBy<T>(int value) where T : global::System.Collections.Generic.IComparer<FakeGaugeElement> => HStack();

    [ReactorSourceTransparent]
    internal static Element PickBy<T>(object value) => VStack();

    [Fact]
    public void CandidateConstrainedByTheRecord_IsNotTrusted()
    {
        // IComparer<Element> satisfies `where T : IComparer<FakeGaugeElement>` only once the
        // record is an Element (contravariance), which brings in the exact PickBy<T>(int).
        var picked = PickBy<global::System.Collections.Generic.IComparer<Element>>(0);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)picked).Orientation);
        Assert.Null(picked.CallSite);
    }

    internal static Element MakeBy<T>(string value) where T : global::System.Collections.Generic.IComparer<FakeGaugeElement> => TextBlock(value);

    internal static string MakeBy<T>(object value) => "object";

    internal static TResult ApplyTo<TResult>(global::System.Func<string, TResult> make) => make("text");

    [Fact]
    public void MethodGroupConstrainedByTheRecord_IsNotTrusted()
    {
        // ApplyTo infers TResult from the group's best method for a string: MakeBy<T>(object),
        // a string, here, where the constraint fails; MakeBy<T>(string), an Element, once the
        // record is complete — so Display(Element) runs.
        var shown = Display(ApplyTo(MakeBy<global::System.Collections.Generic.IComparer<Element>>));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element ShowAll(Element first, object rest) => VStack(first);

    [ReactorSourceTransparent]
    internal static Element ShowAll(Element first, global::System.Collections.Generic.IEnumerable<Element> rest) => HStack(first);

    [Fact]
    public void NestedTypeOfARecordClosedGeneric_IsNotTrusted()
    {
        // ValueCollection names the record only through its containing type's arguments; it is
        // an IEnumerable<Element> by covariance once the record is an Element.
        FakeGaugeElement gauge = FakeGauge();
        global::System.Collections.Generic.Dictionary<int, FakeGaugeElement>.ValueCollection values =
            new global::System.Collections.Generic.Dictionary<int, FakeGaugeElement> { [1] = gauge }.Values;
        var shown = ShowAll(gauge, values);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    internal static Outer<T>.Nested Box<T>(T value) => new();

    [ReactorSourceTransparent]
    internal static Element ShowNested(object value) => VStack();

    [ReactorSourceTransparent]
    internal static Element ShowNested(Outer<FakeGaugeElement>.Nested nested) => HStack();

    [Fact]
    public void NestedTypeOfAGenericOverTheRecord_IsNotTrusted()
    {
        // Box(FakeGauge()) is an Outer<FakeGaugeElement>.Nested, which only the second
        // ShowNested takes; an Element placeholder would make it Outer<Element>.Nested.
        var shown = ShowNested(Box(FakeGauge()));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    [Fact]
    public void ModifierWithAnExplicitTypeArgument_KeepsItsWiderType()
    {
        // Identity<object>() makes `boxed` an object in both compilations, so Present(object)
        // runs; following the receiver's type instead would bind Present(Element).
        var boxed = FakeGauge().Identity<object>();
        var shown = Present(boxed);

        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)shown).Orientation);
    }

    [ReactorSourceTransparent]
    internal static Element PresentMaybe(object? value) => VStack();

    [ReactorSourceTransparent]
    internal static Element PresentMaybe(Element? child) => HStack(child);

    [Fact]
    public void AsExpressionToTheRecord_IsReBoundAndStamped()
    {
        // `boxed as FakeGaugeElement` binds PresentMaybe(object?) here; PresentMaybe(Element?)
        // for real. A bare `as` (no `!`), so nothing but the operator itself names the record.
        object boxed = FakeGauge();
        var presented = PresentMaybe(boxed as FakeGaugeElement); var line = Line();

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)presented).Orientation);
        Assert.Equal(line, presented.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void AliasOfTheRecord_IsEnoughToMarkAMemberAsTouchingWrappers()
    {
        // The only token naming the record here is an alias, so the per-member fast path must
        // know aliases: otherwise PresentMaybe(object?) — all that binds here — is intercepted
        // while the real call is PresentMaybe(Element?).
        var presented = PresentMaybe(default(GaugeAlias)); var line = Line();

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)presented).Orientation);
        Assert.Equal(line, presented.CallSite!.Value.LineNumber);
    }

    internal static void Read(object source, out string value) => value = "object";

    internal static void Read(Element source, out Element value) => value = source;

    [Fact]
    public void OutVarTypedByAnOverloadThatDependsOnTheRecord_IsNotTrusted()
    {
        // `value` is a string here (Read(object, ...) is all that binds) and an Element in the
        // real compilation, where Button(Element, ...) is chosen.
        FakeGaugeElement gauge = FakeGauge();
        Read(gauge, out var value);
        var button = Button(value);

        Assert.Null(button.CallSite);
    }

    [Fact]
    public void QueryRangeVariableTypedFromAWrapperDerivedSequence_IsNotTrusted()
    {
        FakeGaugeElement gauge = FakeGauge();
        var content = Content(gauge);
        var buttons = (from item in new[] { content } select Button(item)).ToArray();

        Assert.Null(buttons[0].CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Pair(Element first, string second) => VStack(first);

    [ReactorSourceTransparent]
    internal static Element Pair(Element first, object second) => HStack(first);

    [Fact]
    public void LocalBeyondTheInferenceDepth_AbandonsTheReBinding()
    {
        // `e` is a record five locals deep, beyond the inference limit, next to a substituted
        // first argument. Only Pair(Element, object) applies in the real compilation. Today Roslyn
        // also ranks the error-typed `e` as ambiguous between the two, so this is defence in
        // depth: the rewrite fails closed on its own (depth exhaustion, residual error) rather
        // than relying on how an error type happens to be ranked.
        var a = FakeGauge();
        var b = a;
        var c = b;
        var d = c;
        var e = d;
        var pair = Pair(FakeGauge(), e);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)pair).Orientation);
        Assert.Null(pair.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Many(global::System.Collections.Generic.IEnumerable<Element> children) => HStack([.. children]);

    [ReactorSourceTransparent]
    internal static Element Many(object value) => VStack();

    [Fact]
    public void ValueWhoseTypeContainsTheRecord_AbandonsTheReBinding()
    {
        // List<FakeGaugeElement> reaches IEnumerable<Element> by variance only once the record
        // is an Element; no placeholder can stand in for that.
        var many = Many(new global::System.Collections.Generic.List<FakeGaugeElement> { FakeGauge() });

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)many).Orientation);
        Assert.Null(many.CallSite);
    }

    internal static string Choose3<T>(long value) => "plain";

    internal static Element Choose3<T>(int value) where T : Element => TextBlock("element");

    [Fact]
    public void NestedCallWithAnExplicitWrapperTypeArgument_IsNotTrusted()
    {
        // Here `where T : Element` rules Choose3<T>(int) out, so the call is a string and
        // Display(string) binds; once the record is an Element the exact int match wins and
        // Display(Element) runs. Nothing in the call is a wrapper VALUE.
        var shown = Display(Choose3<FakeGaugeElement>(0));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Pair2(Element first, object second) => VStack(first);

    [ReactorSourceTransparent]
    internal static Element Pair2(Element first, Element second) => HStack(first, second);

    internal static Element PairWith<T>(T child) where T : FakeGaugeElement => Pair2(FakeGauge(), child);

    [Fact]
    public void ValueOfATypeParameterConstrainedToTheRecord_IsNotTrusted()
    {
        // `child` is a T bounded by the record: an object here, an Element in the real
        // compilation, which picks Pair2(Element, Element).
        var paired = PairWith(FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)paired).Orientation);
        Assert.Null(paired.CallSite);
    }

    internal sealed class GaugeHolder
    {
        public FakeGaugeElement Child { get; } = FakeGauge();
    }

    internal sealed class TextHolder
    {
        public string Child => "text";
    }

    internal static GaugeHolder LoadHolder(object value) => new();

    internal static TextHolder LoadHolder(Element value) => new();

    [Fact]
    public void MemberOfAReceiverThatDependsOnTheRecord_IsNotReplacedWhole()
    {
        // Here LoadHolder(gauge) can only bind LoadHolder(object), whose Child is the record;
        // in the real compilation LoadHolder(Element) wins and Child is a string.
        FakeGaugeElement gauge = FakeGauge();
        var shown = Display(LoadHolder(gauge).Child);

        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    internal static FakeGaugeElement[] Children(object value) => [FakeGauge()];

    internal static string[] Children(Element value) => ["text"];

    [Fact]
    public void ImplicitLambdaParameterThatIsTheRecordOnlyHere_IsNotTrusted()
    {
        // `item` is the record here (Children(object) is all that binds) and a string in the
        // real compilation, where Children(Element) wins.
        FakeGaugeElement gauge = FakeGauge();
        var shown = Children(gauge).Select(item => Display(item)).ToArray();

        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)shown[0]).Orientation);
        Assert.Null(shown[0].CallSite);
    }

    [Fact]
    public void VarLocalOfAWrapperCastToObject_IsAnObject()
    {
        // The cast fixes the local's type: Present(object) in both compilations, so the call
        // is re-bound to it and stamped.
        var boxed = (object)FakeGauge();
        var line = Line(); var shown = Present(boxed);

        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)shown).Orientation);
        Assert.NotNull(shown.CallSite);
        Assert.Equal(line, shown.CallSite!.Value.LineNumber);
    }

    internal static object MakeAny(object value) => value;

    internal static Element MakeAny(Element value) => value;

    [ReactorSourceTransparent]
    internal static Element From2(global::System.Func<FakeGaugeElement, object> make) => VStack();

    [ReactorSourceTransparent]
    internal static Element From2(global::System.Func<FakeGaugeElement, Element> make) => HStack();

    [Fact]
    public void MethodGroupConvertedToADelegateOverTheRecord_IsNotTrusted()
    {
        // Here only MakeAny(object) takes a FakeGaugeElement, so MakeAny converts to
        // Func<FakeGaugeElement, object> alone; once the record is an Element, MakeAny(Element)
        // converts to the better Func<FakeGaugeElement, Element>. Neither MakeAny names the record.
        var made = From2(MakeAny);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)made).Orientation);
        Assert.Null(made.CallSite);
    }

    [Fact]
    public void ValueWithAConversionToTheRecord_IsNotTrusted()
    {
        // GaugeBox -> FakeGaugeElement is user-defined; once the record is an Element that
        // conversion reaches Element too, and Pair2(Element, Element) wins over (Element, object).
        FakeGaugeElement gauge = FakeGauge();
        var paired = Pair2(gauge, new GaugeBox());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)paired).Orientation);
        Assert.Null(paired.CallSite);
    }

    private static readonly bool s_flag = true;

    [Fact]
    public void VarLocalOfAConditionalWideningTheRecord_IsItsWiderType()
    {
        // Both locals are objects, so Present(object) runs and the calls are stamped with it.
        FakeGaugeElement gauge = FakeGauge();
        var mixed = s_flag ? gauge : new object();
        var parenthesized = (s_flag ? gauge : new object());
        var line = Line(); var first = Present(mixed); var second = Present(parenthesized);

        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)first).Orientation);
        Assert.Equal(WinUI.Orientation.Vertical, ((StackElement)second).Orientation);
        Assert.Equal(line, first.CallSite?.LineNumber);
        Assert.Equal(line, second.CallSite?.LineNumber);
    }

    [ReactorSourceTransparent]
    internal static Element ShowCrate(ICrate crate) => HStack();

    [ReactorSourceTransparent]
    internal static Element ShowCrate(object value) => VStack();

    [Fact]
    public void ConditionalWhoseOtherArmTheRecordConvertsTo_IsNotTrusted()
    {
        // The record converts to GaugeCrate (user-defined), so the conditional is a
        // GaugeCrate and ShowCrate(ICrate) runs; with an Element in the record's place,
        // GaugeCrate -> Element makes it an Element, which only ShowCrate(object) takes.
        FakeGaugeElement gauge = FakeGauge();
        var shown = ShowCrate(s_flag ? gauge : new GaugeCrate());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Wrap(Element first, params Element[] rest) => HStack([first, .. rest]);

    [Fact]
    public void ArgumentThatConvertsToTheParamsArray_IsNotStampedAsOneOfItsElements()
    {
        // Duo converts to Element AND to Element[]; C# then passes it in normal form, so
        // `rest` is Duo's own (empty) array and must not be written into as rest[0].
        FakeGaugeElement gauge = FakeGauge();
        var line = Line(); var wrapped = Wrap(gauge, new Duo());

        Assert.Equal(line, wrapped.CallSite?.LineNumber);
    }

    [Fact]
    public void CompetingUsingStaticOverload_WinsOverFactories()
    {
        // GaugeStacks.VStack(double, params FakeGaugeElement[]) is more specific than
        // Factories.VStack(double, params Element?[]) for an all-gauge argument list.
        var stack = VStack(8, FakeGauge());

        Assert.True(GaugeStacks.Called);
        Assert.Null(stack.CallSite);
    }

    [Fact]
    public void WithAWrapperSpecificCompetitorInScope_ReBindingDeclinesConservatively()
    {
        // The competitor cannot take a TextBlock, so the real call goes to Factories, but
        // telling that apart would mean re-implementing overload resolution against types
        // that do not exist yet. Declining costs a location; guessing could cost the build.
        var mixed = VStack(8, TextBlock("x"), FakeGauge());
        Assert.Null(mixed.CallSite);

        // Positive control in the same file: a call that needs no substitution binds as
        // usual and is stamped, so the nulls above are the guard and not a dead generator.
        var plain = VStack(8, TextBlock("y"));
        Assert.NotNull(plain.CallSite);
        Assert.EndsWith("WrapperFactoryOverloadTests.cs", plain.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }
}

/// <summary>Converts TO the record, so through it to Element once the record is complete.</summary>
public sealed class GaugeBox
{
    public static implicit operator FakeGaugeElement(GaugeBox box) => FakeGauge();
}

public interface ICrate
{
}

/// <summary>The record converts to it, and it converts to Element.</summary>
public sealed class GaugeCrate : ICrate
{
    public static implicit operator GaugeCrate(FakeGaugeElement gauge) => new();

    public static implicit operator Element(GaugeCrate crate) => Microsoft.UI.Reactor.Factories.TextBlock("crate");
}

/// <summary>Converts to a single Element and to a whole (empty) Element[].</summary>
public sealed class Duo
{
    public static implicit operator Element(Duo duo) => Microsoft.UI.Reactor.Factories.TextBlock("single");

    public static implicit operator Element[](Duo duo) => [];
}

/// <summary>A modifier-shaped extension, <c>T M&lt;T&gt;(this T)</c>.</summary>
public static class IdentityExtensions
{
    public static T Identity<T>(this T value) => value;
}

/// <summary>A generic type with a nested type, which carries the outer type's arguments.</summary>
public class Outer<T>
{
    public sealed class Nested
    {
    }
}

/// <summary>A generic delegate whose signature, not its type arguments, names the record.</summary>
public delegate FakeGaugeElement GaugeMaker<T>();

/// <summary>Converts FROM the record, which an Element placeholder cannot.</summary>
public sealed class GaugeBadge
{
    public GaugeBadge(FakeGaugeElement gauge) => Gauge = gauge;

    public FakeGaugeElement Gauge { get; }

    public static implicit operator GaugeBadge(FakeGaugeElement gauge) => new(gauge);
}

/// <summary>Holds the overload a placeholder can reach.</summary>
public class BaseFrames
{
    [ReactorSourceTransparent]
    public static Element Frame(Element child) => Microsoft.UI.Reactor.Factories.VStack(child);
}

/// <summary>Adds the more specific overload the real compilation picks.</summary>
public class DerivedFrames : BaseFrames
{
    [ReactorSourceTransparent]
    public static Element Frame(FakeGaugeElement gauge) => Microsoft.UI.Reactor.Factories.HStack(gauge);
}

/// <summary>A user-defined stack factory competing with <c>Factories.VStack</c>.</summary>
public static class GaugeStacks
{
    public static bool Called { get; private set; }

    public static Element VStack(double spacing, params FakeGaugeElement[] children)
    {
        Called = true;
        return Microsoft.UI.Reactor.Factories.VStack(spacing, children);
    }
}