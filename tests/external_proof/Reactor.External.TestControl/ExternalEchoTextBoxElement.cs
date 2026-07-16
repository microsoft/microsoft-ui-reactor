using Microsoft.UI.Reactor.Wrappers;

namespace Reactor.External.TestControl;

// Spec 062 §14 — the stable-ABI proof for issue #163.
//
// This partial record is filled in by Reactor.Wrappers.Generator: it emits the
// Optional<string> Text init-prop, the OnTextChanged callback, the deferred
// (suppress-counter) HandCodedControlled descriptor entry + trampoline, the
// Pattern-A self-registration cctor, and the EchoTextBox(...) factory.
//
//   * AutoDiscover = false + Include = { "Text" }  →  surface EXACTLY the one
//     coercing value prop (line 665 of the generator drops any prop that is
//     neither auto-discovered nor explicitly Included, so [WrapControlled] alone
//     is not enough to surface it — Include is required).
//   * [WrapControlled("Text", Deferred = true)]    →  route it through the
//     DEFERRED echo channel, whose generated trampoline gates on the PUBLIC
//     ReactorBinding.ShouldSuppressEcho primitive. If that primitive were still
//     internal, THIS assembly (no InternalsVisibleTo from Reactor.dll) would not
//     compile — so a green build is the proof the read-side ABI gap is closed.
//   * RegisterAssembly = false                     →  EchoTextBox is a pure-code
//     control with no IXamlMetadataProvider; skip the RegisterControlAssembly
//     call (issue #142) that only third-party XAML control libraries need.
[GenerateReactorWrapper(typeof(EchoTextBox), AutoDiscover = false, Include = new[] { "Text" }, RegisterAssembly = false)]
[WrapControlled("Text", Deferred = true)]
public partial record ExternalEchoTextBoxElement;
