using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Reactor.External.TestControl.Tests;

/// <summary>
/// Spec 062 §14 — stable-ABI proof for issue #163, from a genuinely EXTERNAL
/// assembly. <see cref="ExternalEchoTextBoxElement"/> is a source-generated
/// <c>[GenerateReactorWrapper]</c> + <c>[WrapControlled("Text", Deferred = true)]</c>
/// wrapper whose generated deferred-controlled trampoline gates the echo on the
/// PUBLIC <c>ReactorBinding.ShouldSuppressEcho</c> primitive.
///
/// <para>The very fact that this assembly compiles — the wrapper generator runs
/// here against <see cref="EchoTextBox"/> with NO <c>InternalsVisibleTo</c> from
/// Reactor.dll — is the proof the read-side echo primitive is public: before it
/// was, the generated code named the internal <c>ChangeEchoSuppressor</c> and this
/// project failed with CS0122. The live echo round-trip (framework write is
/// suppressed, user edit fires) needs a WinUI dispatcher and lives in
/// <c>Reactor.AppTests.Host</c> as <c>Spec062DeferredControlled_Echo</c>.</para>
/// </summary>
public class ExternalEchoBoxWrapperSelftests
{
    [Fact]
    public void GeneratedFactory_SurfacesControlledValue_AndCallback()
    {
        // The generated factory + init props exist and carry the controlled
        // Optional<string> value and its On{Prop}Changed callback. Constructing the
        // record is headless-safe (no WinUI control is created until mount) and
        // triggers the generated Pattern-A registration static cctor.
        var el = ExternalEchoTextBoxElement.EchoTextBox("hi", _ => { });

        Assert.IsAssignableFrom<Element>(el);
        Assert.True(el.Text.HasValue);
        Assert.Equal("hi", el.Text.Value);
        Assert.NotNull(el.OnTextChanged);
    }

    [Fact]
    public void GeneratedControlledValue_DefaultsToUnset()
    {
        // Controlled props default to Optional.Unset so the control owns the value
        // and user input survives re-renders unless an explicit value is provided.
        var el = ExternalEchoTextBoxElement.EchoTextBox();

        Assert.False(el.Text.HasValue);
        Assert.Null(el.OnTextChanged);
    }

    [Fact]
    public void GeneratedWrapper_LivesInExternalAssembly_NoInternalsVisibleTo()
    {
        // Guard the "external" in external-proof: the generated element must be
        // compiled into THIS assembly, not Reactor.dll — otherwise the no-IVT ABI
        // proof is vacuous. (typeof does not construct a WinUI object.)
        var asm = typeof(ExternalEchoTextBoxElement).Assembly.GetName().Name;
        Assert.Equal("Reactor.External.TestControl", asm);
    }
}
