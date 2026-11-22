namespace Microsoft.UI.Reactor.Cli;

// Issue #1664: simple assembly names are case-insensitive, so an app assembly named like an
// assembly Reactor ships shadows it at run time and crashes on startup. The build rejects it
// too (REACTOR_BUILD_001); `mur --create` refuses the name before scaffolding anything.
internal static class ProjectNameRules
{
    // Assemblies the Microsoft.UI.Reactor* packages put on an app's reference path, plus the
    // package ids themselves (a project named after a package id is confusing to restore).
    internal static readonly string[] ReservedNames =
    [
        "Reactor",
        "Reactor.Advanced",
        "Reactor.Interop.WinForms",
        "Reactor.Wrappers.Abstractions",
        "Microsoft.UI.Reactor",
        "Microsoft.UI.Reactor.Advanced",
        "Microsoft.UI.Reactor.Devtools",
    ];

    internal static bool CollidesWithReactorAssembly(string name) =>
        Array.Exists(ReservedNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
}
