// Light edit #3 (spec 045 §4.2): expose vendored internals to the Reactor
// wrapper assembly and its test project so the wrapper can construct
// LayoutPanel / DocumentGroup / Document and call internal helpers from a
// reconcile pass.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Microsoft.UI.Reactor.Docking.Xaml")]
[assembly: InternalsVisibleTo("Reactor.Docking.Xaml.Tests")]
